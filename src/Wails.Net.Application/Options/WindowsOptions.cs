namespace Wails.Net.Application.Options;

/// <summary>
/// Windows 平台特定应用级选项。
/// 对应 Wails v3 Go 版本 application_options.go 中的 WindowsOptions 结构。
/// 配置 WebView2 Runtime 运行时行为，全局生效于所有窗口共享的浏览器环境。
/// </summary>
public class WindowsOptions
{
    /// <summary>
    /// WebView2 用户数据目录路径（Cookie、缓存、LocalStorage 等）。
    /// 对应 Wails v3 Go 版本 <c>WindowsOptions.WebviewUserDataPath</c>。
    /// 为 null 或空时使用默认路径 <c>%APPDATA%\[BinaryName.exe]</c>。
    /// </summary>
    public string? WebviewUserDataPath { get; set; }

    /// <summary>
    /// WebView2 可执行文件目录路径（指向 Microsoft Edge 渲染引擎）。
    /// 对应 Wails v3 Go 版本 <c>WindowsOptions.WebviewBrowserPath</c>。
    /// 为 null 或空时使用系统安装的 WebView2 Runtime。
    /// 用于指定固定版本 WebView2 或自带 runtime 部署。
    /// </summary>
    public string? WebviewBrowserPath { get; set; }

    /// <summary>
    /// WebView2 附加 Chromium 启动参数列表。
    /// 对应 Wails v3 Go 版本 <c>WindowsOptions.AdditionalBrowserArgs</c>。
    /// 每个参数必须含 <c>--</c> 前缀（如 <c>--remote-debugging-port=9222</c>）。
    /// 全局生效于所有窗口共享的浏览器环境。
    /// </summary>
    public List<string>? AdditionalBrowserArgs { get; set; }

    /// <summary>
    /// WebView2 启用的 Chromium 特性列表。
    /// 对应 Wails v3 Go 版本 <c>WindowsOptions.EnabledFeatures</c>。
    /// 会被转换为 <c>--enable-features=</c> 命令行参数注入。
    /// </summary>
    public List<string>? EnabledFeatures { get; set; }

    /// <summary>
    /// WebView2 禁用的 Chromium 特性列表。
    /// 对应 Wails v3 Go 版本 <c>WindowsOptions.DisabledFeatures</c>。
    /// 会被转换为 <c>--disable-features=</c> 命令行参数注入。
    /// </summary>
    public List<string>? DisabledFeatures { get; set; }

    /// <summary>
    /// Win32 窗口类名。
    /// 对应 Wails v3 Go 版本 <c>WindowsOptions.WndClass</c>。
    /// 默认为 <c>WailsWebviewWindow</c>。
    /// 用于自定义窗口类（影响 Shell 自动化、UI Automation 等场景）。
    /// </summary>
    public string? WndClass { get; set; }

    /// <summary>
    /// 是否禁用窗口图标（标题栏、任务栏、Alt+Tab 均不显示）。
    /// 对应 Wails v3 Go 版本 <c>WindowsOptions.DisableIcon</c>。
    /// <para>
    /// 为 true 时：窗口创建后追加扩展样式 <c>WS_EX_DLGMODALFRAME</c>（对齐上游 <c>disableIcon()</c>），
    /// 且应用级 <c>SetIcon</c> 应用图标时临时清除 <c>WS_SYSMENU</c> 再恢复，
    /// 避免 Windows 把图标重新加回标题栏（对齐上游 <c>applyIcon</c> 的规避逻辑）。
    /// 适用于无边框/自定义标题栏应用。
    /// </para>
    /// </summary>
    public bool DisableIcon { get; set; }
}
