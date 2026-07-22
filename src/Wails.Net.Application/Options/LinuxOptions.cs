namespace Wails.Net.Application.Options;

/// <summary>
/// Linux 平台特定应用级选项。
/// 对应 Wails v3 Go 版本 application_options.go 中的 LinuxOptions 结构。
/// 配置 WebKitGTK 运行时行为，全局生效于所有窗口共享的浏览器环境。
/// </summary>
public class LinuxOptions
{
    /// <summary>
    /// WebKitGTK 用户数据目录路径（Cookie、缓存、LocalStorage 等）。
    /// 对应 Wails v3 Go 版本 <c>LinuxOptions.WebviewUserDataPath</c>。
    /// 为 null 或空时使用 WebKitGTK 默认路径。
    /// </summary>
    public string? WebviewUserDataPath { get; set; }

    /// <summary>
    /// WebKitGTK 附加运行时参数列表。
    /// 对应 Wails v3 Go 版本 <c>LinuxOptions.AdditionalBrowserArgs</c>。
    /// 每个参数会通过 <c>webkit_web_context_set_additional_command_line_arguments</c> 注入。
    /// </summary>
    public List<string>? AdditionalBrowserArgs { get; set; }

    /// <summary>
    /// WebKitGTK 启用的特性列表。
    /// 对应 Wails v3 Go 版本 <c>LinuxOptions.EnabledFeatures</c>。
    /// </summary>
    public List<string>? EnabledFeatures { get; set; }

    /// <summary>
    /// WebKitGTK 禁用的特性列表。
    /// 对应 Wails v3 Go 版本 <c>LinuxOptions.DisabledFeatures</c>。
    /// </summary>
    public List<string>? DisabledFeatures { get; set; }

    /// <summary>
    /// GTK 窗口类名。
    /// 对应 Wails v3 Go 版本 <c>LinuxOptions.WndClass</c>。
    /// 默认为 <c>WailsWebviewWindow</c>。
    /// </summary>
    public string? WndClass { get; set; }
}
