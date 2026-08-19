using Android.Webkit;
using Wails.Net.Application.Platform;
using Wails.Net.Events;
using WailsApplication = Wails.Net.Application.Application;
using Wails.Net.Application.Webview;

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
    /// 所属窗口 ID，用于页面加载完成后派发 <see cref="WindowEventType.WindowRuntimeReady"/>。
    /// 对应 Wails v3 Go 版本 WebView 导航完成后的 emit(WindowRuntimeReady) 行为（P0-2 审计项）。
    /// </summary>
    private readonly uint _windowId;

    /// <summary>
    /// R2-4：当前窗口的 <see cref="WebviewRequestPipeline"/>，懒加载并缓存。
    /// </summary>
    private WebviewRequestPipeline? _pipeline;

    /// <summary>
    /// 构造 WailsWebViewClient 实例。
    /// </summary>
    /// <param name="assetServer">静态资源服务器引用，可为 null。</param>
    /// <param name="windowName">关联的窗口名称，用于 per-window CSP 注入；可为 null。</param>
    /// <param name="windowId">所属窗口 ID，页面加载完成后用于派发 WindowRuntimeReady。</param>
    public WailsWebViewClient(Wails.Net.AssetServer.AssetServer? assetServer, string? windowName = null, uint windowId = 0)
    {
        _assetServer = assetServer;
        _windowName = windowName;
        _windowId = windowId;
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

        // R2-4：统一委托给 WebviewRequestPipeline。
        // Android 主线程存在同步上下文，同步包装使用 WebviewPipelineSync.RunOnThreadPool
        // 通过 Task.Run 强制不捕获调用方上下文，避免死锁。
        var pipeline = GetOrCreatePipeline();
        var webviewRequest = new WebviewRequest
        {
            Method = request.Method ?? "GET",
            Path = path,
            RequestBody = null,
            WindowId = _windowId,
            WindowName = _windowName,
            Origin = request.RequestHeaders != null && request.RequestHeaders.TryGetValue("Referer", out var referer) ? referer : null
        };
        var response = pipeline.HandleAsync(webviewRequest).RunOnThreadPool();

        // 特殊处理：管线返回 404 且请求为 /api/ 路径时，交回 WebView 默认处理。
        if (response.StatusCode == 404 && assetPath.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return CreateWebResourceResponse(response);
    }

    /// <summary>
    /// R2-4：创建或获取当前窗口的 <see cref="WebviewRequestPipeline"/>，懒加载并缓存。
    /// </summary>
    private WebviewRequestPipeline GetOrCreatePipeline()
    {
        if (_pipeline is not null)
        {
            return _pipeline;
        }
        var app = WailsApplication.Get();
        _pipeline = new WebviewRequestPipeline(
            ipcHandler: async (body, windowId, _, _) =>
            {
                if (app is null) return null;
                var msg = await app.HandleMessageFromFrontend(body, windowId).ConfigureAwait(false);
                return (object?)msg;
            },
            assetHandler: async (path, windowName, _) =>
            {
                if (_assetServer is null) return null;
                var bytes = await _assetServer.ServeAsync(path, windowName).ConfigureAwait(false);
                return (byte[]?)bytes;
            },
            mimeTypeResolver: path => _assetServer?.GetMimeType(path) ?? "application/octet-stream");
        return _pipeline;
    }

    /// <summary>
    /// R2-4：将 <see cref="WebviewResponse"/> 转换为 Android <see cref="WebResourceResponse"/>。
    /// </summary>
    private static WebResourceResponse CreateWebResourceResponse(WebviewResponse response)
    {
        var stream = response.Body is { Length: > 0 } ? new MemoryStream(response.Body) : Stream.Null;
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = response.ContentType
        };
        return new WebResourceResponse(
            response.ContentType,
            "utf-8",
            response.StatusCode,
            response.StatusText,
            headers,
            stream);
    }

    /// <summary>
    /// 页面加载完成后触发 <see cref="WindowEventType.WindowRuntimeReady"/> 窗口事件（P0-2 审计项）。
    /// <para>
    /// 对应 Wails v3 Go 版本 WebView 导航完成后的 emit(WindowRuntimeReady) 调用：
    /// Windows 平台在 <c>CoreWebView2.NavigationCompleted</c> 中派发，
    /// Linux 平台在 runtime 注入完成后派发，Android 在此处通过 <c>OnPageFinished</c> 派发，
    /// 使 <see cref="WailsApplication"/> 的事件总线与生命周期等待（如
    /// <c>WaitUntilPlatformStartedAsync</c>）在三平台行为一致。
    /// </para>
    /// </summary>
    /// <param name="view">加载完成的 WebView。</param>
    /// <param name="url">完成加载的页面 URL。</param>
    public override void OnPageFinished(WebView? view, string? url)
    {
        base.OnPageFinished(view, url);

        if (_windowId == 0)
        {
            return; // 未绑定窗口（纯资源拦截场景），不派发窗口事件
        }

        WailsApplication.Get()?.DispatchWindowEvent(_windowId, (uint)WindowEventType.WindowRuntimeReady);
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

}
