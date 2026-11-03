using System.Buffers;
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
/// <para>
/// <b>生产装配决策（OPT-CLEANUP-001 / OPT-IPC-003，2026-08-15）</b>：
/// 当前生产路径未装配本类——桌面模式的消息路径由平台 WebView 拦截承载
/// （Windows <c>OnWebResourceRequested</c> / Linux <c>wails://</c> scheme），
/// Server 模式走 <see cref="WebSocketTransport"/>。本类保留作为独立 HTTP 传输组件
/// （HTTP POST /wails/message 端点 + 分块上传 + token 校验，已有测试覆盖）。
/// 是否接入统一 IPC fallback 链由 OPT-IPC-003 决策，落地时需同步扩展前端 runtime
/// flags（注入 HTTP 端点 URL），否则装配后前端不可达（见 optimization-roadmap.md §5.2）。
/// </para>
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
    /// 普通 IPC 消息请求体的最大字节数（64 MiB）。
    /// 对应 Wails v3 Go 版本 transport_http.go 中的
    /// <c>maxRuntimeBodyBytes = maxAssembledBytes = 64 * 1024 * 1024</c>。
    /// 超过此上限的 POST body 会在读取时被拒绝，并返回 HTTP 413。
    /// </summary>
    public const long MaxRuntimeBodyBytes = 64 * 1024 * 1024;

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
    /// 获取或设置 Server 模式请求安全闸门（P0 安全加固，对齐 Wails v3 `v3.0.0`）。
    /// 非空时启用以下策略：
    /// <list type="bullet">
    /// <item>全部请求校验 <c>Host</c> 头（loopback 绑定的 DNS rebinding 防护）；</item>
    /// <item>消息端点按「同源 / <see cref="IpcOptions.AllowedOrigins"/> 白名单」校验 <c>Origin</c>，
    /// 并补 <c>Sec-Fetch-Site</c> 判定。</item>
    /// </list>
    /// 消息端点的「仅 POST、其它方法 405（<c>Allow: POST</c>）」属于端点自身语义，始终生效。
    /// 由 <c>DefaultIpcTransportStrategy</c> / <c>UseServerModeTransport</c> 在 Server 模式装配；
    /// 为 null 时不启用 Host/Origin 闸门（保持既有行为，裸构造传输层与既有单元测试不受影响）。
    /// </summary>
    public IpcRequestGuard? RequestGuard { get; set; }

    /// <summary>
    /// 获取或设置 IPC handshake token（OPT-IPC-002）。
    /// 由框架装配时注入进程级随机 token（<see cref="Wails.Net.Application.Security.IpcHandshakeToken.Current"/>），
    /// 前端通过 <c>x-wails-token</c> 请求头回传。当 <see cref="RequireHandshakeToken"/> 为 true
    /// 且本值非空时，缺失或不匹配 token 的 IPC 请求被拒绝（403）。
    /// </summary>
    public string? HandshakeToken { get; set; }

    /// <summary>
    /// 获取或设置是否要求 handshake token 校验（默认 true）。
    /// 裸构造传输层且 <see cref="HandshakeToken"/> 为 null 时不强制校验，保持低层 API 与测试兼容；
    /// 框架装配路径会注入 token，从而默认启用校验。
    /// </summary>
    public bool RequireHandshakeToken { get; set; } = true;

    /// <summary>
    /// handshake token 请求头名称。
    /// 与前端 <c>@wails-net/runtime</c> 的 <c>x-wails-token</c> 头保持一致。
    /// </summary>
    public const string HandshakeTokenHeader = "x-wails-token";

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
    /// 返回前端 JS 客户端代码（占位实现；实际运行时 JS 已迁往 npm 包 <c>@wails-net/runtime</c>，
    /// 见 <c>packages/wails-net-runtime/</c>，由前端 <c>import { wails } from "@wails-net/runtime"</c> 加载；
    /// 本方法仅保留用于诊断输出）。
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
    /// <param name="targetWindowId">定向目标窗口 ID（OPT-EVENT-002）；null 表示广播。</param>
    public void NotifyEvent(string eventName, object? data, uint? senderWindowId = null, uint? targetWindowId = null)
    {
        _broadcaster.BroadcastEvent(eventName, data, senderWindowId, targetWindowId);
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
            // P0 安全加固：loopback 绑定时拒绝非 localhost / IP 字面量 / 绑定主机 / 白名单的 Host 头，
            // 防止 DNS rebinding 页面通过同源判定（对齐 Wails v3 v3.0.0 host_guard_server.go）。
            if (RequestGuard is { } hostGuard && !hostGuard.IsHostAllowed(request))
            {
                await WriteTextResponseAsync(
                    response, 403, "403 Forbidden: Host not allowed", cancellationToken);
                return;
            }

            // 添加 CORS 响应头（使用 CorsOptions 若已设置，否则回退到默认 *）
            ApplyCorsHeaders(request, response);

            // 处理 OPTIONS 预检请求
            if (string.Equals(request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = 204;
                return;
            }

            var path = request.Url?.AbsolutePath ?? "/";
            var isMessageEndpoint = path.EndsWith(MessageEndpoint, StringComparison.OrdinalIgnoreCase);

            // P0 安全加固：消息端点只接受 POST，其它方法返回 405 + Allow: POST。
            // 对齐 Wails v3 v3.0.0 transport_http_server.go 的 checkRuntimeRequest。
            if (isMessageEndpoint &&
                !string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextResponseAsync(
                    response, 405, "405 Method Not Allowed", cancellationToken, allow: "POST");
                return;
            }

            // 消息端点：处理 IPC 消息
            if (isMessageEndpoint)
            {
                // IPC 来源校验：
                // - 已装配 Server 模式闸门时按「同源 / AllowedOrigins 白名单」严格校验；
                // - 否则保持既有 IpcOriginValidator 行为（本地源恒允许）。
                var originAllowed = RequestGuard is { } requestGuard
                    ? requestGuard.IsRuntimeRequestAllowed(request)
                    : OriginValidator is null || OriginValidator.Validate(request.Headers["Origin"]);

                if (!originAllowed)
                {
                    await WriteTextResponseAsync(
                        response, 403, "403 Forbidden: Origin not allowed", cancellationToken);
                    return;
                }

                // IPC handshake token 校验（OPT-IPC-002）：
                // 当框架注入了 token（HandshakeToken 非空）且 RequireHandshakeToken=true 时，
                // 请求必须携带匹配的 x-wails-token 头，否则拒绝。token 不写入日志。
                if (RequireHandshakeToken && !string.IsNullOrEmpty(HandshakeToken) &&
                    !string.Equals(request.Headers[HandshakeTokenHeader], HandshakeToken, StringComparison.Ordinal))
                {
                    response.StatusCode = 403;
                    response.ContentType = "text/plain; charset=utf-8";
                    var tokenForbidden = System.Text.Encoding.UTF8.GetBytes("403 Forbidden: Invalid or missing handshake token");
                    response.ContentLength64 = tokenForbidden.Length;
                    await response.OutputStream.WriteAsync(tokenForbidden, cancellationToken);
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
    /// <para>
    /// <b>P1-8：请求体上限（64 MiB → HTTP 413）</b>。
    /// 对应 Wails v3 Go 版本 transport_http.go <c>handleRuntimeRequest</c> 中
    /// <c>r.Body = http.MaxBytesReader(rw, r.Body, int64(maxRuntimeBodyBytes))</c>：
    /// 先通过 <see cref="HttpListenerRequest.ContentLength64"/> 预判以拒绝已知超大请求，
    /// 再边读边计数覆盖 Content-Length 缺失（如 chunked）的场景，避免先完整缓冲再判断。
    /// 超限时返回 413，响应体文本与 Go 一致。
    /// </para>
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    private async Task HandleMessageAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;

        // 预判：Content-Length 已知且超过上限时直接拒绝，不读取 body。
        if (request.ContentLength64 > MaxRuntimeBodyBytes)
        {
            await WriteRequestEntityTooLargeAsync(context.Response, MaxRuntimeBodyBytes, cancellationToken);
            return;
        }

        // 边读边计数：覆盖 Content-Length 缺失（chunked）等场景。
        var bodyBytes = await ReadBodyWithLimitAsync(request.InputStream, MaxRuntimeBodyBytes, cancellationToken);
        if (bodyBytes is null)
        {
            await WriteRequestEntityTooLargeAsync(context.Response, MaxRuntimeBodyBytes, cancellationToken);
            return;
        }

        // 保持与既有 StreamReader 一致的 UTF-8 / BOM 解码语义（未超限请求行为不变）。
        using var reader = new StreamReader(new MemoryStream(bodyBytes, writable: false));
        var body = await reader.ReadToEndAsync(cancellationToken);
        var reEncodedBytes = System.Text.Encoding.UTF8.GetBytes(body);
        await HandleMessageFromBytesAsync(context, reEncodedBytes, cancellationToken);
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

        // 3. 读取 chunk body（限制单 chunk ≤ 1MB）。
        // 对应 Wails v3 Go 版本 transport_http.go handleChunkedRequest 中
        // r.Body = http.MaxBytesReader(rw, r.Body, int64(maxChunkBodyBytes))：
        // 先按 Content-Length 预判，再边读边计数，避免先完整缓冲再判断。
        if (request.ContentLength64 > ChunkStore.MaxChunkBodyBytes)
        {
            await WriteChunkedErrorAsync(response, 422,
                $"chunk body 超过最大限制 {ChunkStore.MaxChunkBodyBytes} 字节", cancellationToken);
            return;
        }

        var chunk = await ReadBodyWithLimitAsync(request.InputStream, ChunkStore.MaxChunkBodyBytes, cancellationToken);
        if (chunk is null)
        {
            await WriteChunkedErrorAsync(response, 422,
                $"chunk body 超过最大限制 {ChunkStore.MaxChunkBodyBytes} 字节", cancellationToken);
            return;
        }

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
    /// 从请求流读取全部字节，读入字节数超过 <paramref name="maxBytes"/> 时返回 <c>null</c>。
    /// 边读边计数，避免先完整缓冲再判断，对应 Wails v3 Go 版本
    /// transport_http.go 中 <c>http.MaxBytesReader</c> 的限流读取语义。
    /// <para>
    /// 声明为 <c>internal</c> 以便测试在不构造超大实体的情况下验证边界行为。
    /// </para>
    /// </summary>
    /// <param name="stream">要读取的请求流。</param>
    /// <param name="maxBytes">允许的最大字节数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>读取到的字节数组；超过上限时返回 <c>null</c>。</returns>
    internal static async Task<byte[]?> ReadBodyWithLimitAsync(
        Stream stream,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    return null;
                }

                ms.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// 写入请求体超限响应（HTTP 413 Request Entity Too Large）。
    /// 响应体文本对齐 Wails v3 Go 版本 transport_http.go <c>handleRuntimeRequest</c> 中
    /// <c>http.Error(rw, "runtime request body exceeds "+N+" MiB", http.StatusRequestEntityTooLarge)</c>。
    /// </summary>
    /// <param name="response">HTTP 响应对象。</param>
    /// <param name="limitBytes">触发的上限字节数，用于生成 <c>N MiB</c> 文案。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示写入操作的异步任务。</returns>
    private static async Task WriteRequestEntityTooLargeAsync(
        HttpListenerResponse response,
        long limitBytes,
        CancellationToken cancellationToken)
    {
        response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
        response.ContentType = "text/plain; charset=utf-8";
        var message = $"runtime request body exceeds {limitBytes / (1024 * 1024)} MiB";
        var bytes = System.Text.Encoding.UTF8.GetBytes(message);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    /// <summary>
    /// 写入纯文本错误响应（供 P0 安全加固的 403/405 分支使用）。
    /// </summary>
    /// <param name="response">HTTP 响应。</param>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="message">响应正文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="allow">可选的 <c>Allow</c> 响应头（405 时使用）。</param>
    private static async Task WriteTextResponseAsync(
        HttpListenerResponse response,
        int statusCode,
        string message,
        CancellationToken cancellationToken,
        string? allow = null)
    {
        if (allow is not null)
        {
            response.Headers["Allow"] = allow;
        }

        response.StatusCode = statusCode;
        response.ContentType = "text/plain; charset=utf-8";
        var body = System.Text.Encoding.UTF8.GetBytes(message);
        response.ContentLength64 = body.Length;
        await response.OutputStream.WriteAsync(body, cancellationToken);
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
