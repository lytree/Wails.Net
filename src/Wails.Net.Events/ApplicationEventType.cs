namespace Wails.Net.Events;

/// <summary>
/// 应用程序级别的事件类型枚举（对应 Wails v3 中的 events.go）。
/// </summary>
public enum ApplicationEventType : uint
{
    /// <summary>应用程序已启动。</summary>
    Started = 0,

    /// <summary>应用程序已关闭。</summary>
    Shutdown = 1,

    /// <summary>系统主题已更改。</summary>
    ThemeChanged = 2,

    /// <summary>文件已拖放到应用程序。</summary>
    FileDropped = 3,

    /// <summary>窗口即将关闭。</summary>
    WindowClosing = 4,

    /// <summary>窗口已关闭。</summary>
    WindowClosed = 5,

    /// <summary>窗口获得焦点。</summary>
    WindowFocus = 6,

    /// <summary>窗口失去焦点。</summary>
    WindowFocusLost = 7,

    /// <summary>DPI（每英寸点数）已更改。</summary>
    DPIChanged = 8,

    /// <summary>电池状态已更改。</summary>
    BatteryChanged = 9,

    /// <summary>网络状态已更改。</summary>
    NetworkChanged = 10,

    /// <summary>系统从挂起状态恢复。</summary>
    Resume = 11,

    /// <summary>系统已挂起。</summary>
    Suspend = 12,

    /// <summary>显示器配置已更改。</summary>
    DisplayChanged = 13,

    /// <summary>剪贴板内容已更改。</summary>
    ClipboardChanged = 14,

    /// <summary>系统托盘图标被点击。</summary>
    SystemTrayClicked = 15,

    /// <summary>系统托盘菜单已打开。</summary>
    SystemTrayMenuOpened = 16,

    /// <summary>窗口运行时就绪。</summary>
    WindowRuntimeReady = 17,

    /// <summary>窗口进入全屏。</summary>
    WindowEnterFullScreen = 18,

    /// <summary>窗口退出全屏。</summary>
    WindowExitFullScreen = 19,

    /// <summary>URL 开始加载。</summary>
    URLStartsLoading = 20,

    /// <summary>URL 加载完成。</summary>
    URLFinishedLoading = 21,

    /// <summary>窗口即将卸载。</summary>
    WindowBeforeUnload = 22,

    /// <summary>URL 加载失败。</summary>
    URLLoadFailed = 23,

    /// <summary>收到深度链接。</summary>
    DeepLinkReceived = 24,

    /// <summary>应用激活（成为前台活动应用）。</summary>
    ApplicationActive = 25,

    /// <summary>应用失活（失去前台活动状态）。</summary>
    ApplicationInactive = 26,
}
