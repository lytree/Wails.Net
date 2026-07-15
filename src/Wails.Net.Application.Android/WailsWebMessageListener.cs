using System.Text.Json;
using Android.Webkit;
using Wails.Net.Application.Bindings;
using WailsApplication = Wails.Net.Application.Application;

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

        // 同步等待结果返回给前端。
        // 注意：JS 调用 JavascriptInterface 方法是同步阻塞的，此处 GetResult() 不会死锁
        // 因为 HandleMessageFromFrontend 内部不依赖 UI 线程（绑定调用在工作线程执行）。
        var response = app.HandleMessageFromFrontend(message, _windowId).GetAwaiter().GetResult();
        if (response is null)
        {
            return string.Empty;
        }

        return JsonSerializer.Serialize(response, JsonOptions.DefaultSerializerOptions);
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
