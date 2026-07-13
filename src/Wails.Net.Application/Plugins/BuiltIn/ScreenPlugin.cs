using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 屏幕插件，将屏幕查询操作以插件命令形式注册。
/// 借鉴 Tauri v2 的 "核心即插件" 哲学：屏幕信息查询是核心能力，
/// 但通过插件命令路径暴露给前端。
/// 对应 Wails v3 前端的 <c>window.wails.screen.*</c> API。
/// <para>
/// 命令通过 <see cref="Application.ScreenManager"/> 或 <see cref="Application.GetScreens()"/>
/// 获取屏幕信息。与 <see cref="ApplicationPlugin"/> 的 <c>application.getScreens</c> 命令互补，
/// 此插件提供更完整的屏幕查询 API（getPrimary/getAll）。
/// </para>
/// </summary>
public class ScreenPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "screen";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务，屏幕信息通过 Application 获取
    }

    /// <summary>
    /// 配置插件，注册所有屏幕查询命令。
    /// 命令名采用 <c>screen.&lt;action&gt;</c> 格式，与前端 <c>wails.screen.&lt;action&gt;</c> API 一致。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        var commands = context.Commands;

        // === 屏幕查询 ===
        commands.MapCommand("screen.getAll", (Func<ICommandContext, Screen[]>)(ctx =>
            GetAppOrThrow(ctx).GetScreens()));

        commands.MapCommand("screen.getPrimary", (Func<ICommandContext, Screen?>)(ctx =>
            GetAppOrThrow(ctx).GetPrimaryScreen()));
    }

    /// <summary>
    /// 从命令上下文中获取全局 <see cref="Application"/> 实例。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>全局应用实例。</returns>
    /// <exception cref="InvalidOperationException">当应用未初始化时抛出。</exception>
    private static Application GetAppOrThrow(ICommandContext ctx)
    {
        var app = Application.Get();
        if (app is null)
        {
            throw new InvalidOperationException("应用未初始化，无法执行屏幕查询命令");
        }

        return app;
    }
}
