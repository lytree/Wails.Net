using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 系统托盘插件，将 <see cref="ISystemTrayManager"/> 的原生操作以插件命令形式注册。
/// 借鉴 Tauri v2 的 "核心即插件" 哲学：系统托盘是核心能力，
/// 但通过插件命令路径暴露给前端。
/// 对应 Wails v3 前端的 <c>window.wails.tray.*</c> API。
/// <para>
/// 托盘采用单实例模型：前端调用 <c>tray.setIcon</c> 等命令时，
/// 操作的是当前活动的托盘实例（由 <see cref="TrayHolder"/> 持有）。
/// 若未创建托盘实例，命令抛出 <see cref="InvalidOperationException"/> 由调度器捕获。
/// </para>
/// </summary>
public class TrayPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "tray";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// 注册 <see cref="TrayHolder"/> 单例用于持有活动托盘实例。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<TrayHolder>();
    }

    /// <summary>
    /// 配置插件，注册所有托盘操作命令。
    /// 命令名采用 <c>tray.&lt;action&gt;</c> 格式，与前端 <c>wails.tray.&lt;action&gt;</c> API 一致。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        var commands = context.Commands;

        // === 托盘属性 ===
        commands.MapCommand("tray.setIcon", (Action<ICommandContext, TrayIconOptions>)((ctx, opts) =>
            GetTrayManagerOrThrow(ctx).SetIcon(GetActiveTrayOrThrow(ctx), opts.IconData)));

        commands.MapCommand("tray.setLabel", (Action<ICommandContext, TrayLabelOptions>)((ctx, opts) =>
            GetTrayManagerOrThrow(ctx).SetLabel(GetActiveTrayOrThrow(ctx), opts.Label)));

        commands.MapCommand("tray.setMenu", (Action<ICommandContext, TrayMenuOptions>)((ctx, opts) =>
            GetTrayManagerOrThrow(ctx).SetMenu(GetActiveTrayOrThrow(ctx), opts.Menu)));

        commands.MapCommand("tray.setTooltip", (Action<ICommandContext, TrayTooltipOptions>)((ctx, opts) =>
            GetTrayManagerOrThrow(ctx).SetTooltip(GetActiveTrayOrThrow(ctx), opts.Tooltip)));

        // === 显示状态 ===
        commands.MapCommand("tray.destroy", (Action<ICommandContext>)(ctx =>
        {
            var manager = GetTrayManagerOrThrow(ctx);
            var tray = GetActiveTrayOrThrow(ctx);
            manager.DestroySystemTray(tray);
            ctx.Services.GetRequiredService<TrayHolder>().ActiveTray = null;
        }));

        commands.MapCommand("tray.show", (Action<ICommandContext>)(ctx =>
            GetTrayManagerOrThrow(ctx).Show(GetActiveTrayOrThrow(ctx))));

        commands.MapCommand("tray.hide", (Action<ICommandContext>)(ctx =>
            GetTrayManagerOrThrow(ctx).Hide(GetActiveTrayOrThrow(ctx))));

        commands.MapCommand("tray.isVisible", (Func<ICommandContext, bool>)(ctx =>
            GetTrayManagerOrThrow(ctx).IsVisible(GetActiveTrayOrThrow(ctx))));
    }

    /// <summary>
    /// 从命令上下文中获取系统托盘管理器。
    /// 通过 <see cref="Application.SystemTrayManager"/> 获取平台注入的管理器实例。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>系统托盘管理器实例。</returns>
    /// <exception cref="InvalidOperationException">当管理器未注入时抛出。</exception>
    private static ISystemTrayManager GetTrayManagerOrThrow(ICommandContext ctx)
    {
        var app = Application.Get();
        if (app?.SystemTrayManager is not { } manager)
        {
            throw new InvalidOperationException("系统托盘管理器未注入，无法执行托盘命令");
        }

        return manager;
    }

    /// <summary>
    /// 获取当前活动的托盘实例。
    /// 从 <see cref="TrayHolder"/> 单例中读取。若未创建托盘，抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>活动托盘实例。</returns>
    /// <exception cref="InvalidOperationException">当托盘未创建时抛出。</exception>
    private static object GetActiveTrayOrThrow(ICommandContext ctx)
    {
        var holder = ctx.Services.GetService<TrayHolder>();
        if (holder?.ActiveTray is not { } tray)
        {
            throw new InvalidOperationException("尚未创建系统托盘，请先通过 Application.SystemTrayManager.CreateSystemTray 创建");
        }

        return tray;
    }
}

/// <summary>
/// 活动托盘实例持有者（DI 单例）。
/// 由创建托盘的代码（如 Application 启动流程或自定义初始化）写入，
/// 由 <see cref="TrayPlugin"/> 命令读取。
/// </summary>
public sealed class TrayHolder
{
    /// <summary>当前活动的系统托盘实例，可为 null。</summary>
    public object? ActiveTray { get; set; }
}

/// <summary>tray.setIcon 命令参数。</summary>
public sealed class TrayIconOptions
{
    /// <summary>图标二进制数据。</summary>
    public byte[]? IconData { get; set; }
}

/// <summary>tray.setLabel 命令参数。</summary>
public sealed class TrayLabelOptions
{
    /// <summary>标签文本。</summary>
    public string Label { get; set; } = string.Empty;
}

/// <summary>tray.setMenu 命令参数。</summary>
public sealed class TrayMenuOptions
{
    /// <summary>菜单实例，可为 null。</summary>
    public Menu? Menu { get; set; }
}

/// <summary>tray.setTooltip 命令参数。</summary>
public sealed class TrayTooltipOptions
{
    /// <summary>提示文本。</summary>
    public string Tooltip { get; set; } = string.Empty;
}
