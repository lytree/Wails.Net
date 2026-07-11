namespace Wails.Net.Events;

/// <summary>
/// 系统事件名称常量。
/// 这些常量定义了 Wails 框架内部使用的保留事件名称，
/// 自定义事件名称不应与这些常量冲突。
/// 对应 Wails v3 Go 版本 events.go 中的事件名称常量。
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

    /// <summary>窗口已创建事件。</summary>
    public const string WindowCreated = "wails:window:created";

    /// <summary>窗口已移动事件。</summary>
    public const string WindowMoved = "wails:window:moved";

    /// <summary>窗口已调整大小事件。</summary>
    public const string WindowResized = "wails:window:resized";

    /// <summary>窗口已最小化事件。</summary>
    public const string WindowMinimised = "wails:window:minimised";

    /// <summary>窗口已最大化事件。</summary>
    public const string WindowMaximised = "wails:window:maximised";

    /// <summary>窗口已取消最小化事件。</summary>
    public const string WindowUnminimised = "wails:window:unminimised";

    /// <summary>窗口已取消最大化事件。</summary>
    public const string WindowUnmaximised = "wails:window:unmaximised";

    /// <summary>窗口进入全屏事件。</summary>
    public const string WindowFullscreen = "wails:window:fullscreen";

    /// <summary>窗口退出全屏事件。</summary>
    public const string WindowUnfullscreen = "wails:window:unfullscreen";

    /// <summary>窗口文件拖放事件。</summary>
    public const string WindowFileDropped = "wails:window:file:dropped";

    /// <summary>拖拽操作进入窗口事件。</summary>
    public const string WindowDragEnter = "wails:window:drag:enter";

    /// <summary>拖拽操作离开窗口事件。</summary>
    public const string WindowDragLeave = "wails:window:drag:leave";

    /// <summary>拖拽操作在窗口中释放事件。</summary>
    public const string WindowDragDrop = "wails:window:drag:drop";

    /// <summary>拖拽操作在窗口上移动事件。</summary>
    public const string WindowDragOver = "wails:window:drag:over";

    /// <summary>开发者工具已打开事件。</summary>
    public const string WindowDevToolsOpened = "wails:window:devtools:opened";

    /// <summary>开发者工具已关闭事件。</summary>
    public const string WindowDevToolsClosed = "wails:window:devtools:closed";

    /// <summary>窗口已显示事件。</summary>
    public const string WindowShow = "wails:window:show";

    /// <summary>窗口已隐藏事件。</summary>
    public const string WindowHide = "wails:window:hide";

    /// <summary>窗口运行时就绪事件。</summary>
    public const string WindowRuntimeReady = "wails:window:runtime:ready";

    /// <summary>文件拖放目标事件。</summary>
    public const string WindowFileDropTarget = "wails:window:file:droptarget";

    /// <summary>窗口进入全屏事件（带 enter 前缀）。</summary>
    public const string WindowEnterFullScreen = "wails:window:enter:fullscreen";

    /// <summary>窗口退出全屏事件（带 exit 前缀）。</summary>
    public const string WindowExitFullScreen = "wails:window:exit:fullscreen";

    /// <summary>窗口标题已更改事件。</summary>
    public const string WindowTitleChanged = "wails:window:title:changed";

    /// <summary>系统从挂起状态恢复事件。</summary>
    public const string Resume = "wails:resume";

    /// <summary>系统已挂起事件。</summary>
    public const string Suspend = "wails:suspend";

    /// <summary>显示器配置已更改事件。</summary>
    public const string DisplayChanged = "wails:display:changed";

    /// <summary>URL 开始加载事件。</summary>
    public const string URLStartsLoading = "wails:url:starts:loading";

    /// <summary>URL 加载完成事件。</summary>
    public const string URLFinishedLoading = "wails:url:finished:loading";

    /// <summary>URL 加载失败事件。</summary>
    public const string URLLoadFailed = "wails:url:load:failed";

    /// <summary>窗口即将卸载事件。</summary>
    public const string WindowBeforeUnload = "wails:window:before:unload";

    /// <summary>
    /// 根据窗口事件类型返回对应的事件名称。
    /// </summary>
    /// <param name="type">窗口事件类型枚举值。</param>
    /// <returns>事件名称字符串。</returns>
    public static string GetEventName(WindowEventType type) => type switch
    {
        WindowEventType.WindowCreated => WindowCreated,
        WindowEventType.WindowClosing => WindowClosing,
        WindowEventType.WindowClosed => WindowClosed,
        WindowEventType.WindowFocus => WindowFocus,
        WindowEventType.WindowFocusLost => WindowFocusLost,
        WindowEventType.WindowMoved => WindowMoved,
        WindowEventType.WindowResized => WindowResized,
        WindowEventType.WindowMinimised => WindowMinimised,
        WindowEventType.WindowMaximised => WindowMaximised,
        WindowEventType.WindowUnminimised => WindowUnminimised,
        WindowEventType.WindowUnmaximised => WindowUnmaximised,
        WindowEventType.WindowFullscreen => WindowFullscreen,
        WindowEventType.WindowUnfullscreen => WindowUnfullscreen,
        WindowEventType.WindowDPIChanged => DPIChanged,
        WindowEventType.WindowFileDropped => WindowFileDropped,
        WindowEventType.WindowDragEnter => WindowDragEnter,
        WindowEventType.WindowDragLeave => WindowDragLeave,
        WindowEventType.WindowDragDrop => WindowDragDrop,
        WindowEventType.WindowDragOver => WindowDragOver,
        WindowEventType.WindowDevToolsOpened => WindowDevToolsOpened,
        WindowEventType.WindowDevToolsClosed => WindowDevToolsClosed,
        WindowEventType.WindowShow => WindowShow,
        WindowEventType.WindowHide => WindowHide,
        WindowEventType.WindowRuntimeReady => WindowRuntimeReady,
        WindowEventType.WindowFileDropTarget => WindowFileDropTarget,
        WindowEventType.WindowEnterFullScreen => WindowEnterFullScreen,
        WindowEventType.WindowExitFullScreen => WindowExitFullScreen,
        WindowEventType.WindowTitleChanged => WindowTitleChanged,
        _ => $"wails:custom:{(uint)type}"
    };

    /// <summary>
    /// 根据应用程序事件类型返回对应的事件名称。
    /// </summary>
    /// <param name="type">应用程序事件类型枚举值。</param>
    /// <returns>事件名称字符串。</returns>
    public static string GetEventName(ApplicationEventType type) => type switch
    {
        ApplicationEventType.Started => Startup,
        ApplicationEventType.Shutdown => Shutdown,
        ApplicationEventType.ThemeChanged => ThemeChanged,
        ApplicationEventType.FileDropped => FileDropped,
        ApplicationEventType.WindowClosing => WindowClosing,
        ApplicationEventType.WindowClosed => WindowClosed,
        ApplicationEventType.WindowFocus => WindowFocus,
        ApplicationEventType.WindowFocusLost => WindowFocusLost,
        ApplicationEventType.DPIChanged => DPIChanged,
        ApplicationEventType.BatteryChanged => BatteryChanged,
        ApplicationEventType.NetworkChanged => NetworkChanged,
        ApplicationEventType.Resume => Resume,
        ApplicationEventType.Suspend => Suspend,
        ApplicationEventType.DisplayChanged => DisplayChanged,
        ApplicationEventType.ClipboardChanged => ClipboardChanged,
        ApplicationEventType.SystemTrayClicked => SystemTrayClick,
        ApplicationEventType.SystemTrayMenuOpened => SystemTrayMenuOpen,
        ApplicationEventType.WindowRuntimeReady => WindowRuntimeReady,
        ApplicationEventType.WindowEnterFullScreen => WindowEnterFullScreen,
        ApplicationEventType.WindowExitFullScreen => WindowExitFullScreen,
        ApplicationEventType.URLStartsLoading => URLStartsLoading,
        ApplicationEventType.URLFinishedLoading => URLFinishedLoading,
        ApplicationEventType.URLLoadFailed => URLLoadFailed,
        ApplicationEventType.WindowBeforeUnload => WindowBeforeUnload,
        _ => $"wails:custom:{(uint)type}"
    };

    /// <summary>
    /// 根据事件类型的 uint 值返回对应的事件名称。
    /// 自动区分窗口事件（>= 1000）与应用程序事件（&lt; 1000）。
    /// </summary>
    /// <param name="eventType">事件类型的数值。</param>
    /// <returns>事件名称字符串；若为未知自定义事件则返回 <c>wails:custom:{eventType}</c>。</returns>
    public static string GetEventName(uint eventType)
    {
        if (eventType >= (uint)WindowEventType.WindowCreated)
        {
            if (Enum.IsDefined(typeof(WindowEventType), eventType))
            {
                return GetEventName((WindowEventType)eventType);
            }
        }
        else if (Enum.IsDefined(typeof(ApplicationEventType), eventType))
        {
            return GetEventName((ApplicationEventType)eventType);
        }

        return $"wails:custom:{eventType}";
    }
}
