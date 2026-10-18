namespace Wails.Net.Application.Options;

/// <summary>
/// macOS 平台特定窗口级选项。
/// 对应 Wails v3 Go 版本 <c>WebviewWindowOptions.Mac</c> 结构。
/// 仅在 macOS 平台生效，其他平台忽略。
/// </summary>
public class WebviewWindowMacOptions
{
    /// <summary>
    /// 无边框窗口圆角类型（0=Default、1=Square、2=Custom）。
    /// 对应 Wails v3 Go 版本 <c>MacWindowOptions.CornerType</c>。
    /// </summary>
    public int CornerType { get; set; }

    /// <summary>
    /// 无边框窗口自定义圆角半径。
    /// </summary>
    public double CornerRadius { get; set; }

    /// <summary>
    /// 窗口背景类型（0=Normal、1=Transparent、2=Translucent、3=LiquidGlass）。
    /// 对应 Wails v3 Go 版本 <c>MacWindowOptions.Backdrop</c>。
    /// </summary>
    public int Backdrop { get; set; }

    /// <summary>
    /// 窗口层级（0=Normal、1=Floating、2=TornOffMenu、3=ModalPanel、4=MainMenu、
    /// 5=Status、6=PopUpMenu、7=ScreenSaver）。
    /// 对应 Wails v3 Go 版本 <c>MacWindowLevel</c>。
    /// </summary>
    public int WindowLevel { get; set; }

    /// <summary>
    /// 是否禁用 Escape 键退出全屏。
    /// </summary>
    public bool DisableEscapeExitsFullscreen { get; set; }

    /// <summary>
    /// 是否启用欺诈网站警告。
    /// </summary>
    public bool EnableFraudulentWebsiteWarnings { get; set; }

    /// <summary>
    /// 是否禁用窗口阴影。
    /// </summary>
    public bool DisableShadow { get; set; }

    /// <summary>
    /// 窗口外观名称（如 "NSAppearanceNameDarkAqua"、"NSAppearanceNameAqua"）。
    /// 对应 Wails v3 Go 版本 <c>MacWindowOptions.Appearance</c>。
    /// 为空时跟随系统。
    /// </summary>
    public string? Appearance { get; set; }

    /// <summary>
    /// 隐藏标题栏高度（用于隐形标题栏拖动区域）。
    /// 对应 Wails v3 Go 版本 <c>MacWindowOptions.InvisibleTitleBarHeight</c>。
    /// </summary>
    public uint InvisibleTitleBarHeight { get; set; }

    /// <summary>
    /// 标题栏选项。为 null 时回退到应用级 <see cref="MacOptions.TitleBar"/>。
    /// </summary>
    public MacTitleBarOptions? TitleBar { get; set; }

    /// <summary>
    /// 相机（视频）媒体捕获权限策略。仅在 macOS（WKWebView media-capture）生效。
    /// 对应 Wails v3 Go 版本 <c>WebviewWindowOptions.Permissions</c> 中
    /// <c>PermissionType: PermissionCamera</c> 项；未设置时为 <see cref="Permission.Default"/>。
    /// </summary>
    public Permission CameraPermission { get; set; } = Permission.Default;

    /// <summary>
    /// 麦克风（音频）媒体捕获权限策略。仅在 macOS（WKWebView media-capture）生效。
    /// 对应 Wails v3 Go 版本 <c>WebviewWindowOptions.Permissions</c> 中
    /// <c>PermissionType: PermissionMicrophone</c> 项；未设置时为 <see cref="Permission.Default"/>。
    /// </summary>
    public Permission MicrophonePermission { get; set; } = Permission.Default;
}

/// <summary>
/// 能力请求（相机/麦克风媒体捕获）的权限策略。
/// 对应 Wails v3 Go 版本 <c>webview_window_options.go</c> 中的 <c>Permission</c> 类型：
/// 成员语义与 <c>PermissionDefault</c> / <c>PermissionAllow</c> / <c>PermissionDeny</c> 一一对应。
/// </summary>
public enum Permission
{
    /// <summary>
    /// 使用平台原生处理（对应 Go <c>PermissionDefault</c>）。
    /// macOS 上 WebKit 会弹出其自带的相机/麦克风授权提示。
    /// </summary>
    Default = 0,

    /// <summary>
    /// 无需提示直接授权（对应 Go <c>PermissionAllow</c>）。
    /// </summary>
    Allow = 1,

    /// <summary>
    /// 无需提示直接拒绝（对应 Go <c>PermissionDeny</c>）。
    /// </summary>
    Deny = 2,
}
