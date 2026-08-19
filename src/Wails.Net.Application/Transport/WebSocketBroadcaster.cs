using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;

namespace Wails.Net.Application.Transport;

/// <summary>
/// WebSocket 事件广播器，用于 Server 模式下向前端推送事件。
/// 对应 Wails v3 Go 版本中的 WebSocket 事件广播实现。
/// 支持全量广播、定向发送及排除指定客户端广播。
/// <para>
/// R2-2 融合审计：每个客户端连接使用独立的 <see cref="ClientMailbox"/>（单写者 Channel），
/// 保证同一连接上事件与响应顺序一致，避免并发 <c>SendAsync</c> 导致帧交错；
/// 当邮箱满时 <see cref="ClientMailbox.EnqueueAsync"/> 阻塞生产者形成背压，
/// 同时通过 <see cref="ClientMailbox.DisposeAsync"/> 等待写入循环退出，避免 StopAsync 之后的悬挂写入。
/// </para>
/// <para>
/// R2-3 融合审计：暴露 <see cref="MessagesEnqueued"/> / <see cref="MessagesSent"/> / <see cref="MessagesDropped"/>
/// 计数器（Interlocked 安全），让运维能观测 mailbox 健康度。
/// </para>
/// </summary>
public class WebSocketBroadcaster
{
    /// <summary>
    /// 已连接的 WebSocket 客户端集合：客户端 ID → 客户端邮箱。
    /// </summary>
    private readonly ConcurrentDictionary<string, ClientMailbox> _mailboxes = new(StringComparer.Ordinal);

    /// <summary>
    /// 默认 mailbox 容量。足够容纳高负载事件流下的突发消息；超过则
    /// <see cref="ClientMailbox.EnqueueAsync"/> 阻塞生产者形成背压，
    /// 避免无界队列耗尽内存。
    /// </summary>
    public const int DefaultMailboxCapacity = 256;

    private readonly int _mailboxCapacity;

    private long _messagesEnqueued;
    private long _messagesSent;
    private long _messagesDropped;

    /// <summary>累计入队的消息数（已成功写入 mailbox）。</summary>
    public long MessagesEnqueued => Interlocked.Read(ref _messagesEnqueued);

    /// <summary>累计成功通过底层 WebSocket SendAsync 发送的消息数。</summary>
    public long MessagesSent => Interlocked.Read(ref _messagesSent);

    /// <summary>累计丢弃的消息数（邮箱已释放 / ObjectDisposed / OperationCanceled）。</summary>
    public long MessagesDropped => Interlocked.Read(ref _messagesDropped);

    /// <summary>
    /// 关闭阶段等待写入循环退出消息排空的最大时长。
    /// 超过此值仍有关闭挂起的连接将被强制关闭，避免 <see cref="StopAsync"/> 卡死。
    /// </summary>
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger<WebSocketBroadcaster>? _logger;

    /// <summary>
    /// 客户端 → 窗口 ID 映射（§2.5 卫生项）。
    /// 由握手首消息携带的 windowId 建立，用于实现事件真定向（OPT-EVENT-002）。
    /// 值为 null 表示尚未建立窗口映射（客户端为全局/未绑定）。
    /// </summary>
    private readonly ConcurrentDictionary<string, uint?> _clientWindowIds = new(StringComparer.Ordinal);

    /// <summary>
    /// 客户端 ID 生成器（线程安全递增）。
    /// </summary>
    private int _nextClientId = 1;

    /// <summary>
    /// 获取当前连接的客户端数量。
    /// </summary>
    public int ClientCount => _mailboxes.Count;

    public WebSocketBroadcaster()
        : this(null, DefaultMailboxCapacity)
    {
    }

    /// <summary>
    /// 使用可选日志记录器构造广播器。
    /// </summary>
    /// <param name="logger">日志记录器，可为 null（无日志输出）。</param>
    public WebSocketBroadcaster(ILogger<WebSocketBroadcaster>? logger)
        : this(logger, DefaultMailboxCapacity)
    {
    }

    /// <summary>
    /// 使用可选日志记录器与 mailbox 容量构造广播器。
    /// </summary>
    /// <param name="logger">日志记录器，可为 null。</param>
    /// <param name="mailboxCapacity">每客户端 mailbox 容量，&lt;= 0 时回退到 <see cref="DefaultMailboxCapacity"/>。</param>
    public WebSocketBroadcaster(ILogger<WebSocketBroadcaster>? logger, int mailboxCapacity)
    {
        _logger = logger;
        _mailboxCapacity = mailboxCapacity > 0 ? mailboxCapacity : DefaultMailboxCapacity;
    }

