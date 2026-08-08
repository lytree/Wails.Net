using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;

namespace Wails.Net.Plugins.Appinfo;

/// <summary>
/// Appinfo 插件：桌面通用插件：Windows / Linux / macOS。
/// 对应 docs/development/plugin-packaging.md 的前后端一体双包模型。
/// </summary>
public class AppinfoPlugin : IPlugin
{
    /// <summary>插件名称（命令命名空间前缀）。</summary>
    public string Name => "app";

    /// <summary>注册插件 DI 服务（Host 构建前调用）。</summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 示例：services.AddSingleton<AppinfoService>();
    }

    /// <summary>注册插件命令（Build 阶段调用）。</summary>
    /// <param name="context">插件配置上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 示例：无参命令 app.ping
        context.Commands.MapCommand("app.ping", (Func<ICommandContext, string>)(ctx => "pong"));
    }
}