using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 路径插件，提供获取各种系统目录路径的命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/api/path</c>。
/// </summary>
public class PathPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "path";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册路径相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("path.appDataDir", (Func<ICommandContext, string>)(ctx =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName)));

        context.Commands.MapCommand("path.appConfigDir", (Func<ICommandContext, string>)(ctx =>
            OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", AppName)));

        context.Commands.MapCommand("path.appLogDir", (Func<ICommandContext, string>)(ctx =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName, "logs")));

        context.Commands.MapCommand("path.appCacheDir", (Func<ICommandContext, string>)(ctx =>
            Path.Combine(Path.GetTempPath(), AppName)));

        context.Commands.MapCommand("path.downloadDir", (Func<ICommandContext, string>)(ctx =>
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloads = Path.Combine(profile, "Downloads");
            return Directory.Exists(downloads) ? downloads : profile;
        }));

        context.Commands.MapCommand("path.documentDir", (Func<ICommandContext, string>)(ctx =>
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));

        context.Commands.MapCommand("path.homeDir", (Func<ICommandContext, string>)(ctx =>
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

        context.Commands.MapCommand("path.tempDir", (Func<ICommandContext, string>)(ctx =>
            Path.GetTempPath()));

        context.Commands.MapCommand("path.configDir", (Func<ICommandContext, string>)(ctx =>
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)));

        context.Commands.MapCommand("path.dataDir", (Func<ICommandContext, string>)(ctx =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName)));

        context.Commands.MapCommand("path.runtimeDir", (Func<ICommandContext, string>)(ctx =>
            Path.GetTempPath()));
    }

    /// <summary>
    /// 获取当前应用名称，用于构建应用专属目录路径。
    /// </summary>
    private static string AppName => Application.Get()?.Options.Name ?? "WailsNet";
}
