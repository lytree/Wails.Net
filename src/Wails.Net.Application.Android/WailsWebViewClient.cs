using Android.Webkit;

namespace Wails.Net.Application.Windows;

/// <summary>
/// Android WebView 资源拦截器，继承 <see cref="WebViewClient"/>。
/// 对应 ADR-0002 §4：通过重写 <c>ShouldInterceptRequest</c> 拦截 WebView 资源请求，
/// 将自定义协议（如 wails://）的请求转发到 <see cref="Wails.Net.AssetServer.AssetServer"/>。
/// </summary>
public sealed class WailsWebViewClient : WebViewClient
{
    /// <summary>
    /// 拦截 WebView 资源请求。
    /// 骨架实现返回 null（使用 WebView 默认加载行为）。
    /// 完整实现需解析请求 URL，将自定义协议请求转发到 AssetServer：
    /// <code>
    /// var url = request?.Url;
    /// if (url is null) return null;
    /// var path = url.Path ?? "/";
    /// var content = await _assetServer.ServeAsync(path);
    /// var mimeType = GetMimeType(path);
    /// return new WebResourceResponse(mimeType, "utf-8", new MemoryStream(content));
    /// </code>
    /// </summary>
    /// <param name="view">发起请求的 WebView 实例。</param>
    /// <param name="request">Web 资源请求信息。</param>
    /// <returns>自定义的 <see cref="WebResourceResponse"/>，或 null 使用默认加载。</returns>
    public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
    {
        // TODO: 完整实现需注入 AssetServer 引用，拦截自定义协议请求并返回资源内容。
        // 骨架实现返回 null，使用 WebView 默认网络加载行为。
        return null;
    }
}
