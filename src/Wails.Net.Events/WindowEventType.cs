namespace Wails.Net.Events;

/// <summary>
/// 窗口级别的事件类型枚举。
/// </summary>
public enum WindowEventType : uint
{
    /// <summary>窗口已创建。</summary>
    WindowCreated = 1000,

    /// <summary>窗口即将关闭。</summary>
    WindowClosing = 1001,

    /// <summary>窗口已关闭。</summary>
    WindowClosed = 1002,

    /// <summary>窗口获得焦点。</summary>
    WindowFocus = 1003,

    /// <summary>窗口失去焦点。</summary>
    WindowFocusLost = 1004,

    /// <summary>窗口已移动。</summary>
    WindowMoved = 1005,

    /// <summary>窗口已调整大小。</summary>
    WindowResized = 1006,

    /// <summary>窗口已最小化。</summary>
    WindowMinimised = 1007,

    /// <summary>窗口已最大化。</summary>
    WindowMaximised = 1008,

    /// <summary>窗口已从最小化状态恢复。</summary>
    WindowUnminimised = 1009,

    /// <summary>窗口已从最大化状态恢复。</summary>
    WindowUnmaximised = 1010,

    /// <summary>窗口已进入全屏模式。</summary>
    WindowFullscreen = 1011,

    /// <summary>窗口已退出全屏模式。</summary>
    WindowUnfullscreen = 1012,

    /// <summary>窗口 DPI（每英寸点数）已更改。</summary>
    WindowDPIChanged = 1013,

    /// <summary>文件已拖放到窗口。</summary>
    WindowFileDropped = 1014,

    /// <summary>拖拽操作进入窗口。</summary>
    WindowDragEnter = 1015,

    /// <summary>拖拽操作离开窗口。</summary>
    WindowDragLeave = 1016,

    /// <summary>拖拽操作在窗口中释放（放下）。</summary>
    WindowDragDrop = 1017,

    /// <summary>拖拽操作在窗口上移动。</summary>
    WindowDragOver = 1018,

    /// <summary>开发者工具已打开。</summary>
    WindowDevToolsOpened = 1019,

    /// <summary>开发者工具已关闭。</summary>
    WindowDevToolsClosed = 1020,

    /// <summary>窗口已显示。</summary>
    WindowShow = 1021,

    /// <summary>窗口已隐藏。</summary>
    WindowHide = 1022,
}
