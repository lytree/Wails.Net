namespace Wails.Net.Application.Channels;

/// <summary>
/// Channel 前后端协议常量与载荷形状（RH-IPC-05）。
/// <para>
/// 上行（前端 → 后端）由 <see cref="Transport.MessageProcessor"/> 处理三类消息：
/// <c>channel:open</c> / <c>channel:message</c> / <c>channel:close</c>。
/// 下行（后端 → 前端）复用事件下行通道：以 <see cref="EventPrefix"/> 开头的保留事件名
/// 投递到通道所属窗口，前端 runtime 在派发用户回调前拦截该命名空间。
/// </para>
/// <para>
/// 复用事件下行而不是新增一条传输通道，是为了让 Channel 在桌面（ExecJS / 原生 postMessage）
/// 与 Server 模式（WebSocket）下天然可用，无需为每类传输各写一份下行实现。
/// <b>副作用</b>：用户不应使用 <c>wails:channel:</c> 前缀发布事件——该命名空间为框架保留。
/// </para>
/// </summary>
public static class ChannelProtocol
{
    /// <summary>
    /// 通道消息的基础类型（上行消息类型前缀）。
    /// </summary>
    public const string MessageBaseType = "channel";

    /// <summary>前端创建通道。</summary>
    public const string ActionOpen = "open";

    /// <summary>前端向通道发送消息。</summary>
    public const string ActionMessage = "message";

    /// <summary>前端关闭通道。</summary>
    public const string ActionClose = "close";

    /// <summary>
    /// 下行事件名保留前缀：完整事件名为 <c>wails:channel:&lt;channelId&gt;</c>。
    /// </summary>
    public const string EventPrefix = "wails:channel:";

    /// <summary>
    /// 构造通道下行事件名。
    /// </summary>
    /// <param name="channelId">通道 ID。</param>
    /// <returns>下行事件名。</returns>
    public static string EventName(string channelId) => EventPrefix + channelId;

    /// <summary>
    /// 判断事件名是否属于通道保留命名空间。
    /// </summary>
    /// <param name="eventName">事件名。</param>
    /// <returns>属于通道命名空间返回 true。</returns>
    public static bool IsChannelEvent(string eventName)
        => eventName.StartsWith(EventPrefix, StringComparison.Ordinal);
}

/// <summary>
/// 通道下行载荷：随保留事件名一起投递到前端。
/// </summary>
public sealed class ChannelDownlink
{
    /// <summary>
    /// 事件标识（当前后端 <see cref="IChannel.SendAsync"/> 不携带事件标识，预留）。
    /// </summary>
    public string? Event { get; set; }

    /// <summary>
    /// 业务负载。<see cref="Closed"/> 为 true 时无意义。
    /// </summary>
    public object? Payload { get; set; }

    /// <summary>
    /// 是否为关闭信号（后端调用 <see cref="IChannel.CloseAsync"/> 时置 true）。
    /// </summary>
    public bool Closed { get; set; }
}

/// <summary>
/// <c>channel:open</c> 消息载荷。
/// </summary>
public sealed class ChannelOpenPayload
{
    /// <summary>
    /// 前端指定的通道 ID；为 null 时由后端生成（GUID 无连字符形式）。
    /// </summary>
    public string? Id { get; set; }
}

/// <summary>
/// <c>channel:message</c> 消息载荷。
/// </summary>
public sealed class ChannelInboundPayload
{
    /// <summary>
    /// 目标通道 ID。
    /// </summary>
    public string? ChannelId { get; set; }

    /// <summary>
    /// 可选事件标识，透传给 <see cref="IChannel.OnMessage"/> 的订阅者。
    /// </summary>
    public string? Event { get; set; }

    /// <summary>
    /// 业务负载。
    /// </summary>
    public object? Data { get; set; }
}

/// <summary>
/// <c>channel:close</c> 消息载荷。
/// </summary>
public sealed class ChannelClosePayload
{
    /// <summary>
    /// 要关闭的通道 ID。
    /// </summary>
    public string? ChannelId { get; set; }
}
