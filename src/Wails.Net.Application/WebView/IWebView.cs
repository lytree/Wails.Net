namespace Wails.Net.Application.WebViews;

/// <summary>
/// 平台无关的 WebView 抽象接口。
/// 统一 Windows WebView2 和 Linux WebKitGTK 的交互。
/// </summary>
public interface IWebView : IDisposable
{
    /// <summary>窗口 ID</summary>
    uint WindowId { get; }

    /// <summary>当前 URL</summary>
    string Url { get; }

    /// <summary>当前标题</summary>
    string Title { get; }

    /// <summary>是否可后退</summary>
    bool CanGoBack { get; }

    /// <summary>是否可前进</summary>
    bool CanGoForward { get; }

    /// <summary>加载状态</summary>
    WebViewLoadStatus LoadStatus { get; }

    /// <summary>
    /// 导航到指定 URL。
    /// </summary>
    /// <param name="url">目标 URL。</param>
    Task NavigateAsync(string url);

    /// <summary>
    /// 加载 HTML 内容。
    /// </summary>
    /// <param name="html">HTML 内容。</param>
    Task LoadHtmlAsync(string html);

    /// <summary>
    /// 执行 JavaScript 脚本。
    /// </summary>
    /// <param name="javascript">JavaScript 脚本内容。</param>
    /// <returns>脚本执行结果（JSON 字符串）。</returns>
    Task<string> ExecuteScriptAsync(string javascript);

    /// <summary>
    /// 注入 CSS 样式。
    /// </summary>
    /// <param name="css">CSS 样式内容。</param>
    Task InjectCssAsync(string css);

    /// <summary>
    /// 向前端发送消息（JSON 字符串）。
    /// </summary>
    /// <param name="json">消息内容（JSON 字符串）。</param>
    Task PostMessageAsync(string json);

    /// <summary>
    /// 设置缩放。
    /// </summary>
    /// <param name="zoom">缩放比例。</param>
    void SetZoom(double zoom);

    /// <summary>
    /// 后退。
    /// </summary>
    void GoBack();

    /// <summary>
    /// 前进。
    /// </summary>
    void GoForward();

    /// <summary>
    /// 重新加载。
    /// </summary>
    void Reload();

    /// <summary>
    /// 停止加载。
    /// </summary>
    void Stop();

    /// <summary>
    /// 打开开发者工具。
    /// </summary>
    void OpenDevTools();

    /// <summary>
    /// 关闭开发者工具。
    /// </summary>
    void CloseDevTools();

    /// <summary>
    /// 消息接收事件（前端 postMessage）。
    /// </summary>
    event EventHandler<WebViewMessageEventArgs>? MessageReceived;

    /// <summary>
    /// 导航开始事件。
    /// </summary>
    event EventHandler<WebViewNavigationEventArgs>? NavigationStarted;

    /// <summary>
    /// 导航完成事件。
    /// </summary>
    event EventHandler<WebViewNavigationEventArgs>? NavigationCompleted;

    /// <summary>
    /// 标题变化事件。
    /// </summary>
    event EventHandler<string>? TitleChanged;
}
