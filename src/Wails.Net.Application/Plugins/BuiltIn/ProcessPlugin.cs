using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 进程插件，提供应用退出和重启命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-process</c>。
/// </summary>
public class ProcessPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "process";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册进程相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("process.exit", (Action<ICommandContext, int>)((ctx, exitCode) =>
        {
            Application.Get()?.Quit();
        }));

        context.Commands.MapCommand("process.restart", (Action<ICommandContext>)(ctx =>
        {
            Restart();
        }));

        context.Commands.MapCommand("process.relaunch", (Action<ICommandContext>)(ctx =>
        {
            Restart();
        }));

        // 获取当前进程 ID（与前端 wails.process.getPid API 一致）
        context.Commands.MapCommand("process.getPid", (Func<ICommandContext, int>)(_ => Environment.ProcessId));
    }

    /// <summary>
    /// 重启应用：使用当前进程路径和参数启动新进程，然后退出当前进程。
    /// </summary>
    private static void Restart()
    {
        var path = Environment.ProcessPath;
        var args = Environment.GetCommandLineArgs().Skip(1);
        if (path is not null)
        {
            Process.Start(path, args);
        }
        Application.Get()?.Quit();
    }
}
