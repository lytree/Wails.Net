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
    /// 对应 Wails v3 Go 版本 <c>MacWindow.WindowLevel</c>（Go 侧为字符串常量，
    /// 此处以序号表示，平台层映射为 <c>NSWindowLevel</c>）。
    /// </summary>
    public int WindowLevel { get; set; }

    /// <summary>
    /// 原生窗口类：0=NSWindow（标准窗口）、1=NSPanel（辅助面板）。
    /// 对应 Wails v3 Go 版本 <c>MacWindow.WindowClass</c>（<c>MacWindowClass</c>）。
    /// </summary>
    public int WindowClass { get; set; }

    /// <summary>
    /// NSPanel 专属行为，仅在 <see cref="WindowClass"/> 为 1 时生效。
    /// 对应 Wails v3 Go 版本 <c>MacWindow.PanelPreferences</c>。
    /// </summary>
    public MacPanelPreferences? PanelPreferences { get; set; }

    /// <summary>
    /// 跨 Space / 全屏行为位掩码，取值与 <c>NSWindowCollectionBehavior</c> 一致。
    /// 对应 Wails v3 Go 版本 <c>MacWindow.CollectionBehavior</c>（<c>MacWindowCollectionBehavior</c>）。
    /// 例如 <c>CanJoinAllSpaces(1) | FullScreenAuxiliary(256) | Stationary(16) | IgnoresCycle(64)</c>。
    /// </summary>
    public int CollectionBehavior { get; set; }

    /// <summary>
    /// notch 窗口内部配置，由 <see cref="Application.CreateNotchWindow"/> 写入；
    /// 普通窗口为 null。非公开 API，平台层据此启用相机刘海定位与动画。
    /// </summary>
    internal NotchWindowInfo? Notch { get; set; }

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
/// NSPanel 专属行为选项。
/// 对应 Wails v3 Go 版本 <c>MacPanelPreferences</c> 结构。
/// </summary>
public class MacPanelPreferences
{
    /// <summary>
    /// 赋予 NSPanel AppKit 浮动面板行为（<c>floatingPanel</c>）。
    /// 显式设置的 <see cref="WebviewWindowMacOptions.WindowLevel"/> 优先级更高。
    /// </summary>
    public bool FloatingPanel { get; set; }

    /// <summary>
    /// 非激活面板仅在点击的视图需要键盘输入时获取 key 状态
    /// （对应 <c>becomesKeyOnlyIfNeeded</c>）。
    /// </summary>
    public bool BecomesKeyOnlyIfNeeded { get; set; }

    /// <summary>
    /// 应用 <c>NSWindowStyleMaskNonactivatingPanel</c>：显示或聚焦面板时
    /// 不激活应用（保持当前前台应用不变）。
    /// </summary>
    public bool NonActivating { get; set; }

    /// <summary>
    /// 应用 <c>NSWindowStyleMaskUtilityWindow</c>（工具窗口样式）。
    /// </summary>
    public bool UtilityWindow { get; set; }
}

/// <summary>
/// notch 窗口内部配置（不参与公开 API 序列化）。
/// 由 <c>Application.CreateNotchWindow</c> 写入，macOS 平台层读取。
/// </summary>
internal sealed class NotchWindowInfo
{
    /// <summary>
    /// WebView 可用内容宽度（不含原生形状边距），默认 660。
    /// </summary>
    public int ContentWidth { get; init; }

    /// <summary>
    /// WebView 可用内容高度（不含底部边距），默认 92。
    /// </summary>
    public int ContentHeight { get; init; }

    /// <summary>
    /// 是否启用显示/隐藏滑入滑出动画。
    /// </summary>
    public bool Animated { get; init; }

    /// <summary>
    /// 显示动画时长（毫秒，默认 420）；隐藏使用其 2/3。
    /// </summary>
    public int AnimationSpeedMilliseconds { get; init; }

    /// <summary>
    /// 目标显示器 ID（字符串），null 表示主显示器。
    /// </summary>
    public string? ScreenId { get; init; }
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
