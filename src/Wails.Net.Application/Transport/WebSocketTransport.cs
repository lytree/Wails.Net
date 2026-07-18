using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Transport;

/// <summary>
/// 基于 WebSocket 的双向传输层实现。
/// 对应 Wails v3 Go 版本中的 WebSocket IPC 传输。
/// 使用 HttpListener 接收 WebSocket 连接，通过 MessageProcessor 处理前端消息，
/// 并支持多客户端连接（每个窗口一个 WebSocket）。
/// </summary>
public class WebSocketTransport : ITransport, IWailsEventListener, IAssetServerTransport
{
    /// <summary>
    /// 默认监听端口起始值，若被占用则自动递增。
    /// </summary>
    public const int DefaultPortStart = 34116;

    /// <summary>
    /// WebSocket 端点路径。
    /// </summary>
    public const string WebSocketPath = "/wails/ws";

    /// <summary>
    /// HTTP 监听器。
    /// </summary>
    private HttpListener? _listener;

    /// <summary>
    /// 当前监听端口。
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    /// 绑定的基地址。
    /// </summary>
    public string BaseUrl => $"http://localhost:{Port}";

    /// <summary>
    /// WebSocket 端点完整 URL（P0-D：Server 模式事件 API 完善）。
    /// 由 <see cref="BaseUrl"/> 与 <see cref="WebSocketPath"/> 拼接而成，
    /// 供 <see cref="Application.GenerateRuntimeJs"/> 注入到 <see cref="RuntimeOptions.WebSocketUrl"/>，
    /// 使 Server 模式前端 <c>ServerRuntime</c> 知晓连接地址。
    /// </summary>
    public string WebSocketUrl => $"ws://localhost:{Port}{WebSocketPath}";

    /// <summary>
    /// 消息处理器实例。
    /// </summary>
    private readonly MessageProcessor _processor;

    /// <summary>
    /// 事件广播器实例。
    /// </summary>
    private readonly WebSocketBroadcaster _broadcaster;

    /// <summary>
    /// 取消令牌源。
    /// </summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 监听任务。
    /// </summary>
    private Task? _listenTask;

    /// <summary>
    /// 绑定的资源服务器实例（可为 null）。
    /// </summary>
    private Wails.Net.AssetServer.AssetServer? _assetServer;

    /// <summary>
    /// 已连接的 WebSocket 客户端任务集合：客户端 ID → 接收循环任务。
    /// </summary>
    private readonly ConcurrentDictionary<string, Task> _clientTasks = new();

    /// <summary>
    /// 是否已启动。
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// 获取或设置 CORS 配置选项。
    /// 设置后替换硬编码的 <c>Access-Control-Allow-Origin: *</c>，改用白名单回显。
    /// 对应主题 C：CORS 配置化。
    /// </summary>
    public CorsOptions? CorsOptions { get; set; }

    /// <summary>
    /// 获取或设置 IPC 来源校验器。
    /// 设置后在 WebSocket 升级请求时校验 Origin 是否可信，拒绝非白名单来源的连接。
    /// 对应主题 C：IpcOriginValidator 接入传输层。
    /// </summary>
    public IpcOriginValidator? OriginValidator { get; set; }

    /// <summary>
    /// 使用指定的消息处理器和事件广播器构造 WebSocketTransport 实例。
    /// </summary>
    /// <param name="processor">消息处理器。</param>
    /// <param name="broadcaster">事件广播器。</param>
    public WebSocketTransport(MessageProcessor processor, WebSocketBroadcaster broadcaster)
    {
        _processor = processor;
        _broadcaster = broadcaster;
    }

    /// <summary>
    /// 返回前端 JS 客户端代码（占位实现，实际由 RuntimeGenerator 生成）。
    /// </summary>
    /// <returns>JS 客户端代码字符串。</returns>
    public string JSClient()
    {
        return $"// Wails.Net WebSocket Transport Client, endpoint: ws://localhost:{Port}{WebSocketPath}";
    }

