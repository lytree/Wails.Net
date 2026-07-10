namespace Wails.Net.Events;

/// <summary>
/// 系统事件名称常量。
/// 这些常量定义了 Wails 框架内部使用的保留事件名称，
/// 自定义事件名称不应与这些常量冲突。
/// </summary>
public static class KnownEvents
{
    /// <summary>应用程序启动事件。</summary>
    public const string Startup = "wails:startup";

    /// <summary>应用程序关闭事件。</summary>
    public const string Shutdown = "wails:shutdown";

    /// <summary>系统主题更改事件。</summary>
    public const string ThemeChanged = "wails:theme:changed";

    /// <summary>文件拖放事件。</summary>
    public const string FileDropped = "wails:file:dropped";

    /// <summary>窗口即将关闭事件。</summary>
    public const string WindowClosing = "wails:window:closing";

    /// <summary>窗口已关闭事件。</summary>
    public const string WindowClosed = "wails:window:closed";

    /// <summary>窗口获得焦点事件。</summary>
    public const string WindowFocus = "wails:window:focus";

    /// <summary>窗口失去焦点事件。</summary>
    public const string WindowFocusLost = "wails:window:focuslost";

    /// <summary>DPI 更改事件。</summary>
    public const string DPIChanged = "wails:dpi:changed";

    /// <summary>电池状态更改事件。</summary>
    public const string BatteryChanged = "wails:battery:changed";

    /// <summary>网络状态更改事件。</summary>
    public const string NetworkChanged = "wails:network:changed";

    /// <summary>剪贴板内容更改事件。</summary>
    public const string ClipboardChanged = "wails:clipboard:changed";

    /// <summary>系统托盘点击事件。</summary>
    public const string SystemTrayClick = "wails:tray:click";

    /// <summary>系统托盘菜单打开事件。</summary>
    public const string SystemTrayMenuOpen = "wails:tray:menu:open";
}
