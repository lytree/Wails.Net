using Android.Webkit;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Android;

/// <summary>
/// Android WebView 资源拦截器，继承 <see cref="WebViewClient"/>。
/// 对应 ADR-0002 §4：通过重写 <c>ShouldInterceptRequest</c> 拦截 WebView 资源请求，
/// 将 <c>http://wails.localhost/</c> scheme 下的请求转发到 <see cref="Wails.Net.AssetServer.AssetServer"/>。
/// </summary>
/// <remarks>
/// 拦截规则：
/// <list type="bullet">
///   <item>仅当请求 URL 的 host 为 <c>wails.localhost</c> 时拦截，其他 host（外部 CDN、file://）放行由 WebView 默认处理。</item>
///   <item>路径以 <c>/api/</c> 开头的请求不触发 SPA 回退（IPC 请求应直接返回 404 由前端处理）。</item>
///   <item>未配置 AssetServer 时返回 null，使用 WebView 默认加载行为。</item>
/// </list>
/// </remarks>
public sealed class WailsWebViewClient : WebViewClient
{
    /// <summary>
    /// 自定义 scheme 的 host 名，对应 <c>http://wails.localhost/</c>。
    /// </summary>
    private const string WailsHost = "wails.localhost";

    /// <summary>
    /// 静态资源服务器引用，可能为 null（未配置 AssetServer 时）。
    /// </summary>
    private readonly Wails.Net.AssetServer.AssetServer? _assetServer;

    /// <summary>
    /// 关联的窗口名称，用于 per-window CSP 注入（P0-4，对应 Tauri v2 per-window CSP）。
    /// 可为 null（未指定窗口名时，不注入窗口级 CSP，仅回退到全局）。
    /// </summary>
    private readonly string? _windowName;

    /// <summary>
    /// 构造 WailsWebViewClient 实例。
    /// </summary>
    /// <param name="assetServer">静态资源服务器引用，可为 null。</param>
    /// <param name="windowName">关联的窗口名称，用于 per-window CSP 注入；可为 null。</param>
    public WailsWebViewClient(Wails.Net.AssetServer.AssetServer? assetServer, string? windowName = null)
    {
        _assetServer = assetServer;
        _windowName = windowName;
    }

    /// <summary>
    /// 拦截 WebView 资源请求。
    /// 仅当配置了 AssetServer 且请求指向 <c>wails.localhost</c> 时拦截，其他请求放行。
    /// </summary>
    /// <param name="view">发起请求的 WebView 实例。</param>
    /// <param name="request">Web 资源请求信息。</param>
    /// <returns>自定义的 <see cref="WebResourceResponse"/>，或 null 使用默认加载。</returns>
    public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
    {
        if (_assetServer is null || request is null)
        {
            return null;
        }

        var url = request.Url;
        if (url is null)
        {
            return null;
        }

        // 仅拦截 wails.localhost host，放行外部资源（CDN、字体、图片等）
        if (!IsWailsLocalhost(url))
        {
            return null;
        }

        // 提取路径部分（去除 scheme 与 host）
        var path = url.Path;
        if (string.IsNullOrEmpty(path))
        {
            path = "/";
        }

        // 规范化路径：去除前导斜杠
        var assetPath = path.TrimStart('/');
        if (string.IsNullOrEmpty(assetPath))
        {
            assetPath = "index.html";
        }

        // 同步调用 AssetServer（ShouldInterceptRequest 不支持 async）。
        // 当前 AssetServer 中间件链基于 Task.FromResult 包装同步 AssetManager.Open 调用，
        // 实际无真正异步 I/O，不会死锁。
        // P0-4：传递窗口名称以支持 per-window CSP 注入
        var content = _assetServer.ServeAsync(assetPath, _windowName).GetAwaiter().GetResult();
        if (content is not null && content.Length > 0)
        {
            var mimeType = _assetServer.GetMimeType(assetPath);
            return new WebResourceResponse(mimeType, "utf-8", new MemoryStream(content));
        }

        // SPA 路由回退：无扩展名的路径（非 /api/）回退到 index.html
        if (ShouldFallbackToIndex(assetPath))
        {
            var fallbackContent = _assetServer.ServeAsync("index.html", _windowName).GetAwaiter().GetResult();
            if (fallbackContent is not null && fallbackContent.Length > 0)
            {
                return new WebResourceResponse("text/html", "utf-8", new MemoryStream(fallbackContent));
            }
        }

        // 无匹配资源：返回 404
        return new WebResourceResponse("text/plain", "utf-8", 404, "Not Found", new Dictionary<string, string>(), Stream.Null);
    }

    /// <summary>
    /// 判断请求 URL 是否指向 <c>wails.localhost</c>。
    /// 同时兼容 <c>http://wails.localhost/</c> 与 <c>https://wails.localhost/</c>。
    /// </summary>
    /// <param name="url">请求 URL。</param>
    /// <returns>匹配返回 true；否则返回 false。</returns>
    private static bool IsWailsLocalhost(global::Android.Net.Uri url)
    {
        var host = url.Host;
        return string.Equals(host, WailsHost, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断是否应回退到 index.html。
    /// 满足以下条件之一则回退：
    /// <list type="bullet">
    ///   <item>路径无扩展名（SPA 路由，如 <c>/users</c>）。</item>
    ///   <item>路径不以 <c>api/</c> 开头（避免误把 IPC 请求当 SPA 路由）。</item>
    /// </list>
    /// </summary>
    /// <param name="assetPath">规范化后的资源路径（无前导斜杠）。</param>
    /// <returns>应回退返回 true；否则返回 false。</returns>
    private static bool ShouldFallbackToIndex(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return false;
        }

        if (assetPath.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // /api/ 路径不触发 SPA 回退（应由前端 fetch 直接处理 404）
        if (assetPath.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !Path.HasExtension(assetPath);
    }
}