    /// <summary>
    /// 启动 WebSocket 传输服务器。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动操作的异步任务。</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            return;
        }

        _listener = new HttpListener();

        // 查找可用端口
        Port = FindAvailablePort(DefaultPortStart);
        _listener.Prefixes.Add($"{BaseUrl}/");

        _listener.Start();
        IsRunning = true;

        // 启动消息处理器
        _processor.Start();

        // 启动后台监听循环
        _listenTask = ListenLoopAsync(_cts.Token);

        await Task.CompletedTask;
    }

    /// <summary>
    /// 停止 WebSocket 传输服务器。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示停止操作的异步任务。</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            return;
        }

        _cts.Cancel();
        _listener?.Stop();

        if (_listenTask is not null)
        {
            await _listenTask;
            _listenTask = null;
        }

        // 等待所有客户端任务完成
        await Task.WhenAll(_clientTasks.Values);
        _clientTasks.Clear();

        await _processor.StopAsync();
        await _broadcaster.StopAsync();

        IsRunning = false;
    }

    /// <summary>
    /// 处理前端传入的事件通知（实现 IWailsEventListener）。
    /// 将事件广播到所有连接的 WebSocket 客户端。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据。</param>
    public void NotifyEvent(string eventName, object? data)
    {
        _broadcaster.BroadcastEvent(eventName, data);
    }

    /// <summary>
    /// 将资源服务器绑定到当前传输层（实现 IAssetServerTransport）。
    /// 传输层在收到非 WebSocket 端点的 HTTP 请求时，将请求转发给 AssetServer 处理。
    /// </summary>
    /// <param name="assetServer">Wails 内部资源服务器实例。</param>
    public void ServeAssets(Wails.Net.AssetServer.AssetServer assetServer)
    {
        _assetServer = assetServer;
    }

    /// <summary>
    /// HTTP 请求监听循环。
    /// 根据请求路径决定升级为 WebSocket 连接还是转发给资源服务器。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示监听操作的异步任务。</returns>
    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener?.IsListening == true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                break; // 监听器已停止
            }

            // 并发处理请求
            _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
        }
    }

    /// <summary>
    /// 处理单个 HTTP 请求。
    /// 若为 WebSocket 升级请求则建立 WebSocket 连接，否则转发给资源服务器。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        // 添加 CORS 响应头（使用 CorsOptions 若已设置，否则回退到默认 *）
        ApplyCorsHeaders(request, response);

        // 处理 OPTIONS 预检请求
        if (string.Equals(request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 204;
            response.Close();
            return;
        }

        var path = request.Url?.AbsolutePath ?? "/";

        // WebSocket 升级请求
        if (string.Equals(request.Headers["Upgrade"], "websocket", StringComparison.OrdinalIgnoreCase))
        {
            // IPC 来源校验：若设置了 OriginValidator 且校验失败则拒绝连接。
            // 对应主题 C：IpcOriginValidator 接入传输层。
            if (OriginValidator is not null && !OriginValidator.Validate(request.Headers["Origin"]))
            {
                response.StatusCode = 403;
                response.Close();
                return;
            }

            await HandleWebSocketAsync(context, cancellationToken);
            return;
        }

        // 资源请求：转发给 AssetServer
        if (_assetServer is not null)
        {
            await _assetServer.ServeHttpAsync(context, cancellationToken);
            return;
        }

        // 无资源服务器时返回 404
        response.StatusCode = 404;
        response.ContentType = "text/plain; charset=utf-8";
        var body = Encoding.UTF8.GetBytes($"404 Not Found: {path}");
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body, cancellationToken);
        response.Close();
    }

    /// <summary>
    /// 处理 WebSocket 连接：接收客户端消息并处理。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    private async Task HandleWebSocketAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        HttpListenerWebSocketContext? wsContext = null;
        try
        {
            wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
        }
        catch (WebSocketException)
        {
            // WebSocket 升级失败，忽略
            return;
        }

        var webSocket = wsContext.WebSocket;
        var clientId = _broadcaster.AddClient(webSocket);

        try
        {
            // 创建绑定到当前 cancellationToken 的接收循环任务
            var receiveTask = ReceiveLoopAsync(clientId, webSocket, cancellationToken);
            _clientTasks[clientId] = receiveTask;

            await receiveTask;
        }
        finally
        {
            _broadcaster.RemoveClient(clientId);
            _clientTasks.TryRemove(clientId, out _);
            webSocket.Dispose();
        }
    }

    /// <summary>
    /// WebSocket 接收循环：持续接收客户端消息并处理。
    /// </summary>
    /// <param name="clientId">客户端 ID。</param>
    /// <param name="webSocket">WebSocket 连接。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示接收循环的异步任务。</returns>
    private async Task ReceiveLoopAsync(string clientId, WebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var messageBuilder = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested && webSocket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);
            }
            catch (WebSocketException)
            {
                break; // 客户端异常断开
            }
            catch (OperationCanceledException)
            {
                break; // 取消
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            // 累积消息片段
            messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                var messageText = messageBuilder.ToString();
                messageBuilder.Clear();

                await ProcessWebSocketMessageAsync(clientId, messageText, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 处理 WebSocket 接收到的消息。
    /// </summary>
    /// <param name="clientId">发送消息的客户端 ID。</param>
    /// <param name="messageText">消息文本（JSON）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    private async Task ProcessWebSocketMessageAsync(string clientId, string messageText, CancellationToken cancellationToken)
    {
        var message = _processor.ParseMessage(messageText);
        if (message is null)
        {
            // 发送错误响应
            var errorJson = JsonSerializer.Serialize(new
            {
                type = "error",
                error = "无法解析消息"
            }, JsonOptions.DefaultSerializerOptions);
            await _broadcaster.SendToClientAsync(clientId, errorJson);
            return;
        }

        var result = await _processor.ProcessAsync(message);

        if (result is not null)
        {
            var json = JsonSerializer.Serialize(result, JsonOptions.DefaultSerializerOptions);
            await _broadcaster.SendToClientAsync(clientId, json);
        }
    }

    /// <summary>
    /// 应用 CORS 响应头。
    /// 若设置了 <see cref="CorsOptions"/>，使用白名单回显 Origin（仅允许的 Origin 返回）；
    /// 否则回退到默认的通配符 <c>*</c>（向后兼容）。
    /// 对应主题 C：CORS 配置化，替代硬编码的 <c>Access-Control-Allow-Origin: *</c>。
    /// </summary>
    /// <param name="request">HTTP 请求对象，用于读取 Origin 头。</param>
    /// <param name="response">HTTP 响应对象。</param>
    private void ApplyCorsHeaders(HttpListenerRequest request, HttpListenerResponse response)
    {
        var origin = request.Headers["Origin"];

        if (CorsOptions is { Enabled: true } cors)
        {
            var allowedOrigin = cors.ResolveAllowedOrigin(origin);
            if (allowedOrigin is not null)
            {
                response.Headers["Access-Control-Allow-Origin"] = allowedOrigin;
                response.Headers["Access-Control-Allow-Methods"] = cors.AllowedMethods;
                response.Headers["Access-Control-Allow-Headers"] = cors.AllowedHeaders;
                if (cors.AllowCredentials)
                {
                    response.Headers["Access-Control-Allow-Credentials"] = "true";
                }
                response.Headers["Access-Control-Max-Age"] = cors.MaxAgeSeconds.ToString();
            }
            return;
        }

        // 回退：默认通配符（向后兼容）
        response.Headers["Access-Control-Allow-Origin"] = "*";
    }

    /// <summary>
    /// 从指定起始端口查找可用端口。
    /// </summary>
    /// <param name="startPort">起始端口。</param>
    /// <returns>可用的端口号。</returns>
    private static int FindAvailablePort(int startPort)
    {
        for (var port = startPort; port < startPort + 1000; port++)
        {
            if (!IsPortInUse(port))
            {
                return port;
            }
        }

        return startPort; // 回退
    }

    /// <summary>
    /// 检查指定端口是否被占用。
    /// </summary>
    /// <param name="port">端口号。</param>
    /// <returns>如果端口被占用则返回 true。</returns>
    private static bool IsPortInUse(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            client.Connect("localhost", port);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
