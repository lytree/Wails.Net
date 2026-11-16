using System.Text.Json;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Channels;
using Wails.Net.Errors;

namespace Wails.Net.Application.Transport;

/// <summary>
/// <see cref="MessageProcessor"/> 的 Channel 协议处理（RH-IPC-05，拆分自主文件）。
/// <para>
/// 上行协议（对齐 Tauri v2 Channel 的注册机制，见 <see cref="Channels.ChannelManager"/>）：
/// <list type="bullet">
/// <item><c>channel:open</c> — 前端创建通道，响应返回 <c>{ channelId }</c>；</item>
/// <item><c>channel:message</c> — 前端向通道发送消息，分发到后端 <see cref="IChannel.OnMessage"/> 订阅者；</item>
/// <item><c>channel:close</c> — 前端关闭通道，后端触发下行关闭信号并从注册表移除。</item>
/// </list>
/// </para>
/// <para>
/// 下行复用事件通道：以 <see cref="ChannelProtocol.EventPrefix"/> 开头的保留事件名
/// 投递到通道所属窗口（无窗口归属则广播），前端 runtime 在派发用户事件回调前拦截。
/// 这样桌面模式（ExecJS / 原生 postMessage）与 Server 模式（WebSocket）无需各写一份下行实现。
/// </para>
/// </summary>
public partial class MessageProcessor
{
    /// <summary>
    /// 处理 channel:* 消息。
    /// </summary>
    /// <param name="message">前端消息。</param>
    /// <returns>响应消息；open 返回通道 ID，message/close 无需响应返回 null。</returns>
    private async Task<ResponseMessage?> ProcessChannelAsync(Message message)
    {
        // 动作提取：channel:open → open；兼容 "channel.open" 写法。
        var sepIndex = message.Type.IndexOfAny(['.', ':']);
        var action = sepIndex > 0 ? message.Type[(sepIndex + 1)..] : string.Empty;

        switch (action)
        {
            case ChannelProtocol.ActionOpen:
                return ProcessChannelOpen(message);
            case ChannelProtocol.ActionMessage:
                return ProcessChannelMessage(message);
            case ChannelProtocol.ActionClose:
                return await ProcessChannelCloseAsync(message).ConfigureAwait(false);
            default:
                return CreateErrorResponse(
                    message.Id,
                    $"无效的 channel 动作 '{action}'（支持 open/message/close）",
                    CallErrorKind.TypeError);
        }
    }

    /// <summary>
    /// 处理 <c>channel:open</c>：创建通道并绑定下行发送委托。
    /// 下行委托按通道所属窗口定向投递（<see cref="Events.EventProcessor.EmitTo"/>）；
    /// 无窗口归属（windowId 为 null）时退化为广播。
    /// </summary>
    /// <param name="message">前端消息。</param>
    /// <returns>包含 channelId 的响应。</returns>
    private ResponseMessage ProcessChannelOpen(Message message)
    {
        ChannelOpenPayload? payload;
        try
        {
            payload = message.Payload.ValueKind == JsonValueKind.Undefined
                ? null
                : message.Payload.Deserialize<ChannelOpenPayload>(JsonOptions.DefaultSerializerOptions);
        }
        catch (JsonException)
        {
            return CreateErrorResponse(message.Id, "无效的 channel:open 载荷", CallErrorKind.TypeError);
        }

        string channelId;
        try
        {
            var channel = Channels.ChannelManager.Create(
                payload?.Id,
                CreateChannelSender(message.WindowId),
                message.WindowId);
            channelId = channel.Id;
        }
        catch (ArgumentException ex)
        {
            // 通道 ID 冲突：幂等语义——已存在的同 ID 通道视为打开成功，
            // 前端重复 open 不会使旧通道失效。
            if (payload?.Id is not null && Channels.ChannelManager.Get(payload.Id) is not null)
            {
                channelId = payload.Id;
            }
            else
            {
                return CreateErrorResponse(message.Id, ex.Message, CallErrorKind.RuntimeError);
            }
        }

        return new ResponseMessage
        {
            Id = message.Id,
            Type = MessageTypes.Response,
            Result = new Dictionary<string, object?> { ["channelId"] = channelId }
        };
    }

