using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Webview;

namespace Wails.Net.Application.Streams;

/// <summary>
/// 全局额度预留结果。
/// </summary>
internal enum StreamReserveResult
{
    /// <summary>预留成功。</summary>
    Ok,

    /// <summary>额度已满（非阻塞路径）。</summary>
    Full,

    /// <summary>管理器已关闭或连接已关闭。</summary>
    Closed,
}

/// <summary>
/// <see cref="StreamManager.SessionWithAdmission"/> 的结果。
/// </summary>
/// <param name="Session">解析/创建的会话；为 null 表示被拒绝。</param>
/// <param name="AtCapacity">被拒绝是否因容量上限（可重试），否则为终止性拒绝。</param>
internal readonly record struct SessionAdmission(StreamSession? Session, bool AtCapacity);

/// <summary>
/// 会话解析结果：成功时 <see cref="Session"/> 非空；否则 <see cref="Error"/> 为已构造的错误响应。
/// </summary>
/// <param name="Session">解析到的会话。</param>
/// <param name="Error">错误响应（已写好的状态码与 header）。</param>
internal readonly record struct SessionResolution(StreamSession? Session, WebviewResponse? Error);

/// <summary>
/// Streams 双向字节流管理器。给出 Wails 应用的「WebSocket 编程模型」而不绑定 TCP 端口：
/// Go→JS 走持锁 poll，JS→Go 走普通 POST。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream.go</c>（<c>streamManager</c>）、
/// <c>v3/pkg/application/stream_transport.go</c>（桌面端点）与
/// <c>v3/pkg/application/stream_server.go</c>（Server 模式 WebSocket）。
/// <para>
/// <b>线程安全</b>：实例可在任意线程并发调用。
/// </para>
/// </summary>
public sealed class StreamManager : IDisposable
{
    /// <summary>保护 handlers / sessions / retiredThrough / 分片全局记账的锁。</summary>
    private readonly object _mu = new();

    /// <summary>已注册的命名流 handler。</summary>
    private readonly Dictionary<string, StreamHandler> _handlers = new(StringComparer.Ordinal);

    /// <summary>session id → 会话。</summary>
    private readonly Dictionary<string, StreamSession> _sessions = new(StringComparer.Ordinal);

    /// <summary>每个窗口已完成的最高页面代次水位。</summary>
    private readonly Dictionary<uint, ulong> _retiredThrough = new();

    /// <summary>全局分片载荷字节账。</summary>
    private int _chunkBytes;

    /// <summary>全局分片分片数账。</summary>
    private int _chunkParts;

    /// <summary>是否已关闭。</summary>
    private volatile bool _closed;

    /// <summary>保护全局计数器与额度唤醒门的锁。</summary>
    private readonly object _budgetGate = new();

    /// <summary>全局出站字节账。</summary>
    private long _outBytes;

    /// <summary>全局出站帧数账。</summary>
    private long _outFrames;

    /// <summary>全局控制帧账。</summary>
    private long _outControls;

    /// <summary>全局 close 帧账。</summary>
    private long _outCloses;

    /// <summary>全局 lifecycle 槽账。</summary>
    private long _lifecycles;

    /// <summary>全局入站字节账。</summary>
    private long _inBytes;

    /// <summary>全局入站帧数账。</summary>
    private long _inFrames;

    /// <summary>全局额度腾出时唤醒等待的生产者。</summary>
    private readonly PulseGate _budgetSignal = new();

    /// <summary>可选日志记录器。</summary>
    private readonly ILogger<StreamManager>? _logger;

    /// <summary>空闲 session 清扫定时器（首次创建 session 时懒启动）。</summary>
    private Timer? _janitor;

    /// <summary>清扫定时器的 once 标志。</summary>
    private int _janitorStarted;

