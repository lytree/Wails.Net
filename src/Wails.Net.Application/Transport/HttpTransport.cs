using System.Net;
using System.Text.Json;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Security;

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
    /// 分块上传会话存储。
    /// 对应 Wails v3 Go 版本 transport_http.go 中 <c>HTTPTransport.chunkStore</c>。
    /// 在 <see cref="StartAsync"/> 中创建，<see cref="StopAsync"/> 中释放。
    /// 仅对 POST 请求生效；若分块头（<c>x-wails-chunk-id</c>）存在则进入分块处理路径。
    /// </summary>
    private ChunkStore? _chunkStore;

    /// <summary>
    /// 分块上传相关 HTTP 头名称常量。
    /// 与 Wails v3 Go 版本 transport_http.go 中的头名称保持一致。
    /// </summary>
    public static class ChunkHeaders
    {
        /// <summary>分块会话 ID（前端 nanoid 生成）。</summary>
        public const string ChunkId = "x-wails-chunk-id";

        /// <summary>当前 chunk 索引（0-based）。</summary>
        public const string ChunkIndex = "x-wails-chunk-index";

        /// <summary>本会话总 chunk 数量。</summary>
        public const string ChunkTotal = "x-wails-chunk-total";
    }

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
    /// 获取或设置 CORS 配置选项。
    /// 设置后替换硬编码的 <c>Access-Control-Allow-Origin: *</c>，改用白名单回显。
    /// 对应主题 C：CORS 配置化。
    /// </summary>
    public CorsOptions? CorsOptions { get; set; }

    /// <summary>
    /// 获取或设置 IPC 来源校验器。
    /// 设置后在消息端点校验请求 Origin 是否可信，拒绝非白名单来源的 IPC 调用。
    /// 对应主题 C：IpcOriginValidator 接入传输层。
    /// </summary>
    public IpcOriginValidator? OriginValidator { get; set; }

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

        // 启动分块上传会话存储（P0-C1）。
        // 即使本请求不携带 chunk 头，也只是空字典查找开销，可安全常驻。
        _chunkStore = new ChunkStore();

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

        // 释放分块上传会话存储与后台清理定时器（P0-C1）。
        _chunkStore?.Dispose();
        _chunkStore = null;

        IsRunning = false;
    }

    /// <summary>
    /// 处理前端传入的事件通知（实现 IWailsEventListener）。
    /// 将事件广播到所有连接的 WebSocket 客户端。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据。</param>
    /// <param name="senderWindowId">事件来源窗口 ID，可为 null（应用级事件）。</param>
    public void NotifyEvent(string eventName, object? data, uint? senderWindowId = null)
    {
        _broadcaster.BroadcastEvent(eventName, data, senderWindowId);
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
            // 添加 CORS 响应头（使用 CorsOptions 若已设置，否则回退到默认 *）
            ApplyCorsHeaders(request, response);

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
                // IPC 来源校验：若设置了 OriginValidator 且校验失败则拒绝请求。
                // 对应主题 C：IpcOriginValidator 接入传输层。
                if (OriginValidator is not null && !OriginValidator.Validate(request.Headers["Origin"]))
                {
                    response.StatusCode = 403;
                    response.ContentType = "text/plain; charset=utf-8";
                    var forbiddenBody = System.Text.Encoding.UTF8.GetBytes("403 Forbidden: Origin not allowed");
                    response.ContentLength64 = forbiddenBody.Length;
                    await response.OutputStream.WriteAsync(forbiddenBody, cancellationToken);
                    return;
                }

                // P0-C1：分块上传检测。
                // 若请求携带 x-wails-chunk-id 头，进入分块处理路径。
                // 对应 Wails v3 Go 版本 transport_http.go handleRuntimeRequest 中
                // if chunkID := r.Header.Get(chunkIDHeader); chunkID != "" { t.handleChunkedRequest(...) }
                var chunkId = request.Headers[ChunkHeaders.ChunkId];
                if (!string.IsNullOrEmpty(chunkId) && _chunkStore is not null)
                {
                    await HandleChunkedRequestAsync(context, chunkId, cancellationToken);
                    return;
                }

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
    /// 从请求流读取完整 body，再委托给 <see cref="HandleMessageFromBytesAsync"/> 处理。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    private async Task HandleMessageAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        using var reader = new StreamReader(request.InputStream);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var bodyBytes = System.Text.Encoding.UTF8.GetBytes(body);
        await HandleMessageFromBytesAsync(context, bodyBytes, cancellationToken);
    }

    /// <summary>
    /// 处理已组装完成的 IPC 消息字节数组。
    /// 此方法是分块路径与普通路径的共用出口：将字节数组反序列化为 <see cref="Message"/>，
    /// 调用 <see cref="MessageProcessor.ProcessAsync"/>，并将响应写回 HTTP 响应。
    /// 对应 Wails v3 Go 版本 transport_http.go 中的 <c>processBody</c> 函数。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="bodyBytes">已组装完成的 JSON 消息字节数组。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    private async Task HandleMessageFromBytesAsync(
        HttpListenerContext context,
        byte[] bodyBytes,
        CancellationToken cancellationToken)
    {
        var response = context.Response;

        var body = System.Text.Encoding.UTF8.GetString(bodyBytes);
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
    /// 处理分块上传请求（P0-C1）。
    /// 对应 Wails v3 Go 版本 transport_http.go 中的 <c>handleChunkedRequest</c>。
    /// <para>
    /// 协议：前端将大参数（> 512KB）切分为多个 chunk，串行 POST 同一端点，
    /// 通过 <c>x-wails-chunk-id</c>、<c>x-wails-chunk-index</c>、<c>x-wails-chunk-total</c>
    /// 三个 HTTP 头标识分块会话。前 n-1 个 chunk 响应为 200 OK 空 body，
    /// 最后一个 chunk 的响应携带 RPC 结果。
    /// </para>
    /// <para>
    /// 限制：单 chunk body ≤ 1MB、总 chunk 数 ≤ 1024、组装后总大小 ≤ 64MB、会话 TTL 30s。
    /// </para>
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="chunkId">分块会话唯一标识符（来自 <c>x-wails-chunk-id</c> 头）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    private async Task HandleChunkedRequestAsync(
        HttpListenerContext context,
        string chunkId,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        // 1. 校验并解析 chunk-total 头
        if (!int.TryParse(request.Headers[ChunkHeaders.ChunkTotal], out var total) ||
            total <= 0 || total > ChunkStore.MaxChunkTotal)
        {
            await WriteChunkedErrorAsync(response, 422,
                $"无效的 chunk-total（必须 1-{ChunkStore.MaxChunkTotal}）", cancellationToken);
            return;
        }

        // 2. 校验并解析 chunk-index 头
        if (!int.TryParse(request.Headers[ChunkHeaders.ChunkIndex], out var index) ||
            index < 0 || index >= total)
        {
            await WriteChunkedErrorAsync(response, 422,
                $"无效的 chunk-index（必须 0-{total - 1}）", cancellationToken);
            return;
        }

        // 3. 读取 chunk body（限制单 chunk ≤ 1MB）
        using var ms = new MemoryStream();
        await request.InputStream.CopyToAsync(ms, 81920, cancellationToken);
        if (ms.Length > ChunkStore.MaxChunkBodyBytes)
        {
            await WriteChunkedErrorAsync(response, 422,
                $"chunk body 超过最大限制 {ChunkStore.MaxChunkBodyBytes} 字节", cancellationToken);
            return;
        }
        var chunk = ms.ToArray();

        // 4. 原子获取或创建会话，校验 total 一致性
        var pc = _chunkStore!.GetOrAdd(chunkId, total);
        if (pc.Total != total)
        {
            // 同一 chunkID 但 total 不一致，丢弃整个会话
            _chunkStore.TryRemove(chunkId, out _);
            await WriteChunkedErrorAsync(response, 422,
                $"chunk-total 不一致：预期 {pc.Total}，实际 {total}", cancellationToken);
            return;
        }

        // 5. 添加 chunk 到会话
        var received = pc.AddChunk(index, chunk);

        // 6. 检查累计大小是否超过 64MB
        if (pc.Size > ChunkStore.MaxAssembledBytes)
        {
            _chunkStore.TryRemove(chunkId, out _);
            await WriteChunkedErrorAsync(response, 422,
                $"组装后总大小超过最大限制 {ChunkStore.MaxAssembledBytes} 字节", cancellationToken);
            return;
        }

        // 7. 若尚未接收全部 chunk，返回 200 OK 空 body
        if (received < total)
        {
            response.StatusCode = 200;
            response.ContentLength64 = 0;
            return;
        }

        // 8. 所有 chunk 到齐：组装并删除会话
        _chunkStore.TryRemove(chunkId, out _);
        byte[] assembled;
        try
        {
            assembled = pc.Assemble();
        }
        catch (InvalidOperationException ex)
        {
            await WriteChunkedErrorAsync(response, 422,
                $"组装失败：{ex.Message}", cancellationToken);
            return;
        }

        // 9. 调用普通消息处理路径（与 Wails v3 processBody 一致）
        await HandleMessageFromBytesAsync(context, assembled, cancellationToken);
    }

    /// <summary>
    /// 写入分块上传错误响应（422 Unprocessable Entity + JSON 错误体）。
    /// </summary>
    /// <param name="response">HTTP 响应对象。</param>
    /// <param name="statusCode">HTTP 状态码（通常为 422）。</param>
    /// <param name="message">错误消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示写入操作的异步任务。</returns>
    private static async Task WriteChunkedErrorAsync(
        HttpListenerResponse response,
        int statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var errorPayload = JsonSerializer.Serialize(new { error = message }, JsonOptions.DefaultSerializerOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(errorPayload);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
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
            // 使用 CorsOptions 解析允许的 Origin（白名单回显）
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
            // 若 Origin 不在白名单中，不添加 CORS 头（浏览器将阻止跨域请求）
            return;
        }

        // 回退：默认通配符（向后兼容）
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        // P0-C1：允许分块上传相关请求头（x-wails-chunk-*）。
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Range, If-None-Match, x-wails-window-id, x-wails-window-name, x-wails-chunk-id, x-wails-chunk-index, x-wails-chunk-total";
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
