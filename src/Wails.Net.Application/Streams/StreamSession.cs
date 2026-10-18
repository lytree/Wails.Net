using System.Text;

namespace Wails.Net.Application.Streams;

/// <summary>
/// <see cref="StreamSession.Open"/> 的结果。
/// </summary>
internal enum SessionOpenResult
{
    /// <summary>已接受：ack 已入队，handler 已在后台启动。</summary>
    Ok,

    /// <summary>连接表 / 控制额度已满（端点应回 429）。</summary>
    Full,

    /// <summary>会话已关闭（端点应回 410）。</summary>
    Closed,

    /// <summary>连接 id 重复（端点应回 409）。</summary>
    Duplicate,
}

/// <summary>
/// <see cref="StreamSession.AwaitFramesAsync"/> 的结果。
/// </summary>
/// <param name="Frames">本次投递的帧；无帧时为空列表。</param>
/// <param name="More">队列中是否仍有未投递帧。</param>
/// <param name="Alive">会话是否仍然存活（false 表示已被关闭，端点应回 410）。</param>
internal readonly record struct SessionPollResult(List<OutFrame> Frames, bool More, bool Alive);

/// <summary>
/// 一个窗口中的一次页面加载。该页面打开的所有连接都复用在会话的单个持锁 poll 上。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream_session.go</c> 中的 <c>streamSession</c>。
/// <para>
/// 一个 poll 一次、一个队列、一个 drainer —— 顺序性因此由构造本身保证而非靠论证：
/// 帧按追加顺序离开，无论哪个 goroutine / 线程追加；且绝不存在第二个可能超车的投递机制。
/// </para>
/// </summary>
internal sealed class StreamSession : IStreamSink
{
    /// <summary>保护出站队列、连接表与记账的锁。</summary>
    private readonly object _lock = new();

    /// <summary>出站帧队列。</summary>
    private readonly List<OutFrame> _out = new();

    /// <summary>出站队列当前字节数。</summary>
    private int _outBytes;

    /// <summary>出站队列当前数据帧数。</summary>
    private int _outDataFrames;

    /// <summary>出站队列当前非 close 控制帧数。</summary>
    private int _outControls;

    /// <summary>出站队列当前 close 帧数。</summary>
    private int _outCloses;

    /// <summary>连接表：连接 id → 连接。</summary>
    private readonly Dictionary<uint, StreamConn> _conns = new();

    /// <summary>是否已关闭。</summary>
    private bool _closed;

    /// <summary>等待出站队列空间的生产者唤醒门。</summary>
    private readonly PulseGate _space = new();

    /// <summary>parked poll 的「有变化」信号（容量 1，突发折叠为一次唤醒）。</summary>
    private readonly SemaphoreSlim _notify = new(0, 1);

    /// <summary>poll 代次，用于让新 poll 退休更早的 poll。</summary>
    private ulong _pollGen;

    /// <summary>最近一次 poll / touch 的时刻（用于 TTL 回收）。</summary>
    private DateTimeOffset _lastSeen;

    /// <summary>分片重组存储（首次使用时创建）。</summary>
    private StreamChunkStore? _chunkStore;

    /// <summary>所属管理器。</summary>
    private readonly StreamManager _manager;

    /// <summary>
    /// 构造会话。
    /// </summary>
    /// <param name="id">session id（客户端生成）。</param>
    /// <param name="windowId">所属窗口 id。</param>
    /// <param name="generation">页面代次。</param>
    /// <param name="manager">所属管理器。</param>
    public StreamSession(string id, uint windowId, ulong generation, StreamManager manager)
    {
        Id = id;
        WindowId = windowId;
        Generation = generation;
        _manager = manager;
        _lastSeen = DateTimeOffset.UtcNow;
    }

    /// <summary>session id。</summary>
    public string Id { get; }

    /// <summary>所属窗口 id。</summary>
    public uint WindowId { get; }

    /// <summary>
    /// 页面代次。首次创建时由客户端指定且不再改变；一个 poll 只能退休更早的代次，
    /// 使上一页面的延迟请求无法拆掉它的继任者。
    /// </summary>
    public ulong Generation { get; }

