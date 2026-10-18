using System.Text.Json;

namespace Wails.Net.Application.Streams;

/// <summary>
/// 连接的出站帧去向。桌面模式下是窗口的持锁 poll 会话；Server 模式下是真实 WebSocket。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream.go</c> 中的 <c>streamSink</c> 接口。
/// <para>
/// 之上的 handler 代码在两种传输上完全一致，这正是「镜像 WebSocket 模型而非另造 API」的收益。
/// </para>
/// </summary>
internal interface IStreamSink
{
    /// <summary>
    /// 异步入队一个帧；满时等待到有空间（对应 Go 的 <c>block=true</c> 路径）。
    /// 连接/会话已关闭时抛出 <see cref="StreamClosedException"/>。
    /// </summary>
    /// <param name="conn">发起连接的连接（可为 null，表示拒绝路径上尚无连接）。</param>
    /// <param name="connId">目标连接 id。</param>
    /// <param name="kind">帧类型。</param>
    /// <param name="data">帧载荷。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>入队完成的任务。</returns>
    Task SendAsync(StreamConn? conn, uint connId, byte kind, byte[]? data, CancellationToken ct);

    /// <summary>
    /// 非阻塞入队一个帧；满或关闭时返回 false（对应 Go 的 <c>block=false</c> 路径）。
    /// </summary>
    /// <param name="conn">发起连接的连接（可为 null）。</param>
    /// <param name="connId">目标连接 id。</param>
    /// <param name="kind">帧类型。</param>
    /// <param name="data">帧载荷。</param>
    /// <returns>入队成功返回 true。</returns>
    bool TryEnqueue(StreamConn? conn, uint connId, byte kind, byte[]? data);

    /// <summary>从连接表中移除连接（Best-effort 关闭通知已入队后调用）。</summary>
    /// <param name="connId">连接 id。</param>
    void RemoveConn(uint connId);

    /// <summary>非阻塞地唤醒停放的 poll / 写泵。</summary>
    void Wake();

    /// <summary>释放所有等待队列空间的生产者，避免关闭时仍有 Send 永久停放。</summary>
    void WakeProducers();
}

/// <summary>
/// <see cref="StreamConn.DeliverAsync"/> 的结果。
/// </summary>
internal enum StreamDeliverResult
{
    /// <summary>已入队。</summary>
    Delivered,

    /// <summary>收件箱已满（调用方应回 429 或施加背压）。</summary>
    Full,

    /// <summary>连接已关闭。</summary>
    Closed,
}

/// <summary>
/// 通往某个命名流的一条连接，是 <c>*websocket.Conn</c> 的等价物：
/// <see cref="SendAsync"/> 像 socket 写一样阻塞，<see cref="ReceiveAsync"/> 像 socket 读一样阻塞，
/// 且对端消失后两者均失败。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream.go</c> 中的 <c>StreamConn</c>。
/// </summary>
public sealed class StreamConn
{
    /// <summary>共享的 JSON 序列化选项。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    /// <summary>出站帧去向。</summary>
    private readonly IStreamSink _sink;

    /// <summary>所属管理器（用于全局额度记账）；可为 null。</summary>
    private readonly StreamManager? _manager;

    /// <summary>连接 id，由前端分配。</summary>
    private readonly uint _id;

    /// <summary>所属窗口 id（Server 模式恒为 0）。</summary>
    private readonly uint _windowId;

    /// <summary>是否持有全局 lifecycle 槽（桌面连接为 true，Server 连接由请求级持有）。</summary>
    private readonly bool _lifecycle;

    /// <summary>连接存活期取消令牌源；关闭时取消。</summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary><see cref="Close"/> 的 once 标志。</summary>
    private int _closeOnce;

    /// <summary><see cref="Shutdown"/> 的 once 标志。</summary>
    private int _shutdownOnce;

    /// <summary>保护入站队列与记账的锁。</summary>
    private readonly object _inGate = new();

    /// <summary>入站帧队列。</summary>
    private readonly Queue<byte[]> _in = new();

    /// <summary>入站队列当前字节数。</summary>
    private int _inBytes;

    /// <summary>入站帧到达时唤醒等待 Receive 的消费者。</summary>
    private readonly PulseGate _inArrived = new();

    /// <summary>入站队列腾出空间时唤醒等待投递的生产者（Server 模式读泵背压）。</summary>
    private readonly PulseGate _inSpace = new();

    /// <summary>
    /// 构造一条连接。仅供内部（会话 / Server 传输）使用。
    /// </summary>
    /// <param name="id">连接 id。</param>
    /// <param name="name">流名。</param>
    /// <param name="windowId">窗口 id。</param>
    /// <param name="sink">出站帧去向。</param>
    /// <param name="manager">所属管理器。</param>
    /// <param name="lifecycle">是否持有全局 lifecycle 槽。</param>
    internal StreamConn(uint id, string name, uint windowId, IStreamSink sink, StreamManager? manager, bool lifecycle)
    {
        _id = id;
        Name = name;
        _windowId = windowId;
        _sink = sink;
        _manager = manager;
        _lifecycle = lifecycle;
    }

