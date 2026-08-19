using System.Text.Json;
using Android.Webkit;
using Wails.Net.Application.Bindings;
using WailsApplication = Wails.Net.Application.Application;
using Wails.Net.Application.Webview;

namespace Wails.Net.Application.Android;

/// <summary>
/// IPC 桥接对象，通过 <see cref="WebView.AddJavascriptInterface"/> 注入到 JS 全局。
/// 对应 ADR-0002 §5：Android 端 IPC 桥。
///
/// 由于 .NET Android 工作负载的 <c>Android.Webkit.WebView</c> 不直接提供
/// <c>AddWebMessageListener</c>（该方法属于 AndroidX WebKit 扩展，需额外依赖），
/// 改用 <see cref="WebView.AddJavascriptInterface"/> 注入桥接对象。
/// 前端通过 <c>const result = window.WailsBridge.invoke(json)</c> 同步调用后端并获取响应，
/// 与 Windows WebView2 的 <c>window.chrome.webview.postMessage</c> 模式一致。
///
/// 类型必须继承 <c>Java.Lang.Object</c> 以满足 JNI 注册要求。
/// </summary>
public sealed class WailsWebMessageListener : Java.Lang.Object
{
    /// <summary>
    /// 当前窗口 ID，用于将消息路由到正确的 WebviewWindow 实例。
    /// </summary>
    private readonly uint _windowId;

    /// <summary>
    /// R2-4：当前监听器的 <see cref="WebviewRequestPipeline"/>，懒加载并缓存。
    /// </summary>
    private WebviewRequestPipeline? _pipeline;

    /// <summary>
    /// 构造 WailsWebMessageListener 实例。
    /// </summary>
    /// <param name="windowId">所属窗口 ID。</param>
    public WailsWebMessageListener(uint windowId)
    {
        _windowId = windowId;
    }

    /// <summary>
    /// JS 同步调用入口，前端通过 <c>window.WailsBridge.invoke(json)</c> 调用并接收返回值。
    /// 使用 <see cref="JavascriptInterfaceAttribute"/> 标记为可被 JS 调用的方法。
    /// 内部转发到 <see cref="WailsApplication.HandleMessageFromFrontend"/> 进行 IPC 消息处理。
    /// </summary>
    /// <param name="message">前端发送的 JSON 消息字符串。</param>
    /// <returns>响应的 JSON 字符串；无响应时返回空字符串。前端可用 <c>JSON.parse</c> 解析。</returns>
    [JavascriptInterface]
    public string Invoke(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var app = WailsApplication.Get();
        if (app is null)
        {
            return SerializeError("应用实例不可用");
        }

        // R2-4：统一委托给 WebviewRequestPipeline。
        // Android 主线程（WebView JavascriptInterface 回调线程）存在同步上下文，
        // 同步包装使用 WebviewPipelineSync.RunOnThreadPool 通过 Task.Run
        // 强制不捕获调用方上下文，避免死锁。
        var pipeline = GetOrCreatePipeline();
        var request = new WebviewRequest
        {
            Method = "POST",
            Path = "/wails/invoke",
            RequestBody = message,
            WindowId = _windowId,
            WindowName = null,
            Origin = null
        };
        var response = pipeline.HandleAsync(request).RunOnThreadPool();
        return response.Body is null || response.Body.Length == 0
            ? string.Empty
            : System.Text.Encoding.UTF8.GetString(response.Body);
    }

    /// <summary>
    /// R2-4：创建或获取当前监听器的 <see cref="WebviewRequestPipeline"/>，懒加载并缓存。
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
            assetHandler: (_, _, _) => Task.FromResult<byte[]?>(null) /* 监听器仅用于 IPC */,
            mimeTypeResolver: _ => "application/json");
        return _pipeline;
    }

    /// <summary>
    /// 序列化错误响应为 JSON 字符串。
    /// </summary>
    /// <param name="errorMessage">错误消息。</param>
    /// <returns>JSON 字符串。</returns>
    private static string SerializeError(string errorMessage)
    {
        return JsonSerializer.Serialize(new { error = errorMessage }, JsonOptions.DefaultSerializerOptions);
    }
}
