using System.Net;
using System.Text.Json;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;

namespace Wails.Net.Application.Transport;

/// <summary>
/// 基于 HTTP 的传输层实现。
/// 对应 Wails v3 Go 版本中的 IPC HTTP 传输。
/// 内嵌一个轻量级 HTTP 服务器，接收前端消息并通过 MessageProcessor 处理，
/// 同时支持将资源请求转发给 AssetServer。
/// </summary>
public class HttpTransport : ITransport, IWailsEventListener, ITransportHttpHandler, IAssetServerTransport
{
    /// <summary>
    /// 默认监听端口起始值，若被占用则自动递增。
    /// </summary>
    public const int DefaultPortStart = 34115;

    /// <summary>
    /// 消息端点路径。
    /// </summary>
    public const string MessageEndpoint = "/wails/message";

    /// <summary>
    /// WebSocket 端点路径。
    /// </summary>
    public const string WebSocketEndpoint = "/wails/ws";

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
    /// 异步上下文持有当前请求的 HTTP 上下文，用于 ITransportHttpHandler 接口。
    /// </summary>
    private static readonly AsyncLocal<HttpListenerContext?> _currentContext = new();

    /// <summary>
    /// 绑定的资源服务器实例（可为 null）。
    /// </summary>
    private Wails.Net.AssetServer.AssetServer? _assetServer;

    /// <summary>
    /// 是否已启动。
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// 使用指定的消息处理器和事件广播器构造 HttpTransport 实例。
    /// </summary>
    /// <param name="processor">消息处理器。</param>
    /// <param name="broadcaster">事件广播器。</param>
    public HttpTransport(MessageProcessor processor, WebSocketBroadcaster broadcaster)
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
        return $"// Wails.Net HTTP Transport Client, endpoint: {BaseUrl}{MessageEndpoint}";
    }

    /// <summary>
    /// 启动 HTTP 传输服务器。
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
    /// 停止 HTTP 传输服务器。
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
    /// 获取当前正在处理的 HTTP 请求上下文（实现 ITransportHttpHandler）。
    /// 用于资源服务器中间件访问当前请求的头部、查询参数等。
    /// </summary>
    /// <returns>当前 HTTP 上下文；若无正在处理的请求则返回 null。</returns>
    public HttpListenerContext? GetCurrentContext()
    {
        return _currentContext.Value;
    }

    /// <summary>
    /// 将资源服务器绑定到当前传输层（实现 IAssetServerTransport）。
    /// 传输层在收到非消息端点的请求时，将请求转发给 AssetServer 处理。
    /// </summary>
    /// <param name="assetServer">Wails 内部资源服务器实例。</param>
    public void ServeAssets(Wails.Net.AssetServer.AssetServer assetServer)
    {
        _assetServer = assetServer;
    }

    /// <summary>
    /// HTTP 请求监听循环。
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
    /// 根据 URL 路径决定转发给消息处理器还是资源服务器。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        // 设置当前上下文（供 ITransportHttpHandler 使用）
        _currentContext.Value = context;

        try
        {
            // 添加 CORS 响应头
            ApplyCorsHeaders(response);

            // 处理 OPTIONS 预检请求
            if (string.Equals(request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = 204;
                return;
            }

            var path = request.Url?.AbsolutePath ?? "/";

            // 消息端点：处理 IPC 消息
            if (string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(MessageEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                await HandleMessageAsync(context, cancellationToken);
                return;
            }

            // 资源端点：转发给 AssetServer
            if (_assetServer is not null)
            {
                await _assetServer.ServeHttpAsync(context, cancellationToken);
                return;
            }

            // 无资源服务器时返回 404
            response.StatusCode = 404;
            response.ContentType = "text/plain; charset=utf-8";
            var body = System.Text.Encoding.UTF8.GetBytes($"404 Not Found: {path}");
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body, cancellationToken);
        }
        catch
        {
            response.StatusCode = 500;
        }
        finally
        {
            _currentContext.Value = null;
            response.Close();
        }
    }

    /// <summary>
    /// 处理 IPC 消息请求。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    private async Task HandleMessageAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        using var reader = new StreamReader(request.InputStream);
        var body = await reader.ReadToEndAsync(cancellationToken);

        var message = _processor.ParseMessage(body);
        if (message is null)
        {
            response.StatusCode = 400;
            return;
        }

        var result = await _processor.ProcessAsync(message);

        response.ContentType = "application/json; charset=utf-8";
        if (result is not null)
        {
            var json = JsonSerializer.Serialize(result, JsonOptions.DefaultSerializerOptions);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, cancellationToken);
        }
        else
        {
            response.StatusCode = 204; // 无内容
        }
    }

    /// <summary>
    /// 应用 CORS 响应头。
    /// </summary>
    /// <param name="response">HTTP 响应对象。</param>
    private static void ApplyCorsHeaders(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Range, If-None-Match, x-wails-window-id, x-wails-window-name";
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