    /// <summary>连接 id。</summary>
    public uint Id => _id;

    /// <summary>本连接所打开的流名。</summary>
    public string Name { get; }

    /// <summary>打开本连接的窗口 id；Server 模式恒为 0。</summary>
    public uint WindowId => _windowId;

    /// <summary>
    /// 连接关闭时取消的取消令牌，供 handler 派生出的任务随之展开。
    /// 对应 Go <c>StreamConn.Context()</c>。
    /// </summary>
    public CancellationToken Closed => _cts.Token;

    /// <summary>
    /// 排队一个帧用于投递到前端。窗口出站缓冲满时阻塞（如 socket 写阻塞在满的发送缓冲），
    /// 连接消失后抛出 <see cref="StreamClosedException"/>。
    /// 对应 Go <c>StreamConn.Send</c>。
    /// </summary>
    /// <param name="data">帧载荷；长度不得超过 <see cref="StreamConstants.MaxFrameBytes"/>。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>入队完成的任务。</returns>
    /// <exception cref="StreamTooLargeException">帧超过 64 MiB 上限。</exception>
    /// <exception cref="StreamClosedException">连接已关闭。</exception>
    public Task SendAsync(byte[] data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > StreamConstants.MaxFrameBytes)
        {
            throw new StreamTooLargeException();
        }