    /// <summary>
    /// 构造 Streams 管理器。
    /// </summary>
    /// <param name="logger">可选日志记录器；为 null 时不记录日志。</param>
    public StreamManager(ILogger<StreamManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>当前 session 数（用于观测与测试）。</summary>
    internal int SessionCount
    {
        get
        {
            lock (_mu)
            {
                return _sessions.Count;
            }
        }
    }

    /// <summary>当前全局出站字节账（用于观测与测试）。</summary>
    internal long OutboundBytesUsed
    {
        get
        {
            lock (_budgetGate)
            {
                return _outBytes;
            }
        }
    }

    /// <summary>当前全局入站字节账（用于观测与测试）。</summary>
    internal long InboundBytesUsed
    {
        get
        {
            lock (_budgetGate)
            {
                return _inBytes;
            }
        }
    }

    /// <summary>当前全局 lifecycle 槽占用数（用于观测与测试）。</summary>
    internal long LifecyclesUsed
    {
        get
        {
            lock (_budgetGate)
            {
                return _lifecycles;
            }
        }
    }

    // -----------------------------------------------------------------------
    // 注册
    // -----------------------------------------------------------------------

    /// <summary>
    /// 为命名流注册 handler。前端按该名称连接，handler 每个连接运行一次。
    /// 重复注册同一名称会替换 handler；已打开的连接保留其启动时的 handler。
    /// 对应 Go <c>App.HandleStream</c>。
    /// </summary>
    /// <param name="name">流名（非空，长度 ≤ 256）。</param>
    /// <param name="handler">连接处理委托。</param>
    public void HandleStream(string name, StreamHandler handler)
    {
        if (string.IsNullOrEmpty(name) || name.Length > StreamConstants.MaxNameLen || handler is null)
        {
            return;
        }

        lock (_mu)
        {
            _handlers[name] = handler;
        }
    }

    /// <summary>
    /// 查询已注册的 handler。
    /// </summary>
    /// <param name="name">流名。</param>
    /// <returns>handler；未注册时返回 null。</returns>
    internal StreamHandler? GetHandler(string name)
    {
        lock (_mu)
        {
            return _handlers.TryGetValue(name, out var handler) ? handler : null;
        }
    }

    /// <summary>
    /// 记录 handler 抛出的异常（吞掉以免影响连接生命周期）。
    /// </summary>
    /// <param name="name">流名。</param>
    /// <param name="exception">异常。</param>
    internal void LogHandlerError(string name, Exception exception)
    {
        _logger?.LogWarning(exception, "流 \"{Name}\" 的 handler 抛出异常", name);
    }

    // -----------------------------------------------------------------------
    // 分片全局记账
    // -----------------------------------------------------------------------

    /// <summary>
    /// 预留全局分片额度。对应 Go <c>streamManager.reserveChunkResources</c>。
    /// </summary>
    /// <param name="bytes">载荷字节数。</param>
    /// <param name="parts">分片数。</param>
    /// <returns>成功返回 true。</returns>
    internal bool ReserveChunkResources(int bytes, int parts)
    {
        if (bytes < 0 || parts < 0)
        {
            return false;
        }

        lock (_mu)
        {
            if (_closed ||
                bytes > StreamConstants.MaxChunkBytesGlobal - _chunkBytes ||
                parts > StreamConstants.MaxChunkPartsGlobal - _chunkParts)
            {
                return false;
            }

            _chunkBytes += bytes;
            _chunkParts += parts;
            return true;
        }
    }

    /// <summary>
    /// 归还全局分片额度。对应 Go <c>streamManager.releaseChunkResources</c>。
    /// </summary>
    /// <param name="bytes">载荷字节数。</param>
    /// <param name="parts">分片数。</param>
    internal void ReleaseChunkResources(int bytes, int parts)
    {
        if (bytes <= 0 && parts <= 0)
        {
            return;
        }

        lock (_mu)
        {
            _chunkBytes = bytes >= _chunkBytes ? 0 : _chunkBytes - bytes;
            _chunkParts = parts >= _chunkParts ? 0 : _chunkParts - parts;
        }
    }

    // -----------------------------------------------------------------------
    // 全局额度
    // -----------------------------------------------------------------------

    /// <summary>
    /// 非阻塞预留一个非数据 / 数据帧的全局额度。对应 Go <c>streamManager.reserveOutbound(false)</c>。
    /// </summary>
    /// <param name="kind">帧类型。</param>
    /// <param name="bytes">载荷字节数。</param>
    /// <returns>成功返回 true；满或已关闭返回 false。</returns>
    internal bool TryReserveOutbound(byte kind, int bytes)
    {
        lock (_budgetGate)
        {
            if (_closed)
            {
                return false;
            }

            if (!TryReserveOutboundLocked(kind, bytes))
            {
                return false;
            }

            if (_closed)
            {
                ReleaseOutboundLocked(kind, bytes);
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 阻塞预留帧的全局额度（满则等待）。对应 Go <c>streamManager.reserveOutbound(true)</c>。
    /// </summary>
    /// <param name="kind">帧类型。</param>
    /// <param name="bytes">载荷字节数。</param>
    /// <param name="conn">发起连接的连接（用于感知其关闭）；可为 null。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>预留完成的任务。</returns>
    /// <exception cref="StreamClosedException">管理器或连接已关闭。</exception>
    internal async Task ReserveOutboundAsync(byte kind, int bytes, StreamConn? conn, CancellationToken ct)
    {
        while (true)
        {
            TaskCompletionSource<bool>? waiter = null;
            lock (_budgetGate)
            {
                if (_closed || (conn is not null && conn.Closed.IsCancellationRequested))
                {
                    throw new StreamClosedException();
                }

                if (TryReserveOutboundLocked(kind, bytes))
                {
                    if (_closed || (conn is not null && conn.Closed.IsCancellationRequested))
                    {
                        ReleaseOutboundLocked(kind, bytes);
                        throw new StreamClosedException();
                    }

                    return;
                }

                waiter = _budgetSignal.Prepare();
            }

            try
            {
                await waiter!.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (_budgetGate)
                {
                    _budgetSignal.Remove(waiter!);
                }

                throw new StreamClosedException();
            }

            lock (_budgetGate)
            {
                _budgetSignal.Remove(waiter!);
            }
        }
    }

    /// <summary>
    /// 归还 <see cref="TryReserveOutbound"/> / <see cref="ReserveOutboundAsync"/> 预留的额度。
    /// 对应 Go <c>streamManager.releaseOutbound</c>。
    /// </summary>
    /// <param name="kind">帧类型。</param>
    /// <param name="bytes">载荷字节数。</param>
    internal void ReleaseOutbound(byte kind, int bytes)
    {
        lock (_budgetGate)
        {
            ReleaseOutboundLocked(kind, bytes);
            _budgetSignal.Pulse();
        }
    }

    /// <summary>
    /// 预留「打开一个连接」所需的全局 lifecycle 槽与控制槽。对应 Go <c>streamManager.reserveOpen</c>。
    /// </summary>
    /// <returns>成功返回 true。</returns>
    internal bool ReserveOpen()
    {
        lock (_budgetGate)
        {
            if (_closed || !ReserveCounter(ref _lifecycles, 1, StreamConstants.MaxConnectionsGlobal))
            {
                return false;
            }

            if (_closed || !ReserveCounter(ref _outControls, 1, StreamConstants.OutControlDepthGlobal))
            {
                ReleaseCounter(ref _lifecycles, 1);
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 回退 <see cref="ReserveOpen"/> 的预留。对应 Go <c>streamManager.releaseOpenReservation</c>。
    /// </summary>
    internal void ReleaseOpenReservation()
    {
        lock (_budgetGate)
        {
            ReleaseCounter(ref _lifecycles, 1);
            ReleaseCounter(ref _outControls, 1);
            _budgetSignal.Pulse();
        }
    }

    /// <summary>
    /// 预留一个全局 lifecycle 槽（Server 模式请求级）。对应 Go <c>streamManager.reserveLifecycle</c>。
    /// </summary>
    /// <returns>成功返回 true。</returns>
    internal bool ReserveLifecycle()
    {
        lock (_budgetGate)
        {
            return !_closed && ReserveCounter(ref _lifecycles, 1, StreamConstants.MaxConnectionsGlobal);
        }
    }

    /// <summary>
    /// 归还一个全局 lifecycle 槽。对应 Go <c>streamManager.releaseLifecycle</c>。
    /// </summary>
    internal void ReleaseLifecycle()
    {
        lock (_budgetGate)
        {
            ReleaseCounter(ref _lifecycles, 1);
            _budgetSignal.Pulse();
        }
    }

    /// <summary>
    /// 阻塞 / 非阻塞预留入站帧的全局额度。对应 Go <c>streamManager.reserveInbound</c>。
    /// </summary>
    /// <param name="bytes">载荷字节数。</param>
    /// <param name="block">满时是否等待。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>预留结果。</returns>
    internal async Task<StreamReserveResult> ReserveInboundAsync(int bytes, bool block, CancellationToken ct)
    {
        while (true)
        {
            TaskCompletionSource<bool>? waiter = null;
            lock (_budgetGate)
            {
                if (_closed || ct.IsCancellationRequested)
                {
                    return StreamReserveResult.Closed;
                }

                if (TryReserveInboundLocked(bytes))
                {
                    if (_closed || ct.IsCancellationRequested)
                    {
                        ReleaseInboundLocked(bytes, 1);
                        return StreamReserveResult.Closed;
                    }

                    return StreamReserveResult.Ok;
                }

                if (!block)
                {
                    return StreamReserveResult.Full;
                }

                waiter = _budgetSignal.Prepare();
            }

            try
            {
                await waiter!.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (_budgetGate)
                {
                    _budgetSignal.Remove(waiter!);
                }

                return StreamReserveResult.Closed;
            }

            lock (_budgetGate)
            {
                _budgetSignal.Remove(waiter!);
            }
        }
    }

    /// <summary>
    /// 归还入站帧的全局额度。对应 Go <c>streamManager.releaseInbound</c>。
    /// </summary>
    /// <param name="bytes">载荷字节数。</param>
    /// <param name="frames">帧数。</param>
    internal void ReleaseInbound(int bytes, int frames)
    {
        if (bytes <= 0 && frames <= 0)
        {
            return;
        }

        lock (_budgetGate)
        {
            ReleaseInboundLocked(bytes, frames);
            _budgetSignal.Pulse();
        }
    }

    // -----------------------------------------------------------------------
    // 会话
    // -----------------------------------------------------------------------

    /// <summary>
    /// 解析（或在 <paramref name="create"/> 时创建）请求命名的会话，并强制其归属于请求窗口。
    /// <paramref name="maySupersede"/> 仅 poll 为 true：页面引导必定 poll，因此 poll 是「本窗口已换新页」的诚实信号。
    /// 对应 Go <c>App.sessionFor</c>。
    /// </summary>
    /// <param name="windowId">请求窗口 id（0 表示平台未标记）。</param>
    /// <param name="sessionId">session id header 值。</param>
    /// <param name="generation">generation header 值。</param>
    /// <param name="create">是否允许创建会话。</param>
    /// <param name="maySupersede">是否允许退休同窗口更低代次的会话。</param>
    /// <returns>会话解析结果。</returns>
    internal SessionResolution ResolveSession(uint windowId, string? sessionId, string? generation, bool create, bool maySupersede)
    {
        // session id 由客户端生成、每页加载一个，与 runtime 的 clientId 类似。它不是能力凭证，
        // 下方的绑定校验才是阻止一个窗口触达另一窗口流的手段。
        if (string.IsNullOrEmpty(sessionId) || sessionId.Length > StreamConstants.MaxSessionIdLen)
        {
            return new SessionResolution(null, WebviewResponse.Text(400, "Bad Request", "missing session"));
        }

        if (generation is null)
        {
            // 早于页面代次的 runtime 无法安全参与 reload 超越。Gone 是刻意的：旧 poll 客户端理解它并关闭
            // socket，而通用 400 会让它们永远重试。
            return new SessionResolution(null, WebviewResponse.Text(410, "Gone", "stream runtime is out of date"));
        }

        if (!StreamFrameCodec.TryParseUInt64(generation, out var gen) || gen == 0)
        {
            return new SessionResolution(null, WebviewResponse.Text(400, "Bad Request", "bad session generation"));
        }

        StreamSession? session;
        var atCapacity = false;
        if (create)
        {
            var admission = SessionWithAdmission(sessionId, windowId, gen, maySupersede);
            session = admission.Session;
            atCapacity = admission.AtCapacity;
        }
        else
        {
            session = ExistingSession(sessionId);
        }

        if (session is null)
        {
            if (atCapacity)
            {
                // 当前页面的 poll 可超越更早代次并释放该额度。让抢在 poll 之前的 open 重试，
                // 而非永久关闭新 socket。
                return new SessionResolution(null, WebviewResponse.Text(
                    429, "Too Many Requests", "stream session capacity reached",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Retry-After"] = "0" }));
            }

            // Gone 而非 NotFound：告知客户端停止轮询并暴露 close，而非永远重试。
            return new SessionResolution(null, WebviewResponse.Text(410, "Gone", "session closed"));
        }

        if (windowId != 0 && session.WindowId != windowId)
        {
            return new SessionResolution(null, WebviewResponse.Text(403, "Forbidden", "session belongs to another window"));
        }

        if (session.Generation != gen)
        {
            return new SessionResolution(null, WebviewResponse.Text(403, "Forbidden", "session generation mismatch"));
        }

        return new SessionResolution(session, null);
    }

    /// <summary>
    /// 返回 id 对应的会话，必要时创建。会话由 poll 或 send 中先到者创建；前端不进行单独握手。
    /// 对应 Go <c>streamManager.sessionWithAdmission</c>。
    /// </summary>
    /// <param name="id">session id。</param>
    /// <param name="windowId">窗口 id。</param>
    /// <param name="generation">页面代次。</param>
    /// <param name="maySupersede">是否允许退休更低代次会话。</param>
    /// <returns>会话准入结果。</returns>
    internal SessionAdmission SessionWithAdmission(string id, uint windowId, ulong generation, bool maySupersede)
    {
        List<StreamSession>? superseded = null;
        StreamSession? result;

        lock (_mu)
        {
            if (_closed)
            {
                return new SessionAdmission(null, false);
            }

            var found = _sessions.TryGetValue(id, out var existing);

            // 仅在拥有该会话的窗口内解析已存在的 id。外来 poll 曾借用本会话较新的代次并退休请求窗口的合法会话。
            if (found && windowId != 0 && existing!.WindowId != windowId)
            {
                return new SessionAdmission(existing, false);
            }

            // 代次不匹配同理：无效 poll 不得在对撞 id 上使用已存代次去退休其它合法会话。
            if (found && existing!.Generation != generation)
            {
                return new SessionAdmission(existing, false);
            }

            // 0 表示平台无法识别窗口。这类请求可能来自相互独立的浏览器客户端，其代次计数无顺序关系。
            // 保持全局有界，但绝不让其一退休另一或推进共享代次水位。
            var scopedSupersede = maySupersede && windowId != 0;

            if (!found)
            {
                // 页面代次完成后，来自该页的任何延迟请求都不得重建它。
                if (windowId != 0 && _retiredThrough.TryGetValue(windowId, out var watermark) && generation <= watermark)
                {
                    return new SessionAdmission(null, false);
                }

                // 当前页面的 poll 即便前驱撑满额度，也可在本窗口内替换更早会话。
                var replacesOlder = false;
                var windowSessions = 0;
                foreach (var session in _sessions.Values)
                {
                    if (session.WindowId == windowId)
                    {
                        windowSessions++;
                        if (scopedSupersede && session.Generation < generation)
                        {
                            replacesOlder = true;
                        }
                    }
                }

                if ((windowId != 0 && windowSessions >= StreamConstants.MaxSessionsPerWindow && !replacesOlder) ||
                    (_sessions.Count >= StreamConstants.MaxSessions && !replacesOlder))
                {
                    return new SessionAdmission(null, true);
                }

                result = new StreamSession(id, windowId, generation, this);
                _sessions[id] = result;
                StartJanitor();
            }
            else
            {
                result = existing;
            }

            // open POST 与首个 poll 并发启动。先创建新会话者赋予其代次；其 poll 到达时只退休更早会话。
            // 旧页面的延迟 poll 看到更新代次，便不动它。
            if (scopedSupersede)
            {
                // poll 证明这是本窗口当前页面代次。退休整个更早代次区间，而不仅是已存在的会话：
                // 旧页面可能在导航前已派发首个请求，却在本次 poll 完成后才到达。
                if (result!.Generation > 1 && result.Generation - 1 > _retiredThrough.GetValueOrDefault(windowId))
                {
                    _retiredThrough[windowId] = result.Generation - 1;
                }

                // 已有会话的窗口出现新 session id 意味着该窗口导航/重载：上一页面已消失。否则旧会话会存活到
                // TTL 清扫（60-80s），期间应用持有两条通往同一流的活连接。
                List<string>? doomedKeys = null;
                foreach (var pair in _sessions)
                {
                    if (pair.Key != id && pair.Value.WindowId == windowId && pair.Value.Generation < result.Generation)
                    {
                        (doomedKeys ??= new List<string>()).Add(pair.Key);
                        (superseded ??= new List<StreamSession>()).Add(pair.Value);
                    }
                }

                if (doomedKeys is not null)
                {
                    foreach (var key in doomedKeys)
                    {
                        _sessions.Remove(key);
                    }
                }
            }
        }

        if (superseded is not null)
        {
            foreach (var old in superseded)
            {
                RetireLocked(old);
                // 锁外：close() 会取各会话自身的锁并关闭其连接。
                old.Close();
            }
        }

        var admitted = result!;
        admitted.Touch();
        return new SessionAdmission(admitted, false);
    }

    /// <summary>
    /// 不创建地查询会话，供不得凭空造出状态的请求使用。对应 Go <c>streamManager.existingSession</c>。
    /// </summary>
    /// <param name="id">session id。</param>
    /// <returns>会话；不存在时返回 null。</returns>
    internal StreamSession? ExistingSession(string id)
    {
        lock (_mu)
        {
            return _sessions.TryGetValue(id, out var session) ? session : null;
        }
    }

    /// <summary>
    /// 遗忘已关闭的传输状态。不得退休页面代次：观察到 close 的文档可能仍处于加载中，稍后可再创建一个流。
    /// 对应 Go <c>streamManager.removeSession</c>。
    /// </summary>
    /// <param name="id">session id。</param>
    internal void RemoveSession(string id)
    {
        lock (_mu)
        {
            _sessions.Remove(id);
        }
    }

    /// <summary>
    /// 关闭某个正在消失的窗口的所有会话。镜像 <c>eventPayloadStore.dropWindow</c>：窗口没了，
    /// 不会再有 poll，等 TTL 只会白占内存。对应 Go <c>streamManager.dropWindow</c>。
    /// </summary>
    /// <param name="windowId">窗口 id。</param>
    public void DropWindow(uint windowId)
    {
        List<StreamSession>? doomed = null;
        lock (_mu)
        {
            List<string>? remove = null;
            foreach (var pair in _sessions)
            {
                if (pair.Value.WindowId == windowId)
                {
                    (remove ??= new List<string>()).Add(pair.Key);
                    (doomed ??= new List<StreamSession>()).Add(pair.Value);
                    // 与 Go 一致：退休水位在锁内推进。
                    RetireLocked(pair.Value);
                }
            }

            if (remove is not null)
            {
                foreach (var key in remove)
                {
                    _sessions.Remove(key);
                }
            }
        }

        if (doomed is null)
        {
            return;
        }

        foreach (var session in doomed)
        {
            session.Close();
        }
    }

    /// <summary>
    /// 提升窗口的代次水位。对应 Go <c>streamManager.retireLocked</c>。
    /// </summary>
    /// <param name="session">被退休的会话。</param>
    private void RetireLocked(StreamSession? session)
    {
        if (session is null)
        {
            return;
        }

        if (session.Generation > _retiredThrough.GetValueOrDefault(session.WindowId))
        {
            _retiredThrough[session.WindowId] = session.Generation;
        }
    }

    /// <summary>
    /// 一次清扫：关闭已停止轮询的会话的窗口。对应 Go <c>streamManager.reapStale</c>。
    /// </summary>
    /// <param name="now">当前时刻。</param>
    internal void ReapStale(DateTimeOffset now)
    {
        List<StreamSession>? doomed = null;
        lock (_mu)
        {
            if (_closed)
            {
                return;
            }

            List<string>? remove = null;
            foreach (var pair in _sessions)
            {
                // 仍有活连接的会话由运行中的 handler 拥有；饱和下页面可能忙于派发大响应超过 TTL 才发出下一 poll。
                // 因此仅在无连接时信任空闲时长；真正离去的页面由窗口销毁或下一页 poll 的超越捕获，
                // 下方的长宽限是渲染进程无二者而崩溃时的兜底。
                var ttl = pair.Value.ConnCount() > 0 ? StreamConstants.SessionGrace : StreamConstants.SessionTtl;
                if (now - pair.Value.LastSeenAt() > ttl)
                {
                    (remove ??= new List<string>()).Add(pair.Key);
                    (doomed ??= new List<StreamSession>()).Add(pair.Value);
                }
            }

            if (remove is not null)
            {
                foreach (var key in remove)
                {
                    _sessions.Remove(key);
                }
            }
        }

        if (doomed is null)
        {
            return;
        }

        foreach (var session in doomed)
        {
            session.Close();
        }
    }

    /// <summary>
    /// 懒启动清扫定时器。
    /// </summary>
    private void StartJanitor()
    {
        if (Interlocked.Exchange(ref _janitorStarted, 1) != 0)
        {
            return;
        }

        _janitor = new Timer(_ => ReapStale(DateTimeOffset.UtcNow), null, StreamConstants.SessionSweep, StreamConstants.SessionSweep);
    }

    // -----------------------------------------------------------------------
    // 端点分发
    // -----------------------------------------------------------------------

    /// <summary>
    /// 处理一个 Streams 端点请求（<c>/wails/stream/poll</c> 或 <c>/wails/stream/send</c>）。
    /// 对应 Go <c>App.serveStream</c>。
    /// </summary>
    /// <param name="request">平台无关的 WebView 请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>平台无关的响应。</returns>
    public Task<WebviewResponse> HandleAsync(WebviewRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.Equals(request.Path, StreamConstants.PathPoll, StringComparison.Ordinal))
        {
            return StreamEndpoints.ServePollAsync(this, request, ct);
        }

        if (string.Equals(request.Path, StreamConstants.PathSend, StringComparison.Ordinal))
        {
            return StreamEndpoints.ServeSendAsync(this, request, ct);
        }

        return Task.FromResult(WebviewResponse.NotFound());
    }

    // -----------------------------------------------------------------------
    // Server 模式 WebSocket
    // -----------------------------------------------------------------------

    /// <summary>
    /// 处理一个 Server 模式的 Streams WebSocket 会话：<c>windowId=0</c>，流名已在调用方从查询解析。
    /// 与 Go 一致：握手即注册连接（<c>frameOpen</c> 写入为 no-op），读泵以 TCP 背压投递入站帧，
    /// 出站走有界队列 + 单写泵。对应 Wails v3 Go 版本
    /// <c>v3/pkg/application/stream_server.go</c> 中的 <c>serveStreamWS</c>。
    /// </summary>
    /// <param name="socket">已升级的 WebSocket。</param>
    /// <param name="name">流名。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>会话结束的任务。</returns>
    public async Task HandleWebSocketAsync(WebSocket socket, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(socket);

        if (string.IsNullOrEmpty(name) || name.Length > StreamConstants.MaxNameLen)
        {
            await CloseSocketAsync(socket, WebSocketCloseStatus.PolicyViolation, "invalid stream name", ct).ConfigureAwait(false);
            return;
        }

        var handler = GetHandler(name);
        if (handler is null)
        {
            await CloseSocketAsync(socket, WebSocketCloseStatus.PolicyViolation, "no handler registered for stream " + name, ct).ConfigureAwait(false);
            return;
        }

        if (!ReserveLifecycle())
        {
            await CloseSocketAsync(socket, WebSocketCloseStatus.PolicyViolation, "stream connection capacity reached", ct).ConfigureAwait(false);
            return;
        }

        var sink = new WsStreamSink(socket, this, ct);
        var conn = new StreamConn(0, name, 0, sink, this, lifecycle: false);

        try
        {
            _ = ReadPumpAsync(socket, conn, sink.IoToken);

            try
            {
                await handler(conn).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogHandlerError(name, ex);
            }
            finally
            {
                // 与桌面一致：handler 返回即关闭流。
                conn.Close();
            }

            // Close 在其后排队了一个控制帧。在写泵排空该队列前不得拆掉 socket；仍需为停止读取的对端保留逃生通道。
            var drained = await Task.WhenAny(sink.Completion, Task.Delay(TimeSpan.FromSeconds(5), ct)).ConfigureAwait(false);
            if (drained != sink.Completion)
            {
                sink.Abort();
            }

            await sink.Completion.ConfigureAwait(false);
        }
        finally
        {
            sink.Abort();
            ReleaseLifecycle();
        }
    }

    /// <summary>
    /// Server 模式读泵：把 socket 上的二进制消息投递到连接收件箱（满时施加背压）。
    /// </summary>
    /// <param name="socket">WebSocket。</param>
    /// <param name="conn">连接。</param>
    /// <param name="ct">I/O 取消令牌。</param>
    /// <returns>完成的任务。</returns>
    private async Task ReadPumpAsync(WebSocket socket, StreamConn conn, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                using var ms = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                    if (ms.Length > StreamConstants.MaxFrameBytes)
                    {
                        return;
                    }
                }
                while (!result.EndOfMessage);

                if (await conn.DeliverAsync(ms.ToArray(), block: true, ct).ConfigureAwait(false) != StreamDeliverResult.Delivered)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            conn.ClosedByPeer();
        }
    }

    /// <summary>
    /// 以指定关闭状态关闭 socket（吞掉关闭过程中的异常）。
    /// </summary>
    /// <param name="socket">WebSocket。</param>
    /// <param name="status">关闭状态。</param>
    /// <param name="reason">关闭原因。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完成的任务。</returns>
    private static async Task CloseSocketAsync(WebSocket socket, WebSocketCloseStatus status, string reason, CancellationToken ct)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(status, reason, ct).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // 关闭失败：忽略，交由上层终止。
        }
    }

    /// <summary>
    /// 关闭管理器：拒绝后续预留、清空所有会话、停止清扫器。可安全重复调用。
    /// 对应 Go <c>streamManager.close</c>。
    /// </summary>
    public void Dispose()
    {
        List<StreamSession>? doomed;
        lock (_mu)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            doomed = new List<StreamSession>(_sessions.Values);
            _sessions.Clear();
            _retiredThrough.Clear();
        }

        lock (_budgetGate)
        {
            _budgetSignal.Pulse();
        }

        foreach (var session in doomed)
        {
            session.Close();
        }

        _janitor?.Dispose();
        _janitor = null;
        GC.SuppressFinalize(this);
    }

    // -----------------------------------------------------------------------
    // 计数器辅助
    // -----------------------------------------------------------------------

    /// <summary>
    /// 非阻塞预留一个帧的全局额度（调用方须持有 <see cref="_budgetGate"/>）。
    /// </summary>
    /// <param name="kind">帧类型。</param>
    /// <param name="bytes">载荷字节数。</param>
    /// <returns>成功返回 true。</returns>
    private bool TryReserveOutboundLocked(byte kind, int bytes)
    {
        switch (kind)
        {
            case StreamFrameKind.Data:
            {
                var reserved = ReserveCounter(ref _outBytes, bytes, StreamConstants.OutQueueBytesGlobal);
                if (reserved && !ReserveCounter(ref _outFrames, 1, StreamConstants.OutQueueDepthGlobal))
                {
                    ReleaseCounter(ref _outBytes, bytes);
                    reserved = false;
                }

                return reserved;
            }

            case StreamFrameKind.Close:
                // close 取自其独立额度，而非继承连接的 lifecycle 槽：所有权在两个主体间转移必须原子交接，
                // 独立计数器则没有交接，于是「排队失败的 close」与「并发的 shutdown」不会对谁释放什么产生分歧。
                return ReserveCounter(ref _outCloses, 1, StreamConstants.OutCloseDepthGlobal);

            default:
                return ReserveCounter(ref _outControls, 1, StreamConstants.OutControlDepthGlobal);
        }
    }

    /// <summary>
    /// 归还帧的全局额度（调用方须持有 <see cref="_budgetGate"/>）。
    /// </summary>
    /// <param name="kind">帧类型。</param>
    /// <param name="bytes">载荷字节数。</param>
    private void ReleaseOutboundLocked(byte kind, int bytes)
    {
        switch (kind)
        {
            case StreamFrameKind.Data:
                ReleaseCounter(ref _outBytes, bytes);
                ReleaseCounter(ref _outFrames, 1);
                break;
            case StreamFrameKind.Close:
                ReleaseCounter(ref _outCloses, 1);
                break;
            default:
                ReleaseCounter(ref _outControls, 1);
                break;
        }
    }

    /// <summary>
    /// 非阻塞预留一个入站帧的全局额度（调用方须持有 <see cref="_budgetGate"/>）。
    /// </summary>
    /// <param name="bytes">载荷字节数。</param>
    /// <returns>成功返回 true。</returns>
    private bool TryReserveInboundLocked(int bytes)
    {
        var reserved = ReserveCounter(ref _inBytes, bytes, StreamConstants.InQueueBytesGlobal);
        if (reserved && !ReserveCounter(ref _inFrames, 1, StreamConstants.InQueueDepthGlobal))
        {
            ReleaseCounter(ref _inBytes, bytes);
            reserved = false;
        }

        return reserved;
    }

    /// <summary>
    /// 归还入站帧的全局额度（调用方须持有 <see cref="_budgetGate"/>）。
    /// </summary>
    /// <param name="bytes">载荷字节数。</param>
    /// <param name="frames">帧数。</param>
    private void ReleaseInboundLocked(int bytes, int frames)
    {
        ReleaseCounter(ref _inBytes, bytes);
        ReleaseCounter(ref _inFrames, frames);
    }

    /// <summary>
    /// 预留一个计数器额度。对应 Go <c>reserveCounter</c>。
    /// </summary>
    /// <param name="counter">计数器引用。</param>
    /// <param name="amount">预留量。</param>
    /// <param name="limit">上限。</param>
    /// <returns>成功返回 true。</returns>
    private static bool ReserveCounter(ref long counter, long amount, long limit)
    {
        if (amount < 0 || amount > limit)
        {
            return false;
        }

        if (counter + amount > limit)
        {
            return false;
        }

        counter += amount;
        return true;
    }

    /// <summary>
    /// 归还一个计数器额度（不为负）。对应 Go <c>releaseCounter</c>。
    /// </summary>
    /// <param name="counter">计数器引用。</param>
    /// <param name="amount">归还量。</param>
    private static void ReleaseCounter(ref long counter, long amount)
    {
        if (amount <= 0)
        {
            return;
        }

        counter -= amount;
        if (counter < 0)
        {
            counter = 0;
        }
    }

    /// <summary>
    /// Server 模式出站写泵的 sink：把连接的帧经有界队列由单写泵写到一个 socket。
    /// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream_server.go</c> 中的 <c>wsStreamSink</c>。
    /// </summary>
    private sealed class WsStreamSink : IStreamSink
    {
        /// <summary>目标 socket。</summary>
        private readonly WebSocket _socket;

        /// <summary>所属管理器。</summary>
        private readonly StreamManager _manager;

        /// <summary>socket I/O 的取消源（与连接的 ctx 生命周期分离）。</summary>
        private readonly CancellationTokenSource _ioCts;

        /// <summary>保护出站队列的锁。</summary>
        private readonly object _lock = new();

        /// <summary>出站帧队列。</summary>
        private readonly List<OutFrame> _out = new();

        /// <summary>出站队列当前字节数。</summary>
        private int _bytes;

        /// <summary>是否已关闭。</summary>
        private bool _closed;

        /// <summary>等待出站空间的生产者唤醒门。</summary>
        private readonly PulseGate _space = new();

        /// <summary>通知写泵有帧 / 已关闭。</summary>
        private readonly SemaphoreSlim _notify = new(0, 1);

        /// <summary>写泵完成信号。</summary>
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>关闭的 once 标志。</summary>
        private int _shutOnce;

        /// <summary>
        /// 构造 sink 并启动写泵。
        /// </summary>
        /// <param name="socket">WebSocket。</param>
        /// <param name="manager">所属管理器。</param>
        /// <param name="parent">父取消令牌。</param>
        public WsStreamSink(WebSocket socket, StreamManager manager, CancellationToken parent)
        {
            _socket = socket;
            _manager = manager;
            _ioCts = CancellationTokenSource.CreateLinkedTokenSource(parent);
            _ = PumpAsync();
        }

        /// <summary>socket I/O 取消令牌。</summary>
        public CancellationToken IoToken => _ioCts.Token;

        /// <summary>写泵完成信号。</summary>
        public Task Completion => _done.Task;

        /// <summary>
        /// 异步入队（满则等待）。对应 Go <c>wsStreamSink.enqueue(block=true)</c>。
        /// </summary>
        /// <param name="conn">连接。</param>
        /// <param name="connId">连接 id。</param>
        /// <param name="kind">帧类型。</param>
        /// <param name="data">载荷。</param>
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
                    // 控制帧（open ack / error / close）在连接 ctx 取消后仍必须可排队：
                    // 这是对端得知连接结束的唯一途径（对应 Go streamSession.enqueue 对 control 的豁免）。
                    // 仅 sink 自身已关闭、或数据帧遇连接已取消时才拒绝。
                    if (_closed || (!control && conn is not null && conn.Closed.IsCancellationRequested))
                    {
                        throw new StreamClosedException();
                    }

                    hasRoom = control || HasRoom(length);
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

                if (!control && !_manager.TryReserveOutbound(StreamFrameKind.Data, length))
                {
                    throw new StreamClosedException();
                }

                var queued = false;
                lock (_lock)
                {
                    if (_closed || (!control && conn is not null && conn.Closed.IsCancellationRequested))
                    {
                        if (!control)
                        {
                            _manager.ReleaseOutbound(StreamFrameKind.Data, length);
                        }

                        throw new StreamClosedException();
                    }

                    if (control || HasRoom(length))
                    {
                        _out.Add(new OutFrame(connId, kind, data));
                        _bytes += length;
                        queued = true;
                    }
                    else if (!control)
                    {
                        _manager.ReleaseOutbound(StreamFrameKind.Data, length);
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
        /// 非阻塞入队。对应 Go <c>wsStreamSink.enqueue(block=false)</c>。
        /// </summary>
        /// <param name="conn">连接。</param>
        /// <param name="connId">连接 id。</param>
        /// <param name="kind">帧类型。</param>
        /// <param name="data">载荷。</param>
        /// <returns>入队成功返回 true。</returns>
        public bool TryEnqueue(StreamConn? conn, uint connId, byte kind, byte[]? data)
        {
            var control = kind != StreamFrameKind.Data;
            var length = data?.Length ?? 0;

            bool alreadyClosed;
            lock (_lock)
            {
                // 与 SendAsync 一致：控制帧在连接 ctx 取消后仍须可排队（close 通知的唯一途径）。
                alreadyClosed = _closed || (!control && conn is not null && conn.Closed.IsCancellationRequested);
                if (!alreadyClosed && !(control || HasRoom(length)))
                {
                    return false;
                }
            }

            if (alreadyClosed)
            {
                return false;
            }

            if (!control && !_manager.TryReserveOutbound(StreamFrameKind.Data, length))
            {
                return false;
            }

            lock (_lock)
            {
                if (_closed || (!control && conn is not null && conn.Closed.IsCancellationRequested) || !(control || HasRoom(length)))
                {
                    if (!control)
                    {
                        _manager.ReleaseOutbound(StreamFrameKind.Data, length);
                    }

                    return false;
                }

                _out.Add(new OutFrame(connId, kind, data));
                _bytes += length;
            }

            Wake();
            return true;
        }

        /// <summary>
        /// 释放写泵：close 的连接不得让它永久停放。对应 Go <c>wsStreamSink.removeConn</c>。
        /// </summary>
        /// <param name="connId">连接 id（忽略；一个 socket 即一个连接）。</param>
        public void RemoveConn(uint connId)
        {
            lock (_lock)
            {
                _closed = true;
                _space.Pulse();
            }

            Wake();
        }

        /// <summary>非阻塞唤醒写泵。</summary>
        public void Wake()
        {
            try
            {
                _notify.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }

        /// <summary>释放所有等待队列空间的生产者。</summary>
        public void WakeProducers()
        {
            lock (_lock)
            {
                _space.Pulse();
            }
        }

        /// <summary>
        /// 立即中止：取消 I/O、强关 socket、清空队列并归还额度。
        /// </summary>
        public void Abort()
        {
            if (Interlocked.Exchange(ref _shutOnce, 1) != 0)
            {
                return;
            }

            _ioCts.Cancel();
            try
            {
                _socket.Abort();
            }
            catch (Exception)
            {
            }

            lock (_lock)
            {
                _closed = true;
                _space.Pulse();
            }

            ReleaseQueued();
            Wake();
        }

        /// <summary>
        /// 单写泵。WebSocket 不允许并发写，序列化在此同时也按帧被接受顺序保序。
        /// 对应 Go <c>wsStreamSink.pump</c>。
        /// </summary>
        /// <returns>完成的任务。</returns>
        private async Task PumpAsync()
        {
            try
            {
                while (true)
                {
                    OutFrame frame = default;
                    bool have;
                    lock (_lock)
                    {
                        have = _out.Count > 0;
                        if (have)
                        {
                            frame = _out[0];
                            _out.RemoveAt(0);
                            _bytes -= frame.Payload?.Length ?? 0;
                            if (frame.Kind == StreamFrameKind.Data)
                            {
                                _manager.ReleaseOutbound(StreamFrameKind.Data, frame.Payload?.Length ?? 0);
                            }

                            _space.Pulse();
                        }
                        else if (_closed)
                        {
                            break;
                        }
                    }

                    if (!have)
                    {
                        try
                        {
                            await _notify.WaitAsync(_ioCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        continue;
                    }

                    switch (frame.Kind)
                    {
                        case StreamFrameKind.Data:
                            await _socket.SendAsync((frame.Payload ?? []).AsMemory(), WebSocketMessageType.Binary, true, _ioCts.Token).ConfigureAwait(false);
                            break;
                        case StreamFrameKind.Close:
                            await CloseSocketAsync(_socket, WebSocketCloseStatus.NormalClosure, string.Empty, _ioCts.Token).ConfigureAwait(false);
                            return;
                        case StreamFrameKind.Error:
                            await CloseSocketAsync(
                                _socket,
                                WebSocketCloseStatus.InternalServerError,
                                frame.Payload is null ? string.Empty : System.Text.Encoding.UTF8.GetString(frame.Payload),
                                _ioCts.Token).ConfigureAwait(false);
                            return;
                        default:
                            // frameOpen：握手即 ack，无内容可写。
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
            catch (IOException)
            {
            }
            finally
            {
                ReleaseQueued();
                _done.TrySetResult();
            }
        }

        /// <summary>
        /// 队列仍有空间判断：帧数与字节双重上限（控制帧由调用方绕过）。
        /// </summary>
        /// <param name="length">候选帧长度。</param>
        /// <returns>有空间返回 true。</returns>
        private bool HasRoom(int length)
        {
            return _out.Count < StreamConstants.OutQueueDepth &&
                (_out.Count == 0 || _bytes + length <= StreamConstants.OutQueueBytes);
        }

        /// <summary>
        /// 释放队列中所有数据帧的全局额度。
        /// </summary>
        private void ReleaseQueued()
        {
            List<OutFrame>? queued = null;
            lock (_lock)
            {
                if (_out.Count > 0)
                {
                    queued = new List<OutFrame>(_out);
                    _out.Clear();
                    _bytes = 0;
                    _space.Pulse();
                }
            }

            if (queued is null)
            {
                return;
            }

            foreach (var frame in queued)
            {
                if (frame.Kind == StreamFrameKind.Data)
                {
                    _manager.ReleaseOutbound(StreamFrameKind.Data, frame.Payload?.Length ?? 0);
                }
            }
        }
    }
}
