namespace Wails.Net.Application.Channels;

/// <summary>
/// 通道消息载荷。
/// 对应 Tauri v2 Channel 的消息格式：包含可选的事件 ID 与任意负载。
/// </summary>
public sealed class ChannelMessage
{
    /// <summary>
    /// 事件标识（用于响应匹配，可为 null）。
    /// </summary>
    public string? Event { get; set; }

    /// <summary>
    /// 消息负载，可为 null。
    /// </summary>
    public object? Payload { get; set; }
}

/// <summary>
/// 长生命周期双向通道接口。
/// 对应 Tauri v2 的 <c>Channel&lt;T&gt;</c>，弥补请求-响应模型的局限：
/// 允许后端主动向前端推送流式消息，前端也可通过同一通道回送消息。
/// <para>
/// 通道由 <see cref="ChannelManager"/> 创建与注册，通过唯一 <see cref="Id"/> 标识。
/// 关闭后（<see cref="CloseAsync"/>）的所有操作抛出 <see cref="ObjectDisposedException"/>。
/// </para>
/// </summary>
public interface IChannel
{
    /// <summary>
    /// 通道唯一标识。
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 获取通道是否已关闭。
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// 当通道收到消息时触发。
    /// 订阅者可在此回调中处理来自对端的消息。
    /// </summary>
    event Action<string?, ChannelMessage>? OnMessage;

    /// <summary>
    /// 异步发送消息到对端。
    /// </summary>
    /// <param name="payload">消息负载，可为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示发送操作的异步任务。</returns>
    /// <exception cref="ObjectDisposedException">通道已关闭时抛出。</exception>
    Task SendAsync(object? payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步关闭通道。
    /// 重复调用安全（幂等）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示关闭操作的异步任务。</returns>
    Task CloseAsync(CancellationToken cancellationToken = default);
}