    /// <summary>
    /// 注册一个新的 WebSocket 客户端连接。
    /// </summary>
    /// <param name="webSocket">WebSocket 连接实例。</param>
    /// <returns>客户端 ID。</returns>
    public string AddClient(WebSocket webSocket)
    {
        var clientId = Interlocked.Increment(ref _nextClientId).ToString();
        // R2-2：每个连接创建独立 ClientMailbox（单写者顺序发送），避免并发 SendAsync 导致帧交错。
        _mailboxes[clientId] = new ClientMailbox(webSocket, _mailboxCapacity, _logger, this);
        _clientWindowIds[clientId] = null;
        return clientId;
    }

    /// <summary>
    /// 为客户端建立窗口映射（§2.5 卫生项）。
    /// 由接收循环在首条消息携带 windowId 时调用；重复调用以最新值为准。
    /// </summary>
    /// <param name="clientId">客户端 ID。</param>
    /// <param name="windowId">窗口 ID；null 表示未绑定。</param>
    public void SetClientWindow(string clientId, uint? windowId)
    {
        if (_mailboxes.ContainsKey(clientId))
        {
            _clientWindowIds[clientId] = windowId;
        }
    }

    /// <summary>
    /// 异步移除客户端连接。
    /// 会等待邮箱的写入循环退出，避免悬挂 SendAsync 访问已释放 socket。
    /// </summary>
    /// <param name="clientId">客户端 ID。</param>
    /// <returns>异步移除任务。</returns>
    public Task RemoveClientAsync(string clientId)
    {
        if (_mailboxes.TryRemove(clientId, out var mailbox))
        {
            _clientWindowIds.TryRemove(clientId, out _);
            return mailbox.DisposeAsync(ShutdownDrainTimeout);
        }

        _clientWindowIds.TryRemove(clientId, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 同步移除客户端连接（向后兼容 API）。
    /// </summary>
    /// <param name="clientId">客户端 ID。</param>
    public void RemoveClient(string clientId)
    {
        _ = RemoveClientAsync(clientId);
    }

    /// <summary>
    /// 向所有已连接的客户端广播事件。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据。</param>
    /// <param name="senderWindowId">事件来源窗口 ID，可为 null（应用级事件）。
    /// 包含在前端载荷中使前端可识别事件发起方。
    /// 对应 Wails v3 Go 版本 CustomEvent.Sender 字段语义。</param>
    /// <param name="targetWindowId">定向目标窗口 ID（OPT-EVENT-002）；null 表示广播。
    /// Server 模式下 WS 客户端无窗口映射，事件仍广播，但载荷携带 targetWindowId 供前端识别。</param>
    /// <returns>表示广播操作的异步任务。</returns>
    public async Task BroadcastEventAsync(string eventName, object? data, uint? senderWindowId = null, uint? targetWindowId = null)
    {
        if (_mailboxes.IsEmpty)
        {
            return;
        }

        var message = new
        {
            type = "event",
            name = eventName,
            data,
            senderWindowId,
            targetWindowId
        };

        var json = JsonSerializer.Serialize(message, JsonOptions.DefaultSerializerOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        // OPT-EVENT-002 真定向：底层的 targetWindowId 同时过滤发送目标（§2.5 卫生项）。
        // 仅向与目标窗口匹配的客户端发送；未建立窗口映射的客户端（全局）不接收定向事件。
        // R2-2：通过 mailbox 顺序入队；写入循环会自行检查 socket 状态。
        var targetIds = targetWindowId is { } windowTarget
            ? _mailboxes.Where(kvp =>
                _clientWindowIds.TryGetValue(kvp.Key, out var mapped) &&
                mapped == windowTarget).Select(kvp => kvp.Key).ToArray()
            : _mailboxes.Keys.ToArray();

        var tasks = targetIds.Select(id =>
        {
            if (!_mailboxes.TryGetValue(id, out var m))
            {
                return Task.CompletedTask;
            }
            return EnqueueOrLogAsync(m, bytes);
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 将字节消息入队到指定 mailbox；忽略已释放或关闭的连接。
    /// </summary>
    private async Task EnqueueOrLogAsync(ClientMailbox mailbox, byte[] bytes)
    {
        try
        {
            await mailbox.EnqueueAsync(bytes).ConfigureAwait(false);
            Interlocked.Increment(ref _messagesEnqueued);
        }
        catch (ChannelClosedException)
        {
            // 邮箱已关闭（客户端断开/StopAsync），记录并忽略。
            Interlocked.Increment(ref _messagesDropped);
        }
        catch (ObjectDisposedException)
        {
            // 写入循环的资源已释放，记录并忽略。
            Interlocked.Increment(ref _messagesDropped);
        }
        catch (OperationCanceledException)
        {
            // 邮箱关闭时取消了正在等待的入队，记录并忽略。
            Interlocked.Increment(ref _messagesDropped);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _messagesDropped);
            _logger?.LogDebug(ex, "WebSocketBroadcaster 入队失败");
        }
    }

    /// <summary>
    /// 向所有已连接的客户端广播事件（同步包装，用于事件监听器回调）。
    /// </summary>
    public void BroadcastEvent(string eventName, object? data, uint? senderWindowId = null, uint? targetWindowId = null)
    {
        _ = BroadcastEventAsync(eventName, data, senderWindowId, targetWindowId);
    }

    /// <summary>
    /// 向指定客户端发送消息。
    /// 若客户端不存在或未连接则忽略。
    /// </summary>
    /// <param name="clientId">目标客户端 ID。</param>
    /// <param name="message">消息字符串（通常为 JSON）。</param>
    /// <returns>表示发送操作的异步任务。</returns>
    public async Task SendToClientAsync(string clientId, string message)
    {
        if (!_mailboxes.TryGetValue(clientId, out var mailbox))
        {
            return;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(message);
        await EnqueueOrLogAsync(mailbox, bytes).ConfigureAwait(false);
    }

    /// <summary>
    /// 向所有已连接的客户端广播消息，但排除指定客户端。
    /// 用于事件不应回传给发送者窗口的场景。
    /// </summary>
    /// <param name="exceptClientId">要排除的客户端 ID。</param>
    /// <param name="message">消息字符串（通常为 JSON）。</param>
    /// <returns>表示广播操作的异步任务。</returns>
    public async Task SendToAllExceptAsync(string exceptClientId, string message)
    {
        if (_mailboxes.IsEmpty)
        {
            return;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(message);

        var tasks = _mailboxes
            .Where(kvp => kvp.Key != exceptClientId)
            .Select(kvp => EnqueueOrLogAsync(kvp.Value, bytes));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 向所有已连接的客户端广播事件，但排除指定客户端。
    /// </summary>
    public async Task BroadcastEventExceptAsync(string exceptClientId, string eventName, object? data, uint? senderWindowId = null)
    {
        var message = new
        {
            type = "event",
            name = eventName,
            data,
            senderWindowId
        };

        var json = JsonSerializer.Serialize(message, JsonOptions.DefaultSerializerOptions);
        await SendToAllExceptAsync(exceptClientId, json).ConfigureAwait(false);
    }

    /// <summary>
    /// 向指定客户端发送消息（同步包装）。
    /// </summary>
    public void SendToClient(string clientId, string message)
    {
        _ = SendToClientAsync(clientId, message);
    }

    /// <summary>
    /// 向所有已连接的客户端广播消息，但排除指定客户端（同步包装）。
    /// </summary>
    public void SendToAllExcept(string exceptClientId, string message)
    {
        _ = SendToAllExceptAsync(exceptClientId, message);
    }

    /// <summary>
    /// 关闭所有客户端连接并停止广播。
    /// </summary>
    /// <param name="cancellationToken">取消令牌，可选。</param>
    /// <returns>表示停止操作的异步任务。</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _mailboxes.Values.ToArray();
        _mailboxes.Clear();
        _clientWindowIds.Clear();

        // 先进行正常关闭握手，让前端感知到 graceful shutdown。
        var closeTasks = snapshot.Select(m => m.CloseSocketAsync(cancellationToken));
        try
        {
            await Task.WhenAll(closeTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消时仍需继续释放 mailbox，避免泄漏。
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "WebSocketBroadcaster 关闭握手阶段出现异常");
        }

        // 再释放 mailbox，确保写入循环退出。
        var disposeTasks = snapshot.Select(m => m.DisposeAsync(ShutdownDrainTimeout));
        await Task.WhenAll(disposeTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 关闭所有客户端连接并停止广播（向后兼容 API）。
    /// </summary>
    public Task StopAsync() => StopAsync(CancellationToken.None);

    // 供 ClientMailbox 在成功 SendAsync 后递增；使用 ref 访问避免暴露整个 broadcaster。
    internal void IncrementMessagesSent() => Interlocked.Increment(ref _messagesSent);
    internal void IncrementMessagesDropped() => Interlocked.Increment(ref _messagesDropped);
}

/// <summary>
/// 每连接的 WebSocket 邮箱（R2-2 融合审计：单写者保证事件/响应顺序）。
/// 消息通过 <see cref="Channel{T}"/> 排队，由单一写入任务顺序发送，
/// 避免同一连接上并发 <c>SendAsync</c> 导致帧交错。
/// <para>
/// 容量限制（默认 <see cref="WebSocketBroadcaster.DefaultMailboxCapacity"/>）：
/// 当邮箱满时，<see cref="EnqueueAsync"/> 阻塞调用方形成背压，
/// 强制生产者放慢节奏（避免无界内存增长掩盖慢消费）。
/// </para>
/// <para>
/// <b>线程安全</b>：<see cref="ChannelWriter{T}.TryWrite"/> 内部加锁，
/// 无需额外同步。写入任务 <c>_writer</c> 是单线程循环，
/// 不需要锁保护 <c>SendAsync</c> 调用。
/// </para>
/// </summary>
internal sealed class ClientMailbox
{
    private readonly WebSocket _socket;
    private readonly Channel<ReadOnlyMemory<byte>> _queue;
    private readonly Task _writer;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger? _logger;
    private readonly WebSocketBroadcaster _owner;
    private readonly TaskCompletionSource _writerExited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// mailbox 状态：0=active，1=disposing，2=disposed。
    /// </summary>
    private int _state;

    public ClientMailbox(WebSocket socket, int capacity, ILogger? logger, WebSocketBroadcaster owner)
    {
        _socket = socket;
        _logger = logger;
        _owner = owner;
        _queue = Channel.CreateBounded<ReadOnlyMemory<byte>>(
            new BoundedChannelOptions(capacity)
            {
                // Wait 模式：调用 EnqueueAsync 时若邮箱满则阻塞，形成背压
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        _writer = Task.Run(RunAsync);
    }

    /// <summary>
    /// 将字节消息加入发送队列。
    /// </summary>
    /// <param name="message">UTF-8 编码的消息字节。</param>
    /// <returns>异步等待任务；当邮箱关闭或连接已释放时立即完成。</returns>
    public ValueTask EnqueueAsync(ReadOnlyMemory<byte> message)
    {
        if (Volatile.Read(ref _state) != 0)
        {
            return ValueTask.CompletedTask;
        }
        return _queue.Writer.WriteAsync(message, _cts.Token);
    }

    /// <summary>
    /// 同步入队（不阻塞）。邮箱满时丢弃并返回 false，由调用方处理（用于低优先级控制消息）。
    /// </summary>
    public bool TryEnqueue(ReadOnlyMemory<byte> message)
    {
        if (Volatile.Read(ref _state) != 0)
        {
            return false;
        }
        return _queue.Writer.TryWrite(message);
    }

    /// <summary>
    /// 后台写入循环：从队列读取消息，依次调用 <see cref="WebSocket.SendAsync"/>。
    /// </summary>
    private async Task RunAsync()
    {
        try
        {
            await foreach (var msg in _queue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    if (_socket.State == WebSocketState.Open)
                    {
                        await _socket.SendAsync(msg, WebSocketMessageType.Text, endOfMessage: true, _cts.Token).ConfigureAwait(false);
                        _owner.IncrementMessagesSent();
                    }
                }
                catch
                {
                    // 发送失败（客户端已断开等），停止写入循环释放资源。
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 关闭时正常取消。
        }
        finally
        {
            _writerExited.TrySetResult();
        }
    }

    /// <summary>
    /// 关闭底层 WebSocket 连接（正常关闭握手）。
    /// </summary>
    internal Task CloseSocketAsync(CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open && _socket.State != WebSocketState.CloseReceived)
        {
            return Task.CompletedTask;
        }

        try
        {
            return _socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Server shutting down",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "WebSocket 关闭握手失败");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 异步释放 mailbox：完成队列、取消写入任务并等待写入循环退出（最多 drainTimeout）。
    /// 多次调用安全：第一次执行实际释放，后续直接等待首次调用完成。
    /// </summary>
    internal async Task DisposeAsync(TimeSpan drainTimeout)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            await _writerExited.Task.ConfigureAwait(false);
            return;
        }

        // 完成队列：WriteAsync 会立即完成，循环自然退出。
        _queue.Writer.TryComplete();

        // 取消仍阻塞的写入并唤醒读取循环。
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            await _writerExited.Task.WaitAsync(drainTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _owner.IncrementMessagesDropped();
            _logger?.LogWarning("ClientMailbox 写入循环在 {DrainTimeout} 内未退出", drainTimeout);
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
        Volatile.Write(ref _state, 2);
    }
}
