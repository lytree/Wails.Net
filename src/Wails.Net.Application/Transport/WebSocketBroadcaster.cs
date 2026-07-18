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
        return clientId;
    }

    /// <summary>
    /// 移除指定的客户端连接。
    /// </summary>
    /// <param name="clientId">客户端 ID。</param>
    public void RemoveClient(string clientId)
    {
        _clients.TryRemove(clientId, out _);
    }

    /// <summary>
    /// 向所有已连接的客户端广播事件。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据。</param>
    /// <param name="senderWindowId">事件来源窗口 ID，可为 null（应用级事件）。
    /// 包含在前端载荷中使前端可识别事件发起方。
    /// 对应 Wails v3 Go 版本 CustomEvent.Sender 字段语义。</param>
    /// <returns>表示广播操作的异步任务。</returns>
    public async Task BroadcastEventAsync(string eventName, object? data, uint? senderWindowId = null)
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
            senderWindowId
        };

        var json = JsonSerializer.Serialize(message, JsonOptions.DefaultSerializerOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        var tasks = _clients.Values
            .Where(ws => ws.State == WebSocketState.Open)
            .Select(ws => SendAsync(ws, bytes));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 向所有已连接的客户端广播事件（同步包装，用于事件监听器回调）。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据。</param>
    /// <param name="senderWindowId">事件来源窗口 ID，可为 null（应用级事件）。</param>
    public void BroadcastEvent(string eventName, object? data, uint? senderWindowId = null)
    {
        _ = BroadcastEventAsync(eventName, data, senderWindowId);
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
