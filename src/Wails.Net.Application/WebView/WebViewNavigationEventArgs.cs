namespace Wails.Net.Application.WebViews;

/// <summary>
/// WebView 导航事件参数。
/// </summary>
public sealed class WebViewNavigationEventArgs : EventArgs
{
    /// <summary>目标 URL</summary>
    public string Url { get; }

    /// <summary>是否为主框架</summary>
    public bool IsMainFrame { get; }

    /// <summary>导航状态</summary>
    public WebViewLoadStatus Status { get; }

    /// <summary>
    /// 初始化 <see cref="WebViewNavigationEventArgs"/> 的新实例。
    /// </summary>
    /// <param name="url">目标 URL。</param>
    /// <param name="isMainFrame">是否为主框架。</param>
    /// <param name="status">导航状态。</param>
    public WebViewNavigationEventArgs(string url, bool isMainFrame, WebViewLoadStatus status)
    {
        Url = url;
        IsMainFrame = isMainFrame;
        Status = status;
    }
}
