using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 应用插件，将 <see cref="Application"/> 的应用级原生操作以插件命令形式注册。
/// 借鉴 Tauri v2 的 "核心即插件" 哲学：应用控制（显示/隐藏/退出/主题/屏幕查询等）是核心能力，
/// 但通过插件命令路径暴露给前端。
/// 对应 Wails v3 前端的 <c>window.wails.application.*</c> API。
/// <para>
/// 命令通过 <see cref="Application.Get()"/> 获取全局应用实例，
/// 再委托到 <see cref="Application"/> 的公共方法。
/// 支持两种前端调用形式：
/// <list type="bullet">
/// <item><c>wails.application.hide()</c> — 走 MessageProcessor → CommandDispatcher 回退</item>
/// <item><c>wails.call('application.hide', [])</c> — 走 ProcessCallAsync → CommandDispatcher 回退</item>
/// </list>
/// </para>
/// </summary>
public class ApplicationPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "application";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务，Application 实例由全局静态 Application.Get() 获取
    }

    /// <summary>
    /// 配置插件，注册所有应用级操作命令。
    /// 命令名采用 <c>application.&lt;action&gt;</c> 格式，与前端 <c>wails.application.&lt;action&gt;</c> API 一致。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        var commands = context.Commands;

        // === 应用生命周期 ===
        commands.MapCommand("application.quit", (Action<ICommandContext>)(ctx =>
            GetAppOrThrow(ctx).Shutdown()));

        commands.MapCommand("application.hide", (Action<ICommandContext>)(ctx =>
            GetAppOrThrow(ctx).Hide()));

        commands.MapCommand("application.show", (Action<ICommandContext>)(ctx =>
            GetAppOrThrow(ctx).Show()));

        // === 应用信息 ===
        commands.MapCommand("application.getName", (Func<ICommandContext, string>)(ctx =>
            GetAppOrThrow(ctx).Options.Name));

        commands.MapCommand("application.getVersion", (Func<ICommandContext, string>)(ctx =>
            GetAppOrThrow(ctx).Options.Version));

        commands.MapCommand("application.getDescription", (Func<ICommandContext, string>)(ctx =>
            GetAppOrThrow(ctx).Options.Description));

        // === 图标 ===
        commands.MapCommand("application.setIcon", (Action<ICommandContext, ApplicationIconOptions>)((ctx, opts) =>
            GetAppOrThrow(ctx).SetIcon(opts.IconData)));

        // === 主题 ===
        commands.MapCommand("application.isDarkMode", (Func<ICommandContext, bool>)(ctx =>
            GetAppOrThrow(ctx).IsDarkMode()));

        commands.MapCommand("application.getAccentColor", (Func<ICommandContext, string>)(ctx =>
            GetAppOrThrow(ctx).GetAccentColor()));

        // === 屏幕 ===
        commands.MapCommand("application.getPrimaryScreen", (Func<ICommandContext, Screen?>)(ctx =>
            GetAppOrThrow(ctx).GetPrimaryScreen()));

        commands.MapCommand("application.getScreens", (Func<ICommandContext, Screen[]>)(ctx =>
            GetAppOrThrow(ctx).GetScreens()));

        // === 关于对话框 ===
        commands.MapCommand("application.showAboutDialog", (Action<ICommandContext>)(ctx =>
            GetAppOrThrow(ctx).ShowAboutDialog()));
    }

    /// <summary>
    /// 从命令上下文中获取全局 <see cref="Application"/> 实例。
    /// 通过 <see cref="Application.Get()"/> 获取静态单例。
    /// 若应用未初始化，抛出 <see cref="InvalidOperationException"/>，
    /// 由 <see cref="CommandDispatcher"/> 捕获并返回错误响应。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>全局应用实例。</returns>
    /// <exception cref="InvalidOperationException">当应用未初始化时抛出。</exception>
    private static Application GetAppOrThrow(ICommandContext ctx)
    {
        var app = Application.Get();
        if (app is null)
        {
            throw new InvalidOperationException("应用未初始化，无法执行应用级命令");
        }

        return app;
    }
}

/// <summary>application.setIcon 命令参数。</summary>
public sealed class ApplicationIconOptions
{
    /// <summary>图标二进制数据（字节序列），可为 null。</summary>
    public byte[]? IconData { get; set; }
}
