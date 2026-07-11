using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 日志插件，提供前端写入日志的命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-log</c>。
/// 通过 <see cref="ICommandContext.Services"/> 从 DI 容器获取 <see cref="LogService"/> 实例。
/// </summary>
public class LogPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "log";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册日志相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("log.debug", (Action<ICommandContext, string>)((ctx, message) =>
        {
            var service = ctx.Services.GetService<LogService>();
            service?.Debug(message);
        }));

        context.Commands.MapCommand("log.info", (Action<ICommandContext, string>)((ctx, message) =>
        {
            var service = ctx.Services.GetService<LogService>();
            service?.Info(message);
        }));

        context.Commands.MapCommand("log.warn", (Action<ICommandContext, string>)((ctx, message) =>
        {
            var service = ctx.Services.GetService<LogService>();
            service?.Warning(message);
        }));

        context.Commands.MapCommand("log.error", (Action<ICommandContext, string>)((ctx, message) =>
        {
            var service = ctx.Services.GetService<LogService>();
            service?.Error(message);
        }));

        // trace 级别映射到 Debug，与 Tauri plugin-log 行为一致
        context.Commands.MapCommand("log.trace", (Action<ICommandContext, string>)((ctx, message) =>
        {
            var service = ctx.Services.GetService<LogService>();
            service?.Debug(message);
        }));

        context.Commands.MapCommand("log.log", (Action<ICommandContext, string, string>)((ctx, level, message) =>
        {
            var service = ctx.Services.GetService<LogService>();
            service?.Log(level, message);
        }));
    }
}
