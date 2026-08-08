namespace Wails.Net.Application.Options;

/// <summary>
/// macOS 平台特定应用级选项。
/// 对应 Wails v3 Go 版本 <c>application_options.go</c> 中的 MacOptions 结构。
/// 配置 AppKit 应用行为（激活策略、应用菜单终止策略、窗口默认外观等）。
/// 仅在 macOS 平台生效，其他平台忽略。
/// </summary>
public class MacOptions
{
    /// <summary>
    /// 应用激活策略。
    /// 对应 Wails v3 Go 版本 <c>MacOptions.ActivationPolicy</c>。
    /// 0=Regular（常规，Dock 图标）、1=Accessory（无 Dock 图标）、2=Prohibited（无 Dock 图标且不激活）。
    /// 默认 0（Regular）。
    /// </summary>
    public int ActivationPolicy { get; set; } = 0;

    /// <summary>
    /// 最后一个窗口关闭后是否终止应用。
    /// 对应 Wails v3 Go 版本 <c>MacOptions.ApplicationShouldTerminateAfterLastWindowClosed</c>。
    /// 默认 false。
    /// </summary>
    public bool ApplicationShouldTerminateAfterLastWindowClosed { get; set; }

    /// <summary>
    /// 应用菜单（主菜单）配置。为 null 时使用 macOS 默认应用菜单。
    /// </summary>
    public MacTitleBarOptions? TitleBar { get; set; }

    /// <summary>
    /// 是否禁用 Escape 键退出全屏。
    /// 对应 Wails v3 Go 版本 <c>MacOptions.DisableEscapeExitsFullscreen</c>。
    /// </summary>
    public bool DisableEscapeExitsFullscreen { get; set; }

    /// <summary>
    /// 是否启用欺诈网站警告（WKWebView fraudulentWebsiteWarningEnabled）。
    /// 对应 Wails v3 Go 版本 <c>MacOptions.EnableFraudulentWebsiteWarnings</c>。
    /// </summary>
    public bool EnableFraudulentWebsiteWarnings { get; set; }

    /// <summary>
    /// 是否禁用窗口阴影。
    /// 对应 Wails v3 Go 版本 <c>MacOptions.DisableShadow</c>。
    /// </summary>
    public bool DisableShadow { get; set; }

    /// <summary>
    /// 无边框窗口圆角类型。
    /// 对应 Wails v3 Go 版本 <c>MacOptions.CornerType</c>：
    /// 0=Default（AppKit 原生）、1=Square（直角）、2=Custom（使用 <see cref="CornerRadius"/>）。
    /// </summary>
    public int CornerType { get; set; }

    /// <summary>
    /// 无边框窗口自定义圆角半径（CornerType=2 时生效）。
    /// 对应 Wails v3 Go 版本 <c>MacOptions.CornerRadius</c>。
    /// </summary>
    public double CornerRadius { get; set; }

    /// <summary>
    /// 窗口背景类型。
    /// 对应 Wails v3 Go 版本 <c>MacOptions.Backdrop</c>：
    /// 0=Normal、1=Transparent、2=Translucent（NSVisualEffectView）、3=LiquidGlass（macOS 26+）。
    /// </summary>
    public int Backdrop { get; set; }

    /// <summary>
    /// WKWebView 偏好设置。
    /// </summary>
    public MacWebviewPreferences? WebviewPreferences { get; set; }
}

/// <summary>
/// macOS 窗口标题栏选项。
/// 对应 Wails v3 Go 版本 <c>MacTitleBarOptions</c> 结构。
/// </summary>
public class MacTitleBarOptions
{
    /// <summary>
    /// 标题栏是否透明。
    /// 对应 Wails v3 Go 版本 <c>MacTitleBarOptions.AppearsTransparent</c>。
    /// </summary>
    public bool AppearsTransparent { get; set; }

    /// <summary>
    /// 是否隐藏标题栏（移除标题栏区域）。
    /// 对应 Wails v3 Go 版本 <c>MacTitleBarOptions.Hide</c>。
    /// </summary>
    public bool Hide { get; set; }

    /// <summary>
    /// 是否隐藏标题文字。
    /// 对应 Wails v3 Go 版本 <c>MacTitleBarOptions.HideTitle</c>。
    /// </summary>
    public bool HideTitle { get; set; }

    /// <summary>
    /// 内容视图是否扩展到标题栏区域（Full Size Content View）。
    /// 对应 Wails v3 Go 版本 <c>MacTitleBarOptions.FullSizeContent</c>。
    /// </summary>
    public bool FullSizeContent { get; set; }

    /// <summary>
    /// 是否使用工具栏（NSToolbar）。
    /// 对应 Wails v3 Go 版本 <c>MacTitleBarOptions.UseToolbar</c>。
    /// </summary>
    public bool UseToolbar { get; set; }

    /// <summary>
    /// 工具栏样式（NSToolbarStyle）。
    /// 对应 Wails v3 Go 版本 <c>MacTitleBarOptions.ToolbarStyle</c>。
    /// </summary>
    public int ToolbarStyle { get; set; }

    /// <summary>
    /// 全屏时是否显示工具栏。
    /// 对应 Wails v3 Go 版本 <c>MacTitleBarOptions.ShowToolbarWhenFullscreen</c>。
    /// </summary>
    public bool ShowToolbarWhenFullscreen { get; set; }

    /// <summary>
    /// 是否隐藏工具栏分隔线。
    /// 对应 Wails v3 Go 版本 <c>MacTitleBarOptions.HideToolbarSeparator</c>。
    /// </summary>
    public bool HideToolbarSeparator { get; set; }
}

/// <summary>
/// macOS WKWebView 偏好设置。
/// 对应 Wails v3 Go 版本 <c>WebviewPreferences</c> 结构。
/// 属性为 null 表示不修改该偏好（使用 WKWebView 默认值）。
/// </summary>
public class MacWebviewPreferences
{
    /// <summary>
    /// Tab 键是否聚焦链接。
    /// </summary>
    public bool? TabFocusesLinks { get; set; }

    /// <summary>
    /// 是否允许文本交互。
    /// </summary>
    public bool? TextInteractionEnabled { get; set; }

    /// <summary>
    /// 是否允许元素全屏。
    /// </summary>
    public bool? FullscreenEnabled { get; set; }

    /// <summary>
    /// 是否允许返回/前进手势。
    /// </summary>
    public bool? AllowsBackForwardNavigationGestures { get; set; }

    /// <summary>
    /// 是否允许捏合缩放。
    /// </summary>
    public bool? AllowsMagnification { get; set; }

    /// <summary>
    /// 是否允许 AirPlay 媒体播放。
    /// </summary>
    public bool? AllowsAirPlayForMediaPlayback { get; set; }

    /// <summary>
    /// JavaScript 是否可自动打开窗口。
    /// </summary>
    public bool? JavaScriptCanOpenWindowsAutomatically { get; set; }

    /// <summary>
    /// 最小字体大小。
    /// </summary>
    public double? MinimumFontSize { get; set; }

    /// <summary>
    /// 是否允许无用户操作自动播放（媒体类型不需要用户操作）。
    /// </summary>
    public bool? EnableAutoplayWithoutUserAction { get; set; }

    /// <summary>
    /// 用户代理附加应用名（如 "MyApp/1.0"）。
    /// 对应 Wails v3 Go 版本 <c>ApplicationNameForUserAgent</c>，为空时使用 "wails"。
    /// </summary>
    public string? ApplicationNameForUserAgent { get; set; }
}
