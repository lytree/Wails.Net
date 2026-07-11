using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// WebSocket 插件，提供前端创建和管理 WebSocket 连接的能力。
/// 对应 Tauri v2 的 WebSocket 通信功能。
/// 每个连接通过唯一 ID 标识，支持连接、发送文本、关闭和状态查询。
/// </summary>
public class WebSocketPlugin : IPlugin
{
    /// <summary>
    /// 连接超时时长（毫秒）。
    /// </summary>
    private const int ConnectTimeoutMs = 10000;

    /// <summary>
    /// 接收缓冲区大小。
    /// </summary>
    private const int ReceiveBufferSize = 8192;

    /// <summary>
    /// 活跃的 WebSocket 连接，按连接 ID 索引。
    /// 使用并发字典保证线程安全。
    /// </summary>
    private static readonly ConcurrentDictionary<string, ClientWebSocket> _connections = new();

    /// <summary>插件名称</summary>
    public string Name => "websocket";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册 WebSocket 相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 连接到指定 URL
        context.Commands.MapCommand("websocket.connect",
            (Func<ICommandContext, string, Task<string>>)(async (ctx, url) =>
        {
            try
            {
                var ws = new ClientWebSocket();
                using var cts = new CancellationTokenSource(ConnectTimeoutMs);
                await ws.ConnectAsync(new Uri(url), cts.Token);

                var connectionId = Guid.NewGuid().ToString("N");
                _connections[connectionId] = ws;

                // 启动后台接收循环，将收到的消息通过事件分发到前端。
                _ = Task.Run(() => ReceiveLoopAsync(connectionId, ws));

                return JsonSerializer.Serialize(new { id = connectionId, connected = true });
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { connected = false, error = ex.Message });
            }
        }));

        // 发送文本消息
        context.Commands.MapCommand("websocket.send",
            (Func<ICommandContext, string, string, Task<bool>>)(async (ctx, connectionId, message) =>
        {
            if (!_connections.TryGetValue(connectionId, out var ws))
            {
                return false;
            }

            if (ws.State != WebSocketState.Open)
            {
                return false;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
                return true;
            }
            catch
            {
                return false;
            }
        }));

        // 关闭连接
        context.Commands.MapCommand("websocket.close",
            (Func<ICommandContext, string, Task<bool>>)(async (ctx, connectionId) =>
        {
            if (!_connections.TryRemove(connectionId, out var ws))
            {
                return false;
            }

            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                }

                ws.Dispose();
                return true;
            }
            catch
            {
                ws.Dispose();
                return false;
            }
        }));

        // 查询连接状态
        context.Commands.MapCommand("websocket.getState",
            (Func<ICommandContext, string, string>)((ctx, connectionId) =>
        {
            if (!_connections.TryGetValue(connectionId, out var ws))
            {
                return "disconnected";
            }

            return ws.State.ToString().ToLowerInvariant();
        }));

        // 发送二进制数据（Base64 编码）
        context.Commands.MapCommand("websocket.sendBinary",
            (Func<ICommandContext, string, string, Task<bool>>)(async (ctx, connectionId, base64Data) =>
        {
            if (!_connections.TryGetValue(connectionId, out var ws))
            {
                return false;
            }

            if (ws.State != WebSocketState.Open)
            {
                return false;
            }

            try
            {
                var bytes = Convert.FromBase64String(base64Data);
                await ws.SendAsync(bytes, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
                return true;
            }
            catch
            {
                return false;
            }
        }));
    }

    /// <summary>
    /// 后台接收循环，持续从 WebSocket 读取消息并通过事件分发到前端。
    /// 连接关闭或出错时退出循环并清理连接。
    /// </summary>
    /// <param name="connectionId">连接 ID。</param>
    /// <param name="ws">WebSocket 客户端实例。</param>
    private static async Task ReceiveLoopAsync(string connectionId, ClientWebSocket ws)
    {
        var buffer = new byte[ReceiveBufferSize];

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                // 将收到的消息通过事件分发到前端。
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var data = JsonSerializer.Serialize(new
                {
                    id = connectionId,
                    message = message,
                    messageType = result.MessageType.ToString().ToLowerInvariant()
                });

                Application.Get()?.Events.Emit("websocket:message", data, null);
            }
        }
        catch
        {
            // 接收循环中的异常不应中断应用
        }
        finally
        {
            _connections.TryRemove(connectionId, out _);
            Application.Get()?.Events.Emit("websocket:closed", connectionId, null);
        }
    }
}
