using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 应用信息插件，提供获取应用名称、版本、描述等信息的命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/api/app</c>。
/// </summary>
public class AppInfoPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "app";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册应用信息相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("app.getName", (Func<ICommandContext, string>)(ctx =>
            Application.Get()?.Options.Name ?? string.Empty));

        context.Commands.MapCommand("app.getVersion", (Func<ICommandContext, string>)(ctx =>
            Application.Get()?.Options.Version ?? string.Empty));

        context.Commands.MapCommand("app.getDescription", (Func<ICommandContext, string>)(ctx =>
            Application.Get()?.Options.Description ?? string.Empty));

        context.Commands.MapCommand("app.getTauriVersion", (Func<ICommandContext, string>)(ctx =>
            "wails-net-1.0.0"));
    }
}
