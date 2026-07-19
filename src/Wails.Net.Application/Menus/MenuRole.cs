namespace Wails.Net.Application.Menus;

/// <summary>
/// 菜单项预定义角色。对应 Wails v3 Go 版本 menu.go 中的 Role 常量，
/// 并参考 Tauri v2 PredefinedMenuItem 的平台支持矩阵。
/// </summary>
/// <remarks>
/// 当 <see cref="MenuItem.Role"/> 不为 <see cref="None"/> 时，
/// 平台实现将自动调用系统原生命令，忽略 <see cref="MenuItem.Callback"/> 字段。
/// macOS 专属角色在 Windows/Linux 上静默降级为 no-op，不抛异常。
/// 平台支持矩阵通过 <see cref="MenuRoleHelper.IsMacOSExclusive"/> 在运行时校验，
/// 不在枚举字段上使用 [SupportedOSPlatform] 特性以避免跨平台代码触发 CA1416。
/// </remarks>
public enum MenuRole
{
    /// <summary>
    /// 普通菜单项（默认值），走自定义 <see cref="MenuItem.Callback"/>。
    /// </summary>
    None = 0,

    /// <summary>
    /// 分隔符。等价于 <see cref="Menu.IsSeparator"/> = true，提供语义化别名。
    /// </summary>
    Separator,

    // ─── Edit 角色（编辑菜单，最常用；Windows/Linux/macOS 支持，Android 不支持）───

    /// <summary>
    /// 复制。调用焦点控件的系统复制命令。
    /// </summary>
    Copy,

    /// <summary>
    /// 剪切。调用焦点控件的系统剪切命令。
    /// </summary>
    Cut,

    /// <summary>
    /// 粘贴。调用焦点控件的系统粘贴命令。
    /// </summary>
    Paste,

    /// <summary>
    /// 全选。调用焦点控件的系统全选命令。
    /// </summary>
    SelectAll,

    /// <summary>
    /// 撤销。通过 document.execCommand 兼容 Windows/Linux/macOS。
    /// </summary>
    Undo,

    /// <summary>
    /// 重做。通过 document.execCommand 兼容 Windows/Linux/macOS。
    /// </summary>
    Redo,

    // ─── Window 角色 ───

    /// <summary>
    /// 最小化窗口（Windows/macOS 原生支持；Linux 通过 GTK 窗口管理器）。
    /// </summary>
    Minimize,

    /// <summary>
    /// 最大化/还原窗口（Windows/macOS 原生支持；Linux 通过 GTK 窗口管理器）。
    /// </summary>
    Maximize,

    /// <summary>
    /// 进入/退出全屏（Windows/Linux/macOS 支持）。
    /// </summary>
    Fullscreen,

    /// <summary>
    /// 关闭窗口（Windows/Linux/macOS 支持）。
    /// </summary>
    CloseWindow,

    /// <summary>
    /// 缩放窗口（macOS 专属语义，其他平台等价于 Maximize）。
    /// </summary>
    Zoom,

    // ─── Application 角色 ───

    /// <summary>
    /// 显示关于对话框。需配合 <see cref="MenuItem.AboutMetadata"/>。
    /// </summary>
    About,

    /// <summary>
    /// 退出应用（Windows/Linux/macOS 支持）。
    /// </summary>
    Quit,

    /// <summary>
    /// 隐藏应用窗口（macOS 专属，其他平台 no-op）。
    /// </summary>
    Hide,

    /// <summary>
    /// 隐藏其他应用窗口（macOS 专属，其他平台 no-op）。
    /// </summary>
    HideOthers,

    /// <summary>
    /// 显示所有应用窗口（macOS 专属，其他平台 no-op）。
    /// </summary>
    ShowAll,

    /// <summary>
    /// 系统服务菜单（macOS 专属，其他平台 no-op）。
    /// </summary>
    Services,

    /// <summary>
    /// 把所有窗口带到前台（macOS 专属，其他平台 no-op）。
    /// </summary>
    BringAllToFront,

    /// <summary>
    /// 切换全屏（macOS 专属别名，等价于 <see cref="Fullscreen"/>）。
    /// </summary>
    ToggleFullScreen,
}
