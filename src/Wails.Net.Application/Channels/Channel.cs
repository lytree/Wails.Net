using System.Threading;

namespace Wails.Net.Application.Channels;

/// <summary>
/// <see cref="IChannel"/> 的默认实现。
/// 通过传入的发送委托将消息投递到底层传输层（如 WebSocket、HTTP、IPC 等），
/// 收到消息时通过 <see cref="ReceiveMessage"/> 内部方法触发 <see cref="OnMessage"/> 事件。
/// <para>
/// 线程安全：使用 <see cref="Interlocked"/> 保护关闭状态，避免并发关闭与发送冲突。
/// </para>
/// </summary>
internal sealed class Channel : IChannel
{
    /// <summary>
    /// 发送委托：将通道 ID 与负载传递给底层传输层。
    /// 第一个参数为通道 ID，第二个为负载（null 表示关闭信号）。
    /// </summary>
    private readonly Func<string, object?, CancellationToken, Task> _sender;

    /// <summary>
    /// 关闭状态：0=开放，1=已关闭。使用 <see cref="Interlocked"/> 原子操作。
    /// </summary>
    private int _closed;

    /// <summary>
    /// 初始化通道实例。
    /// </summary>
    /// <param name="id">通道唯一标识。</param>
    /// <param name="sender">发送委托。</param>
    internal Channel(string id, Func<string, object?, CancellationToken, Task> sender)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public bool IsClosed => Interlocked.CompareExchange(ref _closed, 0, 0) == 1;

    /// <inheritdoc />
    public event Action<string?, ChannelMessage>? OnMessage;

    /// <inheritdoc />
    public async Task SendAsync(object? payload, CancellationToken cancellationToken = default)
    {
        if (IsClosed)
        {
            throw new ObjectDisposedException(
                nameof(Channel),
                $"通道 {Id} 已关闭，无法发送消息");
        }

        await _sender(Id, payload, cancellationToken);
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        // 幂等：若已关闭则直接返回
        if (Interlocked.CompareExchange(ref _closed, 1, 0) == 1)
        {
            return Task.CompletedTask;
        }

        // 发送关闭信号（payload=null）让对端感知
        return _sender(Id, null, cancellationToken);
    }

    /// <summary>
    /// 内部方法：从底层传输层接收到消息后调用，触发 <see cref="OnMessage"/> 事件。
    /// </summary>
    /// <param name="eventId">事件标识，可为 null。</param>
    /// <param name="message">消息载荷。</param>
    /// <exception cref="ObjectDisposedException">通道已关闭时抛出。</exception>
    internal void ReceiveMessage(string? eventId, ChannelMessage message)
    {
        if (IsClosed)
        {
            throw new ObjectDisposedException(
                nameof(Channel),
                $"通道 {Id} 已关闭，无法接收消息");
        }

        OnMessage?.Invoke(eventId, message);
    }
}