    /// <summary>
    /// 处理 <c>channel:message</c>：把前端消息分发到对应通道的 <see cref="IChannel.OnMessage"/> 订阅者。
    /// </summary>
    /// <param name="message">前端消息。</param>
    /// <returns>无需响应，返回 null。</returns>
    private ResponseMessage? ProcessChannelMessage(Message message)
    {
        ChannelInboundPayload? payload;
        try
        {
            payload = message.Payload.Deserialize<ChannelInboundPayload>(JsonOptions.DefaultSerializerOptions);
        }
        catch (JsonException)
        {
            return CreateErrorResponse(message.Id, "无效的 channel:message 载荷", CallErrorKind.TypeError);
        }

        var channelId = payload?.ChannelId;
        if (string.IsNullOrEmpty(channelId))
        {
            return CreateErrorResponse(message.Id, "channel:message 缺少 channelId", CallErrorKind.TypeError);
        }

        var delivered = Channels.ChannelManager.DispatchMessage(
            channelId!,
            payload?.Event,
            new ChannelMessage { Event = payload?.Event, Payload = payload?.Data });

        if (!delivered)
        {
            // 通道不存在或已关闭：以错误响应告知前端，便于其清理本地句柄。
            return CreateErrorResponse(message.Id, $"通道 '{channelId}' 不存在或已关闭", CallErrorKind.ReferenceError);
        }

        return null;
    }

    /// <summary>
    /// 处理 <c>channel:close</c>：关闭通道（触发下行关闭信号）并从注册表移除。
    /// </summary>
    /// <param name="message">前端消息。</param>
    /// <returns>包含 closed 标记的响应；通道不存在时返回 closed=false（幂等）。</returns>
    private async Task<ResponseMessage?> ProcessChannelCloseAsync(Message message)
    {
        ChannelClosePayload? payload;
        try
        {
            payload = message.Payload.Deserialize<ChannelClosePayload>(JsonOptions.DefaultSerializerOptions);
        }
        catch (JsonException)
        {
            return CreateErrorResponse(message.Id, "无效的 channel:close 载荷", CallErrorKind.TypeError);
        }

        var channelId = payload?.ChannelId;
        if (string.IsNullOrEmpty(channelId))
        {
            return CreateErrorResponse(message.Id, "channel:close 缺少 channelId", CallErrorKind.TypeError);
        }

        if (Channels.ChannelManager.Get(channelId!) is { } channel)
        {
            try
            {
                // 幂等：重复关闭直接返回完成。关闭信号经下行发送委托通知前端。
                await channel.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // 已被并发关闭：视为成功。
            }

            Channels.ChannelManager.Remove(channelId!);
        }

        return new ResponseMessage
        {
            Id = message.Id,
            Type = MessageTypes.Response,
            Result = new Dictionary<string, object?> { ["closed"] = true }
        };
    }

    /// <summary>
    /// 构造通道下行发送委托：经事件通道把 <see cref="ChannelDownlink"/> 投递到通道所属窗口。
    /// <paramref name="payload"/> 为 null 表示关闭信号（与 <see cref="Channel"/> 的既有约定一致）。
    /// </summary>
    /// <param name="windowId">通道所属窗口 ID，可为 null（广播）。</param>
    /// <returns>发送委托。</returns>
    private Func<string, object?, CancellationToken, Task> CreateChannelSender(uint? windowId)
        => (channelId, payload, _) =>
        {
            var name = ChannelProtocol.EventName(channelId);
            var data = new ChannelDownlink
            {
                Payload = payload,
                Closed = payload is null,
            };

            if (windowId is { } id)
            {
                _events.EmitTo(id, name, data);
            }
            else
            {
                _events.Emit(name, data);
            }

            return Task.CompletedTask;
        };
}
