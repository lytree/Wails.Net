namespace Wails.Net.Application.Hosting;

/// <summary>
/// 桌面应用宿主配置选项。
/// 对应 appsettings.json 中的 "Wails" 节。
/// </summary>
public class DesktopHostOptions
{
    /// <summary>应用名称</summary>
    public string ApplicationName { get; set; } = "Wails.Net Application";

    /// <summary>窗口配置</summary>
    public WindowOptions Window { get; set; } = new();

    /// <summary>
    /// 资源目录（<b>兼容字段</b>，OPT-CONFIG-001）。
    /// <para>
    /// 推荐使用 <see cref="AssetsOptions.RootPath"/>（<c>Wails:Assets:RootPath</c>）替代。
    /// 当 <see cref="AssetsOptions.RootPath"/> 为空时才回退到本字段；
    /// 使用本字段时会在启动时输出一次迁移提示日志。未来主版本可能移除。
    /// </para>
    /// </summary>
    public string AssetsDirectory { get; set; } = "dist";

    /// <summary>开发服务器 URL</summary>
    public string? DevServerUrl { get; set; }

    /// <summary>是否启用单实例</summary>
    public bool SingleInstance { get; set; }

    /// <summary>
    /// 简单权限标识列表（<b>兼容字段</b>，OPT-CONFIG-001）。
    /// <para>
    /// 推荐使用 <c>Wails:Permissions</c> 节的 <see cref="Wails.Net.Application.Security.PermissionOptions"/>
    /// （<c>Enabled</c> / <c>DenyByDefault</c> / <c>CapabilitiesDirectory</c> / <c>Scopes</c>）
    /// 与 capabilities 文件（Tauri v2 风格）进行完整权限治理。
    /// 本字段仅用于向 <see cref="Wails.Net.Application.Security.PermissionManager"/> 提供初始全局授权列表，
    /// 与 <see cref="Wails.Net.Application.Security.PermissionOptions.Permissions"/> 语义一致；未来主版本可能移除。
    /// </para>
    /// </summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>
    /// 静态资源配置。设置 RootPath 后，应用将自动通过 http://wails.localhost/
    /// 提供静态资源服务（仿 Wails v3），无需使用 file:// 协议，避免权限问题。
    /// </summary>
    public AssetsOptions Assets { get; set; } = new();

    /// <summary>
    /// 获取或设置应用配置节（多窗口、安全等）。
    /// 与 <see cref="Window"/> 互补：Window 是默认窗口快捷配置，App.Windows 是多窗口列表。
    /// 对应 Tauri v2 的 app 配置节。
    /// </summary>
    public HostingAppConfig? App { get; set; }

    /// <summary>
    /// 静态资源配置选项。
    /// </summary>
    public class AssetsOptions
    {
        /// <summary>
        /// 静态资源根路径。设置为有效路径后，应用将自动创建 FileAssetServer
        /// 并通过 http://wails.localhost/ 提供资源服务。
        /// 支持相对路径（相对于 AppContext.BaseDirectory）和绝对路径。
        /// </summary>
        public string RootPath { get; set; } = string.Empty;

        /// <summary>
        /// 默认文档名称，当请求路径为目录时自动追加此文件名。
        /// </summary>
        public string DefaultDocument { get; set; } = "index.html";

        /// <summary>
        /// 是否启用 SPA 路由回退。当请求的资源不存在时，回退到 DefaultDocument。
        /// 适用于 Vue/React/Angular 等前端框架的客户端路由。
        /// </summary>
        public bool EnableSpaFallback { get; set; } = true;
    }

    /// <summary>
    /// 窗口配置选项。
    /// </summary>
    public class WindowOptions
    {
        /// <summary>窗口宽度</summary>
        public int Width { get; set; } = 1280;

        /// <summary>窗口高度</summary>
        public int Height { get; set; } = 720;

        /// <summary>窗口标题</summary>
        public string Title { get; set; } = "Wails.Net";

        /// <summary>是否无边框</summary>
        public bool Frameless { get; set; }
    }
}
