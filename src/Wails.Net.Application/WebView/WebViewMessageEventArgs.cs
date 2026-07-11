namespace Wails.Net.Application.WebViews;

/// <summary>
/// WebView 消息事件参数。
/// </summary>
public sealed class WebViewMessageEventArgs : EventArgs
{
    /// <summary>消息内容（JSON 字符串）</summary>
    public string Message { get; }

    /// <summary>源窗口 ID</summary>
    public uint WindowId { get; }

    /// <summary>
    /// 初始化 <see cref="WebViewMessageEventArgs"/> 的新实例。
    /// </summary>
    /// <param name="message">消息内容（JSON 字符串）。</param>
    /// <param name="windowId">源窗口 ID。</param>
    public WebViewMessageEventArgs(string message, uint windowId)
    {
        Message = message;
        WindowId = windowId;
    }
}
