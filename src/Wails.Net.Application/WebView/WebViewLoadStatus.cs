namespace Wails.Net.Application.WebViews;

/// <summary>
/// WebView 加载状态。
/// </summary>
public enum WebViewLoadStatus
{
    /// <summary>未加载</summary>
    NotLoaded,
    /// <summary>加载中</summary>
    Loading,
    /// <summary>已加载</summary>
    Loaded,
    /// <summary>加载失败</summary>
    Failed
}
