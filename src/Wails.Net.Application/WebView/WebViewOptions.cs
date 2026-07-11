namespace Wails.Net.Application.WebViews;

/// <summary>
/// WebView 配置选项。
/// </summary>
public sealed class WebViewOptions
{
    /// <summary>初始 URL</summary>
    public string? Url { get; set; }

    /// <summary>初始 HTML</summary>
    public string? Html { get; set; }

    /// <summary>是否启用 DevTools</summary>
    public bool DevToolsEnabled { get; set; } = false;

    /// <summary>是否启用默认右键菜单</summary>
    public bool DefaultContextMenuEnabled { get; set; } = true;

    /// <summary>是否启用缩放</summary>
    public bool ZoomEnabled { get; set; } = true;

    /// <summary>初始缩放</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>注入的 CSS</summary>
    public string? Css { get; set; }

    /// <summary>用户代理</summary>
    public string? UserAgent { get; set; }
}