        return _sink.SendAsync(this, _id, StreamFrameKind.Data, data, ct);
    }

    /// <summary>
    /// <see cref="SendAsync"/> 的非阻塞形式：无空间时返回 false，而非等待前端追赶。
    /// 对应 Go <c>StreamConn.TrySend</c>。
    /// </summary>
    /// <param name="data">帧载荷；长度不得超过 <see cref="StreamConstants.MaxFrameBytes"/>。</param>
    /// <returns>入队成功返回 true；满或已关闭返回 false。</returns>
    /// <exception cref="StreamTooLargeException">帧超过 64 MiB 上限。</exception>
    public bool TrySend(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > StreamConstants.MaxFrameBytes)
        {
            throw new StreamTooLargeException();
        }

        return _sink.TryEnqueue(this, _id, StreamFrameKind.Data, data);
    }

    /// <summary>
    /// 将 <paramref name="value"/> 序列化后作为单个帧发送（便捷方法）。线上只承载序列化字节，
    /// 前端可用 <c>JSON.parse</c> 读取。对应 Go <c>StreamConn.SendJSON</c>。
    /// </summary>
    /// <typeparam name="T">载荷类型。</typeparam>
    /// <param name="value">要序列化的值。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>入队完成的任务。</returns>
    public Task SendJsonAsync<T>(T value, CancellationToken ct = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        return SendAsync(bytes, ct);
    }

    /// <summary>
    /// 阻塞等待下一个帧。空收件箱时等待直到有帧到达或连接关闭。
    /// 对应 Go <c>StreamConn.Receive</c>。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>下一个帧的载荷字节。</returns>
    /// <exception cref="StreamClosedException">连接已关闭。</exception>
    public async Task<byte[]> ReceiveAsync(CancellationToken ct = default)
    {
        while (true)
        {
            byte[]? frame = null;
            TaskCompletionSource<bool>? waiter = null;
            lock (_inGate)
            {
                if (_in.Count > 0)
                {
                    frame = _in.Dequeue();
                    _inBytes -= frame.Length;
                    _inSpace.Pulse();
                }
                else if (_cts.IsCancellationRequested)
                {
                    throw new StreamClosedException();
                }
                else
                {
                    waiter = _inArrived.Prepare();
                }
            }

            if (frame is not null)
            {
                _manager?.ReleaseInbound(frame.Length, 1);
                return frame;
            }

            try
            {
                await waiter!.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (_inGate)
                {
                    _inArrived.Remove(waiter!);
                }

                throw;
            }

            lock (_inGate)
            {
                _inArrived.Remove(waiter!);
            }
        }
    }

    /// <summary>
    /// 阻塞等待下一个帧并反序列化到 <typeparamref name="T"/>。
    /// 畸形帧会抛出反序列化异常并消费该帧——它不会关闭连接，handler 可记录后继续。
    /// 对应 Go <c>StreamConn.ReceiveJSON</c>。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="ct">取消令牌。</param>
    /// <returns>反序列化得到的值。</returns>
    public async Task<T> ReceiveJsonAsync<T>(CancellationToken ct = default)
    {
        var frame = await ReceiveAsync(ct).ConfigureAwait(false);
        var value = JsonSerializer.Deserialize<T>(frame, JsonOptions);
        return value!;
    }

    /// <summary>
    /// 结束本连接并通知前端（前端会看到 onclose）。可从任意线程多次调用。
    /// 对应 Go <c>StreamConn.Close</c>。
    /// </summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _closeOnce, 1) != 0)
        {
            return;
        }

        // 先把连接标记为已关闭（取消 ctx）再排队关闭通知。否则并发 Send 可挤到 close 帧之后，
        // 报告成功，随后被已处理 close 的前端丢弃。
        _cts.Cancel();
        // Best-effort 通知：满或会话已消失时前端会经会话消失得知，这里不得阻塞。
        _ = _sink.TryEnqueue(this, _id, StreamFrameKind.Close, null);
        Shutdown();
    }

    /// <summary>
    /// 标记连接已由对端关闭，使后续 <see cref="Close"/> 不再向已离开的对端排队 close 帧。
    /// 消费与 <see cref="Close"/> 相同的 once。
    /// </summary>
    internal void ClosedByPeer()
    {
        Interlocked.Exchange(ref _closeOnce, 1);
        Shutdown();
    }

    /// <summary>
    /// 会话关闭时拆除连接本地端（不向已消失的前端排队 close 帧）。对应 Go 中
    /// <c>streamSession.close</c> 对每个连接调用的 <c>c.shutdown()</c>。
    /// </summary>
    internal void ShutdownFromSession()
    {
        Shutdown();
    }

    /// <summary>
    /// 在拒绝路径上取消连接（不排队 close 帧、不做 shutdown），用于 open 未被接受时。
    /// </summary>
    internal void Cancel()
    {
        _cts.Cancel();
    }

    /// <summary>
    /// 投递一个入站帧并唤醒等待的 Receive。
    /// <paramref name="block"/> 为 false 时收件箱无空间即返回 <see cref="StreamDeliverResult.Full"/>；
    /// 为 true 时（Server 模式读泵）等待到有空间，让 TCP 背压传导到对端。
    /// 空收件箱总是接受一个帧，无论其大小。对应 Go <c>deliver</c> / <c>deliverBlocking</c>。
    /// </summary>
    /// <param name="data">入站帧载荷。</param>
    /// <param name="block">是否施加背压等待。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>投递结果。</returns>
    internal async Task<StreamDeliverResult> DeliverAsync(byte[] data, bool block, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(data);

        while (true)
        {
            bool room;
            TaskCompletionSource<bool>? waiter = null;
            lock (_inGate)
            {
                if (_cts.IsCancellationRequested)
                {
                    return StreamDeliverResult.Closed;
                }

                room = HasRoom(data.Length);
                if (!room)
                {
                    if (!block)
                    {
                        return StreamDeliverResult.Full;
                    }

                    waiter = _inSpace.Prepare();
                }
            }

            if (!room)
            {
                try
                {
                    await waiter!.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    lock (_inGate)
                    {
                        _inSpace.Remove(waiter!);
                    }

                    return StreamDeliverResult.Closed;
                }

                lock (_inGate)
                {
                    _inSpace.Remove(waiter!);
                }

                continue;
            }

            if (_manager is not null)
            {
                var reserved = await _manager.ReserveInboundAsync(data.Length, block, ct).ConfigureAwait(false);
                if (reserved != StreamReserveResult.Ok)
                {
                    return reserved == StreamReserveResult.Closed
                        ? StreamDeliverResult.Closed
                        : StreamDeliverResult.Full;
                }
            }

            lock (_inGate)
            {
                if (!_cts.IsCancellationRequested && HasRoom(data.Length))
                {
                    _in.Enqueue(data);
                    _inBytes += data.Length;
                    _inArrived.Pulse();
                    return StreamDeliverResult.Delivered;
                }
            }

            _manager?.ReleaseInbound(data.Length, 1);
        }
    }

    /// <summary>
    /// 判断入站收件箱是否仍有空间：帧数未达上限，且（空队列或字节数够放）。
    /// </summary>
    /// <param name="length">候选帧长度。</param>
    /// <returns>有空间返回 true。</returns>
    private bool HasRoom(int length)
    {
        return _in.Count < StreamConstants.InQueueDepth &&
            (_in.Count == 0 || _inBytes + length <= StreamConstants.InQueueBytes);
    }

    /// <summary>
    /// 不通知前端地拆除本地端（对端先行消失时使用），并释放入站缓冲与全局额度。
    /// 对应 Go <c>StreamConn.shutdown</c>。
    /// </summary>
    private void Shutdown()
    {
        if (Interlocked.Exchange(ref _shutdownOnce, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        _sink.RemoveConn(_id);

        int releasedBytes;
        int releasedFrames;
        lock (_inGate)
        {
            releasedBytes = _inBytes;
            releasedFrames = _in.Count;
            _in.Clear();
            _inBytes = 0;
            _inArrived.Pulse();
            _inSpace.Pulse();
        }

        if (_manager is not null)
        {
            _manager.ReleaseInbound(releasedBytes, releasedFrames);
            // lifecycle 槽恰在连接存在期间归属本连接，shutdown 恰好一次。
            if (_lifecycle)
            {
                _manager.ReleaseLifecycle();
            }
        }

        _sink.WakeProducers();
        _sink.Wake();
    }
}