    /// <summary>刷新最近活动时刻。</summary>
    public void Touch()
    {
        lock (_lock)
        {
            _lastSeen = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>读取最近活动时刻。</summary>
    /// <returns>最近活动时刻。</returns>
    public DateTimeOffset LastSeenAt()
    {
        lock (_lock)
        {
            return _lastSeen;
        }
    }

    /// <summary>当前仍持有的连接数。</summary>
    /// <returns>连接数。</returns>
    public int ConnCount()
    {
        lock (_lock)
        {
            return _conns.Count;
        }
    }

    /// <summary>按 id 查连接。</summary>
    /// <param name="connId">连接 id。</param>
    /// <returns>连接；不存在时返回 null。</returns>
    public StreamConn? Conn(uint connId)
    {
        lock (_lock)
        {
            return _conns.TryGetValue(connId, out var conn) ? conn : null;
        }
    }

    /// <summary>取得分片重组存储（首次使用时创建）。</summary>
    /// <returns>分片重组存储。</returns>
    public StreamChunkStore Chunks()
    {
        lock (_lock)
        {
            if (_chunkStore is null)
            {
                _chunkStore = new StreamChunkStore(_closed, _manager);
            }

            return _chunkStore;
        }
    }

    /// <summary>
    /// 接受前端的连接请求。ack 在 handler 启动之前入队，使前端不会看到尚未告知已打开的流的数据。
    /// 对应 Go <c>streamSession.open</c>。
    /// </summary>
    /// <param name="connId">连接 id。</param>
    /// <param name="name">流名。</param>
    /// <returns>处理结果。</returns>
    public SessionOpenResult Open(uint connId, string name)
    {
        var handler = _manager.GetHandler(name);
        if (handler is null)
        {
            var reason = Encoding.UTF8.GetBytes("no handler registered for stream " + name);
            return TryEnqueue(null, connId, StreamFrameKind.Error, reason)
                ? SessionOpenResult.Ok
                : SessionOpenResult.Full;
        }

        if (!_manager.ReserveOpen())
        {
            return SessionOpenResult.Full;
        }

        var conn = new StreamConn(connId, name, WindowId, this, _manager, lifecycle: true);
        lock (_lock)
        {
            if (_closed)
            {
                conn.Cancel();
                _manager.ReleaseOpenReservation();
                return SessionOpenResult.Closed;
            }

            if (_conns.ContainsKey(connId))
            {
                // 前端分配连接 id；重复意味着客户端有 bug 或恶意，拒绝而非替换活连接。
                conn.Cancel();
                _manager.ReleaseOpenReservation();
                return SessionOpenResult.Duplicate;
            }

            // 仍排在队列中的 close 帧在其连接槽上直到前端将其取走。该不变量即便在短生命周期
            // 连接 id 被复用时也能为每个已接受连接保留足够的 close 帧容量。
            if (_conns.Count + _outCloses >= StreamConstants.MaxConnections ||
                _outControls >= StreamConstants.OutControlDepth)
            {
                conn.Cancel();
                _manager.ReleaseOpenReservation();
                return SessionOpenResult.Full;
            }

            _conns[connId] = conn;
            // 在与注册相同的临界区内排队 ack：poll 因此绝不会在数据请求能找到连接之前投递 ack，
            // 且竞争的 open 无法消费其保留槽。
            _out.Add(new OutFrame(connId, StreamFrameKind.Open, null));
            _outControls++;
        }

        Wake();

        // handler 在后台运行；返回即关闭连接（与 Go 的 defer c.Close() 一致）。
        _ = RunHandlerAsync(handler, conn);
        return SessionOpenResult.Ok;
    }

    /// <summary>
    /// 出队并将 ack 提交到 poll 响应。异步入队（满则等待）。
    /// </summary>
    /// <param name="conn">发起连接的连接。</param>
    /// <param name="connId">目标连接 id。</param>
    /// <param name="kind">帧类型。</param>
    /// <param name="data">帧载荷。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>入队完成的任务。</returns>
    public async Task SendAsync(StreamConn? conn, uint connId, byte kind, byte[]? data, CancellationToken ct)
    {
        var control = kind != StreamFrameKind.Data;
        var length = data?.Length ?? 0;

        while (true)
        {
            TaskCompletionSource<bool>? waiter = null;
            bool hasRoom;
            lock (_lock)
            {
                if (_closed)
                {
                    throw new StreamClosedException();
                }

                if (!control && conn is not null && conn.Closed.IsCancellationRequested)
                {
                    throw new StreamClosedException();
                }

                hasRoom = control ? HasControlRoom(kind) : HasDataRoom(length);
                if (!hasRoom)
                {
                    waiter = _space.Prepare();
                }
            }

            if (!hasRoom)
            {
                try
                {
                    await waiter!.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    lock (_lock)
                    {
                        _space.Remove(waiter!);
                    }

                    throw new StreamClosedException();
                }

                lock (_lock)
                {
                    _space.Remove(waiter!);
                }

                continue;
            }

            await _manager.ReserveOutboundAsync(kind, length, control ? null : conn, ct).ConfigureAwait(false);

            var queued = false;
            lock (_lock)
            {
                if (_closed || (!control && conn is not null && conn.Closed.IsCancellationRequested))
                {
                    _manager.ReleaseOutbound(kind, length);
                    throw new StreamClosedException();
                }

                if (control ? HasControlRoom(kind) : HasDataRoom(length))
                {
                    _out.Add(new OutFrame(connId, kind, data));
                    _outBytes += length;
                    CountQueued(kind, 1);
                    queued = true;
                }
                else
                {
                    _manager.ReleaseOutbound(kind, length);
                }
            }

            if (queued)
            {
                Wake();
                return;
            }
        }
    }

    /// <summary>
    /// 非阻塞入队。满或关闭时返回 false。
    /// </summary>
    /// <param name="conn">发起连接的连接。</param>
    /// <param name="connId">目标连接 id。</param>
    /// <param name="kind">帧类型。</param>
    /// <param name="data">帧载荷。</param>
    /// <returns>入队成功返回 true。</returns>
    public bool TryEnqueue(StreamConn? conn, uint connId, byte kind, byte[]? data)
    {
        var control = kind != StreamFrameKind.Data;
        var length = data?.Length ?? 0;

        lock (_lock)
        {
            if (_closed)
            {
                return false;
            }

            if (!control && conn is not null && conn.Closed.IsCancellationRequested)
            {
                return false;
            }

            if (!(control ? HasControlRoom(kind) : HasDataRoom(length)))
            {
                return false;
            }
        }

        if (!_manager.TryReserveOutbound(kind, length))
        {
            return false;
        }

        lock (_lock)
        {
            if (_closed || (!control && conn is not null && conn.Closed.IsCancellationRequested))
            {
                _manager.ReleaseOutbound(kind, length);
                return false;
            }

            if (!(control ? HasControlRoom(kind) : HasDataRoom(length)))
            {
                _manager.ReleaseOutbound(kind, length);
                return false;
            }

            _out.Add(new OutFrame(connId, kind, data));
            _outBytes += length;
            CountQueued(kind, 1);
        }

        Wake();
        return true;
    }

    /// <summary>
    /// 唤醒停放在 native poll 上的请求（非阻塞）。对应 Go <c>streamSession.wake</c>。
    /// </summary>
    public void Wake()
    {
        try
        {
            _notify.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已有一个待处理唤醒：突发折叠为一次，poll 会自行在锁内重读状态。
        }
    }

    /// <summary>
    /// 释放所有等待队列空间的生产者。对应 Go <c>streamSession.wakeProducers</c>。
    /// </summary>
    public void WakeProducers()
    {
        lock (_lock)
        {
            _space.Pulse();
        }
    }

    /// <summary>
    /// 从连接表移除连接。对应 Go <c>streamSession.removeConn</c>。
    /// </summary>
    /// <param name="connId">连接 id。</param>
    public void RemoveConn(uint connId)
    {
        lock (_lock)
        {
            _conns.Remove(connId);
        }
    }

    /// <summary>
    /// 停放直到有帧可投递、hold 到期、本 poll 被超越或会话死亡。
    /// <paramref name="alive"/> 为 false 表示会话已消失，handler 据此让前端停止轮询。
    /// 对应 Go <c>streamSession.awaitFrames</c>。
    /// </summary>
    /// <param name="hold">空 poll 停放时长。</param>
    /// <param name="maxBytes">单响应字节上限。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>本次投递结果。</returns>
    public async Task<SessionPollResult> AwaitFramesAsync(TimeSpan hold, int maxBytes, CancellationToken ct)
    {
        ulong gen;
        lock (_lock)
        {
            _pollGen++;
            gen = _pollGen;
            _lastSeen = DateTimeOffset.UtcNow;
        }

        // 任何已停放的 poll 都属于已经走开的页面。
        Wake();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(hold);

        while (true)
        {
            lock (_lock)
            {
                if (_closed)
                {
                    return new SessionPollResult([], false, false);
                }

                if (gen != _pollGen)
                {
                    // 被更新的 poll 超越：返回空而非窃取更新的 poll 应承载的帧。
                    return new SessionPollResult([], false, true);
                }

                if (_out.Count > 0)
                {
                    var (frames, more) = DrainLocked(maxBytes);
                    return new SessionPollResult(frames, more, true);
                }
            }

            try
            {
                await _notify.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new SessionPollResult([], false, true);
            }
        }
    }

    /// <summary>
    /// 结束会话及其上所有连接。阻塞在 Receive 或 Send 中的 handler 会以
    /// <see cref="StreamClosedException"/> 解除阻塞。对应 Go <c>streamSession.close</c>。
    /// </summary>
    public void Close()
    {
        StreamConn[] conns;
        StreamChunkStore? chunks;
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            conns = new StreamConn[_conns.Count];
            _conns.Values.CopyTo(conns, 0);
            // 立即释放缓冲载荷及其应用级保留：不会再有任何 poll 来取它们。
            foreach (var frame in _out)
            {
                _manager.ReleaseOutbound(frame.Kind, frame.Payload?.Length ?? 0);
            }

            _out.Clear();
            _outBytes = 0;
            _outDataFrames = 0;
            _outControls = 0;
            _outCloses = 0;
            chunks = _chunkStore;
            _space.Pulse();
        }

        chunks?.Close();
        Wake();

        foreach (var conn in conns)
        {
            conn.ShutdownFromSession();
        }
    }

    /// <summary>
    /// 从队列取出尽可能多的帧（不超过 <paramref name="maxBytes"/>）。总是至少取一帧，
    /// 使单帧大于上限时仍能投递，而不是永久卡住队列。
    /// </summary>
    /// <param name="maxBytes">字节上限。</param>
    /// <returns>取出的帧与是否仍有剩余。</returns>
    private (List<OutFrame> Frames, bool More) DrainLocked(int maxBytes)
    {
        var total = 0;
        var n = 0;
        while (n < _out.Count)
        {
            var size = (_out[n].Payload?.Length ?? 0) + StreamConstants.FrameHeaderBytes;
            if (n > 0 && total + size > maxBytes)
            {
                break;
            }

            total += size;
            n++;
        }

        var frames = new List<OutFrame>(n);
        for (var i = 0; i < n; i++)
        {
            var frame = _out[i];
            _manager.ReleaseOutbound(frame.Kind, frame.Payload?.Length ?? 0);
            _outBytes -= frame.Payload?.Length ?? 0;
            CountQueued(frame.Kind, -1);
            frames.Add(frame);
        }

        _out.RemoveRange(0, n);
        var more = _out.Count > 0;

        // 刚移除的内容即为被阻塞生产者的空间。
        _space.Pulse();
        return (frames, more);
    }

    /// <summary>
    /// 判断数据帧是否仍有本地空间（帧数与字节双重上限）。调用方须持有 <see cref="_lock"/>。
    /// </summary>
    /// <param name="length">候选帧长度。</param>
    /// <returns>有空间返回 true。</returns>
    private bool HasDataRoom(int length)
    {
        return _outDataFrames < StreamConstants.OutQueueDepth &&
            (_outDataFrames == 0 || _outBytes + length <= StreamConstants.OutQueueBytes);
    }

    /// <summary>
    /// 判断控制帧是否仍有本地空间（close 与其他控制帧分别记账）。调用方须持有 <see cref="_lock"/>。
    /// </summary>
    /// <param name="kind">帧类型。</param>
    /// <returns>有空间返回 true。</returns>
    private bool HasControlRoom(byte kind)
    {
        return kind == StreamFrameKind.Close
            ? _outCloses < StreamConstants.MaxConnections
            : _outControls < StreamConstants.OutControlDepth;
    }

    /// <summary>
    /// 按帧类型调整本地队列计数。调用方须持有 <see cref="_lock"/>。
    /// </summary>
    /// <param name="kind">帧类型。</param>
    /// <param name="delta">增量。</param>
    private void CountQueued(byte kind, int delta)
    {
        switch (kind)
        {
            case StreamFrameKind.Data:
                _outDataFrames += delta;
                break;
            case StreamFrameKind.Close:
                _outCloses += delta;
                break;
            default:
                _outControls += delta;
                break;
        }
    }

    /// <summary>
    /// 运行 handler；返回即关闭连接。异常记录但不外泄。
    /// </summary>
    /// <param name="handler">已注册的 handler。</param>
    /// <param name="conn">连接。</param>
    /// <returns>完成的任务。</returns>
    private async Task RunHandlerAsync(StreamHandler handler, StreamConn conn)
    {
        try
        {
            await handler(conn).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _manager.LogHandlerError(conn.Name, ex);
        }
        finally
        {
            conn.Close();
        }
    }
}
