using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Transport;

/// <summary>
/// 原生 IPC 传输层接口，对应 WebView 原生 postMessage 通道。
/// 与 <see cref="ITransport"/> 并存，提供低延迟、无 HTTP 协议栈开销的消息收发能力。
/// <para>
/// 对应 Wails v3 Go 版本中 <c>runtime_windows.go</c>、<c>runtime_linux.go</c>、
/// <c>runtime_android.go</c> 注入的 <c>window._wails.invoke</c> 桥接机制。
/// </para>
/// <para>
/// 使用场景：
/// <list type="bullet">
/// <item>常规小消息调用（&lt; 512KB）：走原生 postMessage，避免 HTTP 头解析开销。</item>
/// <item>后端 → 前端事件推送：替代 ExecJS 注入，由前端 <c>message</c> 事件监听器接收。</item>
/// <item>大消息（&gt; 512KB）：仍走 HTTP 分块上传（<see cref="HttpTransport"/>），原生通道不支持二进制。</item>
/// </list>
/// </para>
/// </summary>
public interface INativeIpcTransport : IWailsEventListener
{
    /// <summary>
    /// 注册窗口与其平台实现。
    /// <para>
    /// 注册过程会调用 <paramref name="impl"/> 的 <c>SetNativeMessageHandler</c>，
    /// 将内部消息回调安装为消息路由。之后前端通过原生 postMessage 发送的消息
    /// 会自动路由到 <see cref="MessageProcessor"/> 处理。
    /// </para>
    /// <para>
    /// 应由平台 <c>IPlatformApp.CreateWebviewWindow</c> 在创建窗口后调用。
    /// </para>
    /// </summary>
    /// <param name="windowId">窗口 ID。</param>
    /// <param name="impl">平台特定的窗口实现（必须支持原生 postMessage 接收）。</param>
    void RegisterWindow(uint windowId, IWebviewWindowImpl impl);

    /// <summary>
    /// 解绑窗口的消息接收回调并清理资源。
    /// </summary>
    /// <param name="windowId">窗口 ID。</param>
    void UnregisterWindow(uint windowId);

    /// <summary>
    /// 向指定窗口推送消息（后端 → 前端）。
    /// 平台实现通过 WebView 原生 API
    /// （WebView2 <c>PostWebMessageAsString</c> /
    /// WebKitGTK <c>evaluate_javascript</c> /
    /// Android <c>EvaluateJavascript</c>）发送。
    /// </summary>
    /// <param name="windowId">目标窗口 ID。</param>
    /// <param name="message">要推送的 JSON 字符串。</param>
    /// <returns>表示推送操作的异步任务。</returns>
    Task PostToWindowAsync(uint windowId, string message);

    /// <summary>
    /// 获取平台是否支持原生 postMessage 二进制传输。
    /// <para>
    /// Windows 返回 <c>true</c>（WebView2 支持 <c>postMessageWithAdditionalObjects</c>），
    /// Linux/Android 返回 <c>false</c>（仅字符串）。
    /// </para>
    /// </summary>
    bool SupportsBinary { get; }

    /// <summary>
    /// 获取当前已注册的窗口数量（主要用于测试与诊断）。
    /// </summary>
    int RegisteredWindowCount { get; }
}
