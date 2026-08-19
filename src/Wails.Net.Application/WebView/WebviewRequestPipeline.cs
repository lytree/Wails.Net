using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;

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
    /// 处理静态资源请求：GET 资源 + SPA 回退。
    /// </summary>
    private async Task<WebviewResponse> HandleAssetAsync(WebviewRequest request, CancellationToken cancellationToken)
    {
        var path = NormalizeAssetPath(request.Path);
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
