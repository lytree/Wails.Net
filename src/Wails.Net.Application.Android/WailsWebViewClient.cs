using Android.Webkit;

namespace Wails.Net.Application.Windows;

/// <summary>
/// Android WebView 资源拦截器，继承 <see cref="WebViewClient"/>。
/// 对应 ADR-0002 §4：通过重写 <c>ShouldInterceptRequest</c> 拦截 WebView 资源请求，
/// 将自定义协议（如 wails://）或本地路径请求转发到 <see cref="Wails.Net.AssetServer.AssetServer"/>。
/// </summary>
public sealed class WailsWebViewClient : WebViewClient
{
    /// <summary>
    /// 静态资源服务器引用，可能为 null（未配置 AssetServer 时）。
    /// </summary>
    private readonly Wails.Net.AssetServer.AssetServer? _assetServer;

    /// <summary>
    /// 构造 WailsWebViewClient 实例。
    /// </summary>
    /// <param name="assetServer">静态资源服务器引用，可为 null。</param>
    public WailsWebViewClient(Wails.Net.AssetServer.AssetServer? assetServer)
    {
        _assetServer = assetServer;
    }

    /// <summary>
    /// 拦截 WebView 资源请求。
    /// 当配置了 <see cref="AssetServer"/> 时，将请求路径转发到 AssetServer 处理；
    /// 支持根路径回退到 index.html，以及 SPA 路由回退。
    /// 未配置 AssetServer 时返回 null，使用 WebView 默认加载行为。
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

        // 同步调用 AssetServer（ShouldInterceptRequest 不支持 async）
        var content = _assetServer.ServeAsync(assetPath).GetAwaiter().GetResult();
        if (content is not null && content.Length > 0)
        {
            var mimeType = _assetServer.GetMimeType(assetPath);
            return new WebResourceResponse(mimeType, "utf-8", new MemoryStream(content));
        }

        // SPA 路由回退：无扩展名的路径回退到 index.html
        if (!string.IsNullOrEmpty(assetPath)
            && !assetPath.Equals("index.html", StringComparison.OrdinalIgnoreCase)
            && !Path.HasExtension(assetPath))
        {
            var fallbackContent = _assetServer.ServeAsync("index.html").GetAwaiter().GetResult();
            if (fallbackContent is not null && fallbackContent.Length > 0)
            {
                return new WebResourceResponse("text/html", "utf-8", new MemoryStream(fallbackContent));
            }
        }

        // 无匹配资源：返回 404
        return new WebResourceResponse("text/plain", "utf-8", 404, "Not Found", new Dictionary<string, string>(), Stream.Null);
    }
}
