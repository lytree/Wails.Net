using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;

namespace Wails.Net.Application.Webview;

/// <summary>
/// 统一 WebView 请求管线（R2-4 融合审计）。
/// <para>
/// 取代 Windows / Linux / macOS / Android 四平台各自重复的：
/// <list type="bullet">
/// <item>POST <c>/wails/*</c> IPC 请求 → <see cref="Application.HandleMessageFromFrontend"/> 同步调用 + JSON 响应</item>
/// <item>GET 资源请求 → <see cref="Wails.Net.AssetServer.AssetServer.ServeAsync"/> 调用 + MIME 推断 + SPA 回退</item>
/// <item>错误处理（404 / 500 / 异常捕获）</item>
/// </list>
/// </para>
/// <para>
/// 设计要点：
/// <list type="bullet">
/// <item>全异步 API，内部所有 <c>await</c> 使用 <see cref="Task.ConfigureAwait(bool)"/><c>(false)</c>，
/// 避免捕获调用方同步上下文（UI 线程 / GTK MainLoop / RunLoop / Android 主线程）。</item>
/// <item>IPC 与资源由调用方注入委托，便于单平台替换（如 Android 的 JavascriptInterface 与 ShouldInterceptRequest）。</item>
/// <item>同步包装由 <see cref="WebviewPipelineSync.RunOnThreadPool"/> 提供，强制 Task.Run 避免死锁。</item>
/// </list>
/// </para>
/// <para>
/// 对应 docs/architecture/fusion-architecture-audit.md §10.3 / R2-4。
/// </para>
/// </summary>
public sealed class WebviewRequestPipeline
{
    private readonly Func<string, uint, string?, CancellationToken, Task<object?>> _ipcHandler;
    private readonly Func<string, string?, CancellationToken, Task<byte[]?>> _assetHandler;
    private readonly Func<string, string> _mimeTypeResolver;
    private readonly ILogger<WebviewRequestPipeline>? _logger;

    /// <summary>
    /// 构造统一 WebView 请求管线。
    /// </summary>
    /// <param name="ipcHandler">IPC 处理委托：接收原始请求体 + 窗口 ID + Origin，返回响应对象或 null。</param>
    /// <param name="assetHandler">资源处理委托：接收规范化路径 + 窗口名称，返回资源字节或 null。</param>
    /// <param name="mimeTypeResolver">MIME 类型解析委托；缺省时使用内置常见扩展名映射。</param>
    /// <param name="logger">可选日志记录器。</param>
    public WebviewRequestPipeline(
        Func<string, uint, string?, CancellationToken, Task<object?>> ipcHandler,
        Func<string, string?, CancellationToken, Task<byte[]?>> assetHandler,
        Func<string, string>? mimeTypeResolver = null,
        ILogger<WebviewRequestPipeline>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ipcHandler);
        ArgumentNullException.ThrowIfNull(assetHandler);

