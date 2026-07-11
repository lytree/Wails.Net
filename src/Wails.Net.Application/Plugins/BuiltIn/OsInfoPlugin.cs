using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 操作系统信息插件，提供获取系统平台、架构、版本等信息的命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-os</c>。
/// </summary>
public class OsInfoPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "os";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册操作系统信息相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("os.platform", (Func<ICommandContext, string>)(ctx =>
            OperatingSystem.IsWindows() ? "windows" : "linux"));

        context.Commands.MapCommand("os.hostname", (Func<ICommandContext, string>)(ctx =>
            Environment.MachineName));

        context.Commands.MapCommand("os.arch", (Func<ICommandContext, string>)(ctx =>
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()));

        context.Commands.MapCommand("os.locale", (Func<ICommandContext, string>)(ctx =>
            CultureInfo.CurrentCulture.Name));

        context.Commands.MapCommand("os.version", (Func<ICommandContext, string>)(ctx =>
            Environment.OSVersion.Version.ToString()));

        context.Commands.MapCommand("os.type", (Func<ICommandContext, string>)(ctx =>
            OperatingSystem.IsWindows() ? "Windows" : "Linux"));
    }
}
