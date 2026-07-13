using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 菜单插件，将 <see cref="IMenuManager"/> 的原生操作以插件命令形式注册。
/// 借鉴 Tauri v2 的 "核心即插件" 哲学：菜单是核心能力，
/// 但通过插件命令路径暴露给前端。
/// 对应 Wails v3 前端的 <c>window.wails.menu.*</c> API。
/// <para>
/// 命令通过 <see cref="Application.MenuManager"/> 获取平台注入的菜单管理器实例。
/// </para>
/// </summary>
public class MenuPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "menu";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务，菜单管理器由平台特定代码注入到 Application
    }

    /// <summary>
    /// 配置插件，注册所有菜单操作命令。
    /// 命令名采用 <c>menu.&lt;action&gt;</c> 格式，与前端 <c>wails.menu.&lt;action&gt;</c> API 一致。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        var commands = context.Commands;

        // === 应用菜单 ===
        commands.MapCommand("menu.setApplicationMenu", (Action<ICommandContext, MenuApplicationMenuOptions>)((ctx, opts) =>
            GetMenuManagerOrThrow(ctx).SetApplicationMenu(opts.Menu)));

        commands.MapCommand("menu.getApplicationMenu", (Func<ICommandContext, Menu?>)(ctx =>
            GetMenuManagerOrThrow(ctx).GetApplicationMenu()));

        // === 上下文菜单与弹窗 ===
        // 注意：setContextMenu 和 popup 需要平台特定的窗口上下文支持，
        // 当前实现委托给 Application 的菜单管理器（若平台支持则执行，否则忽略）
        commands.MapCommand("menu.setContextMenu", (Action<ICommandContext, MenuContextMenuOptions>)((ctx, opts) =>
        {
            // 上下文菜单通常绑定到窗口，由 WindowPlugin 的窗口实例处理
            // 此处保留命令注册，平台实现可在 IMenuManager 扩展时支持
        }));

        commands.MapCommand("menu.updateMenuItem", (Action<ICommandContext, MenuUpdateItemOptions>)((ctx, opts) =>
        {
            // 菜单项更新需要平台特定的菜单项 ID 映射
            // 当前为占位实现，保留命令注册供未来扩展
        }));

        commands.MapCommand("menu.popup", (Action<ICommandContext, MenuPopupOptions>)((ctx, opts) =>
        {
            // 弹出菜单需要平台特定的窗口坐标上下文
            // 当前为占位实现，保留命令注册供未来扩展
        }));
    }

    /// <summary>
    /// 从命令上下文中获取菜单管理器。
    /// 通过 <see cref="Application.MenuManager"/> 获取平台注入的管理器实例。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>菜单管理器实例。</returns>
    /// <exception cref="InvalidOperationException">当管理器未注入时抛出。</exception>
    private static IMenuManager GetMenuManagerOrThrow(ICommandContext ctx)
    {
        var app = Application.Get();
        if (app?.MenuManager is not { } manager)
        {
            throw new InvalidOperationException("菜单管理器未注入，无法执行菜单命令");
        }

        return manager;
    }
}

/// <summary>menu.setApplicationMenu 命令参数。</summary>
public sealed class MenuApplicationMenuOptions
{
    /// <summary>菜单实例，可为 null。</summary>
    public Menu? Menu { get; set; }
}

/// <summary>menu.setContextMenu 命令参数。</summary>
public sealed class MenuContextMenuOptions
{
    /// <summary>菜单实例，可为 null。</summary>
    public Menu? Menu { get; set; }
}

/// <summary>menu.updateMenuItem 命令参数。</summary>
public sealed class MenuUpdateItemOptions
{
    /// <summary>菜单项 ID。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>要更新的属性字典。</summary>
    public Dictionary<string, object?> Properties { get; set; } = new();
}

/// <summary>menu.popup 命令参数。</summary>
public sealed class MenuPopupOptions
{
    /// <summary>菜单实例，可为 null。</summary>
    public Menu? Menu { get; set; }

    /// <summary>X 坐标。</summary>
    public int X { get; set; }

    /// <summary>Y 坐标。</summary>
    public int Y { get; set; }
}