        _ipcHandler = ipcHandler;
        _assetHandler = assetHandler;
        _mimeTypeResolver = mimeTypeResolver ?? DefaultMimeTypeResolver;
        _logger = logger;
    }

    /// <summary>
    /// 统一处理入口。
    /// 根据 <see cref="WebviewRequest.IsIpcRequest"/> 自动路由到 IPC 或资源处理路径。
    /// 异常被捕获并转换为 500 响应（不抛出），便于同步包装调用方直接处理。
    /// </summary>
    /// <param name="request">平台无关的 WebView 请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>平台无关的响应。</returns>
    public async Task<WebviewResponse> HandleAsync(WebviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            // 事件投递硬化 P0-1 / Layer 3：超大事件载荷取回端点
            // （对应 Wails v3 Go 版本 application.go 中对 eventPayloadPath 前缀的特判）。
            // 必须早于 IsIpcRequest 判定——该请求为 GET，否则会落入静态资源路径并 404。
            if (request.Path.StartsWith(EventPayloadStore.PathPrefix, StringComparison.Ordinal))
            {
                return HandleEventPayload(request);
            }

            // Streams 双向字节流端点（对应 Wails v3 Go 版本 application.go 中对 streamPath 前缀的特判）。
            // 必须早于 IsIpcRequest 判定——send 端点为 POST /wails/stream/send，会被 IsIpcRequest 命中；
            // 且 poll 为 GET，否则会落入静态资源路径并 404。
            var streams = Application.Get()?.Streams;
            if (streams is not null && request.Path.StartsWith("/wails/stream/", StringComparison.Ordinal))
            {
                return await streams.HandleAsync(request, cancellationToken).ConfigureAwait(false);
            }

            if (request.IsIpcRequest)
            {
                return await HandleIpcAsync(request, cancellationToken).ConfigureAwait(false);
            }
            return await HandleAssetAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "WebviewRequestPipeline 处理失败 method={Method} path={Path}", request.Method, request.Path);
            return WebviewResponse.InternalError(ex.Message);
        }
    }

    /// <summary>
    /// 处理 IPC 请求：POST <c>/wails/*</c>。
    /// </summary>
    private async Task<WebviewResponse> HandleIpcAsync(WebviewRequest request, CancellationToken cancellationToken)
    {
        var body = request.RequestBody ?? "{}";
        var response = await _ipcHandler(body, request.WindowId, request.Origin, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return WebviewResponse.JsonEmpty();
        }
        return WebviewResponse.Json(response);
    }

    /// <summary>
    /// 处理超大事件载荷取回请求：<c>GET /wails/eventpayload/&lt;id&gt;</c>。
    /// <para>
    /// 对应 Wails v3 Go 版本 <c>App.serveEventPayload</c>：载荷为一次性（读取即删）且绑定到
    /// 派发时指定的窗口，因此陈旧 id 或跨窗口取回一律 404。仅接受 GET/HEAD。
    /// </para>
    /// </summary>
    /// <param name="request">平台无关的 WebView 请求。</param>
    /// <returns>载荷 JSON 响应，或 404 / 405。</returns>
    private WebviewResponse HandleEventPayload(WebviewRequest request)
    {
        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return WebviewResponse.MethodNotAllowed();
        }

        var id = request.Path[EventPayloadStore.PathPrefix.Length..];
        if (!EventPayloadStore.IsValidId(id))
        {
            return WebviewResponse.NotFound();
        }

        // 载荷绑定到派发时所在窗口（request.WindowId 由平台请求拦截层填充）。
        var data = Application.Get()?.EventPayloads.Take(id, request.WindowId);
        if (data is null)
        {
            return WebviewResponse.NotFound();
        }

        return WebviewResponse.Ok("application/json", data);
    }

    /// <summary>
    /// 处理静态资源请求：GET 资源 + SPA 回退。
    /// </summary>
    private async Task<WebviewResponse> HandleAssetAsync(WebviewRequest request, CancellationToken cancellationToken)
    {
        var path = NormalizeAssetPath(request.Path);

        // P2-12：dev 模式资产反向代理 —— 配置了 DevServerUrl 时优先把资源请求转发到前端
        // dev server（对应 Wails v3 internal/assetserver/build_dev.go 的 dev 代理）。
        // 仅对 GET 生效；转发结束后成功即返回上游响应，失败（重试耗尽）返回 null 时回退本地资源。
        var devProxy = Application.Get()?.DevAssetProxy;
        if (devProxy is not null && string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            var proxied = await devProxy.TryGetAsync(request.Path, request.Query, cancellationToken).ConfigureAwait(false);
            if (proxied is not null)
            {
                return proxied;
            }
        }

        var content = await _assetHandler(path, request.WindowName, cancellationToken).ConfigureAwait(false);
        if (content is not null && content.Length > 0)
        {
            var mimeType = _mimeTypeResolver(path);
            return WebviewResponse.Ok(mimeType, content);
        }

        // SPA 路由回退：当资源不存在时，回退到 index.html。
        // 适用于 Vue/React/Angular 等前端框架的客户端路由。
        if (ShouldFallbackToIndex(path))
        {
            var fallback = await _assetHandler("index.html", request.WindowName, cancellationToken).ConfigureAwait(false);
            if (fallback is not null && fallback.Length > 0)
            {
                return WebviewResponse.Ok("text/html", fallback);
            }
        }

        return WebviewResponse.NotFound();
    }

    private static string NormalizeAssetPath(string rawPath)
    {
        var p = rawPath.TrimStart('/');
        return string.IsNullOrEmpty(p) ? "index.html" : p;
    }

    private static bool ShouldFallbackToIndex(string path)
    {
        if (string.IsNullOrEmpty(path) ||
            path.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // /api/ 路径不触发 SPA 回退（应由前端 fetch 直接处理 404）。
        if (path.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !Path.HasExtension(path);
    }

    private static string DefaultMimeTypeResolver(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" or ".mjs" => "application/javascript",
            ".json" or ".map" => "application/json",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".eot" => "application/vnd.ms-fontobject",
            ".txt" => "text/plain",
            _ => "application/octet-stream",
        };
    }
}
