using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;

namespace Wails.Net.Application.Transport;

/// <summary>
/// WebSocket 事件广播器，用于 Server 模式下向前端推送事件。
/// 对应 Wails v3 Go 版本中的 WebSocket 事件广播实现。
/// 支持全量广播、定向发送及排除指定客户端广播。
/// </summary>
public class WebSocketBroadcaster
{
    /// <summary>
    /// 已连接的 WebSocket 客户端集合：客户端 ID → WebSocket 连接。
    /// </summary>
    private readonly ConcurrentDictionary<string, WebSocket> _clients = new();

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
    public int ClientCount => _clients.Count;

    /// <summary>
    /// 注册一个新的 WebSocket 客户端连接。
    /// </summary>
    /// <param name="webSocket">WebSocket 连接实例。</param>
    /// <returns>客户端 ID。</returns>
    public string AddClient(WebSocket webSocket)
    {
        var clientId = Interlocked.Increment(ref _nextClientId).ToString();
        _clients[clientId] = webSocket;
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
        if (_clients.ContainsKey(clientId))
        {
            _clientWindowIds[clientId] = windowId;
        }
    }

    /// <summary>
    /// 移除指定的客户端连接。
    /// </summary>
    /// <param name="clientId">客户端 ID。</param>
    public void RemoveClient(string clientId)
    {
        _clients.TryRemove(clientId, out _);
        _clientWindowIds.TryRemove(clientId, out _);
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
        if (_clients.IsEmpty)
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
        var targets = targetWindowId is { } windowTarget
            ? _clients.Where(kvp =>
                kvp.Value.State == WebSocketState.Open &&
                _clientWindowIds.TryGetValue(kvp.Key, out var mapped) &&
                mapped == windowTarget)
            : _clients.Where(kvp => kvp.Value.State == WebSocketState.Open);

        var tasks = targets.Select(kvp => SendAsync(kvp.Value, bytes));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 向所有已连接的客户端广播事件（同步包装，用于事件监听器回调）。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据。</param>
    /// <param name="senderWindowId">事件来源窗口 ID，可为 null（应用级事件）。</param>
    /// <param name="targetWindowId">定向目标窗口 ID（OPT-EVENT-002）；null 表示广播。</param>
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
        if (!_clients.TryGetValue(clientId, out var webSocket) || webSocket.State != WebSocketState.Open)
        {
            return;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(message);
        await SendAsync(webSocket, bytes);
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
        if (_clients.IsEmpty)
        {
            return;
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(message);

        var tasks = _clients
            .Where(kvp => kvp.Key != exceptClientId && kvp.Value.State == WebSocketState.Open)
            .Select(kvp => SendAsync(kvp.Value, bytes));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 向所有已连接的客户端广播事件，但排除指定客户端。
    /// </summary>
    /// <param name="exceptClientId">要排除的客户端 ID。</param>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据。</param>
    /// <param name="senderWindowId">事件来源窗口 ID，可为 null（应用级事件）。</param>
    /// <returns>表示广播操作的异步任务。</returns>
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
        await SendToAllExceptAsync(exceptClientId, json);
    }

    /// <summary>
    /// 向指定客户端发送消息（同步包装）。
    /// </summary>
    /// <param name="clientId">目标客户端 ID。</param>
    /// <param name="message">消息字符串。</param>
    public void SendToClient(string clientId, string message)
    {
        _ = SendToClientAsync(clientId, message);
    }

    /// <summary>
    /// 向所有已连接的客户端广播消息，但排除指定客户端（同步包装）。
    /// </summary>
    /// <param name="exceptClientId">要排除的客户端 ID。</param>
    /// <param name="message">消息字符串。</param>
    public void SendToAllExcept(string exceptClientId, string message)
    {
        _ = SendToAllExceptAsync(exceptClientId, message);
    }

    /// <summary>
    /// 向指定 WebSocket 连接发送字节消息。
    /// </summary>
    /// <param name="webSocket">WebSocket 连接。</param>
    /// <param name="bytes">消息字节。</param>
    /// <returns>表示发送操作的异步任务。</returns>
    private static async Task SendAsync(WebSocket webSocket, byte[] bytes)
    {
        try
        {
            await webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                CancellationToken.None);
        }
        catch
        {
            // 发送失败（客户端已断开等），忽略
        }
    }

    /// <summary>
    /// 关闭所有客户端连接并停止广播。
    /// </summary>
    /// <returns>表示停止操作的异步任务。</returns>
    public async Task StopAsync()
    {
        var tasks = _clients.Values
            .Where(ws => ws.State == WebSocketState.Open)
            .Select(ws => ws.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Server shutting down",
                CancellationToken.None));

        await Task.WhenAll(tasks);
        _clients.Clear();
    }
}
