namespace Wails.Net.Application.Hosting;

/// <summary>
/// 桌面应用宿主配置选项。
/// 对应 appsettings.json 中的 "Desktop" 节。
/// </summary>
public class DesktopHostOptions
{
    /// <summary>应用名称</summary>
    public string ApplicationName { get; set; } = "Wails.Net Application";

    /// <summary>窗口配置</summary>
    public WindowOptions Window { get; set; } = new();

    /// <summary>资源目录</summary>
    public string AssetsDirectory { get; set; } = "dist";

    /// <summary>开发服务器 URL</summary>
    public string? DevServerUrl { get; set; }

    /// <summary>是否启用单实例</summary>
    public bool SingleInstance { get; set; }

    /// <summary>权限列表</summary>
    public List<string> Permissions { get; set; } = new();

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
