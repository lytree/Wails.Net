using Android.Webkit;
using Wails.Net.Application.Platform;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Application.Windows;

/// <summary>
/// IPC 桥接对象，通过 <see cref="WebView.AddJavascriptInterface"/> 注入到 JS 全局。
/// 对应 ADR-0002 §5：Android 端 IPC 桥。
///
/// 由于 .NET Android 工作负载的 <c>Android.Webkit.WebView</c> 不直接提供
/// <c>AddWebMessageListener</c>（该方法属于 AndroidX WebKit 扩展，需额外依赖），
/// 改用 <see cref="WebView.AddJavascriptInterface"/> 注入桥接对象。
/// 前端通过 <c>window.WailsBridge.postMessage(json)</c> 调用后端，
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
    /// JS 调用的入口方法，前端通过 <c>window.WailsBridge.postMessage(json)</c> 调用。
    /// 使用 <see cref="JavascriptInterfaceAttribute"/> 标记为可被 JS 调用的方法。
    /// 内部转发到 <see cref="WailsApplication.HandleMessageFromFrontend"/> 进行 IPC 消息处理。
    /// </summary>
    /// <param name="message">前端发送的 JSON 消息字符串。</param>
    [JavascriptInterface]
    public void PostMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        // 转发到 Application.HandleMessageFromFrontend 进行 IPC 消息处理。
        // 注意：此处可能在 WebView 线程或 Java 线程调用，HandleMessageFromFrontend 内部
        // 已设计为线程安全。
        var app = WailsApplication.Get();
        if (app is not null)
        {
            // 异步处理避免阻塞 Java WebView 线程
            _ = app.HandleMessageFromFrontend(message, _windowId);
        }
    }
}
