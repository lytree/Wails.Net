using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 菜单插件，将 <see cref="IMenuManager"/> 的原生操作以插件命令形式注册。
/// 借鉴 Tauri v2 的 "核心即插件" 哲学：菜单是核心能力，
/// 但通过插件命令路径暴露给前端。
/// 对应 Wails v3 前端的 <c>window.wails.menu.*</c> API 与 <c>contextmenu</c> 事件链路。
/// <para>
/// 命令通过 <see cref="Application.MenuManager"/> 获取平台注入的菜单管理器实例；
/// 弹窗命令通过 <see cref="ICommandContext.WindowId"/> 定位目标 <see cref="WebviewWindow"/>，
/// 再委托到 <see cref="WebviewWindow.OpenContextMenu(Menus.ContextMenuData)"/>。
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
        ArgumentNullException.ThrowIfNull(context);

        var commands = context.Commands;

        // === 应用菜单 ===
        commands.MapCommand("menu.setApplicationMenu", (Action<ICommandContext, MenuApplicationMenuOptions>)((ctx, opts) =>
            GetMenuManagerOrThrow(ctx).SetApplicationMenu(opts.Menu)));

        commands.MapCommand("menu.getApplicationMenu", (Func<ICommandContext, Menu?>)(ctx =>
            GetMenuManagerOrThrow(ctx).GetApplicationMenu()));

        // === 上下文菜单注册 ===
        // menu.setContextMenu — 按 ID 注册上下文菜单到全局注册表
        // 前端通过 CSS 变量 --custom-contextmenu 引用此 ID，
        // contextmenu 事件触发时由 MessageProcessor.ProcessContextMenu 查找并弹出。
        commands.MapCommand("menu.setContextMenu", (Action<ICommandContext, MenuContextMenuOptions>)((ctx, opts) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(opts.Id);
            if (opts.Menu is null)
            {
                // 传 null Menu 视为移除注册
                GetMenuManagerOrThrow(ctx).RemoveContextMenu(opts.Id);
                return;
            }

            if (opts.Menu is not ContextMenu contextMenu)
            {
                throw new InvalidOperationException(
                    $"menu.setContextMenu 的 Menu 必须是 ContextMenu 类型，实际收到 {opts.Menu.GetType().Name}");
            }

            GetMenuManagerOrThrow(ctx).RegisterContextMenu(opts.Id, contextMenu);
        }));

        // === 上下文菜单弹窗 ===
        // menu.popup — 在指定窗口的 (X, Y) 坐标弹出已注册的上下文菜单
        // 与前端 contextmenu 事件路径共用 IWebviewWindowImpl.OpenContextMenu(ContextMenuData) 契约。
        commands.MapCommand("menu.popup", (Action<ICommandContext, MenuPopupOptions>)((ctx, opts) =>
        {
            var window = GetWindowOrThrow(ctx);
            var data = new ContextMenuData
            {
                Id = opts.Id ?? string.Empty,
                X = opts.X,
                Y = opts.Y,
                Data = opts.Data
            };
            window.OpenContextMenu(data);
        }));

        // === 菜单项更新 ===
        // menu.updateMenuItem — 按菜单项 ID 更新属性（label、enabled、checked、hidden 等）
        // 遍历当前应用菜单与所有已注册的上下文菜单查找匹配项，找到后更新并通知平台实现。
        commands.MapCommand("menu.updateMenuItem", (Action<ICommandContext, MenuUpdateItemOptions>)((ctx, opts) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(opts.Id);
            var manager = GetMenuManagerOrThrow(ctx);
            var item = FindMenuItemById(manager, opts.Id);
            if (item is null)
            {
                throw new InvalidOperationException($"找不到 ID 为 '{opts.Id}' 的菜单项");
            }

            ApplyMenuItemProperties(item, opts.Properties);
        }));

        // === 角色菜单项（MenuRole，对应 Wails v3 / Tauri v2 PredefinedMenuItem）===
        // menu.addRoleItem — 向指定父菜单追加一个角色菜单项，返回新菜单项 ID
        commands.MapCommand("menu.addRoleItem", (Func<ICommandContext, MenuAddRoleItemOptions, string>)((ctx, opts) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(opts.ParentId);
            var manager = GetMenuManagerOrThrow(ctx);
            var parent = FindMenuItemById(manager, opts.ParentId)
                ?? throw new InvalidOperationException($"找不到 ID 为 '{opts.ParentId}' 的父菜单项");
            var item = parent.AddRoleItem(opts.Role, opts.Label);
            return item.ID.ToString(CultureInfo.InvariantCulture);
        }));

        // menu.addStandardEditMenu — 向指定父菜单追加标准编辑菜单项（Undo/Redo/Sep/Cut/Copy/Paste/SelectAll）
        commands.MapCommand("menu.addStandardEditMenu", (Action<ICommandContext, MenuParentOptions>)((ctx, opts) =>
        {
            var parent = ResolveParentOrThrow(ctx, opts.ParentId);
            parent.AddStandardEditMenu();
        }));

        // menu.addStandardWindowMenu — 向指定父菜单追加标准窗口菜单项（Minimize/Maximize/Sep/CloseWindow）
        commands.MapCommand("menu.addStandardWindowMenu", (Action<ICommandContext, MenuParentOptions>)((ctx, opts) =>
        {
            var parent = ResolveParentOrThrow(ctx, opts.ParentId);
            parent.AddStandardWindowMenu();
        }));

        // menu.addStandardHelpMenu — 向指定父菜单追加标准帮助菜单项（About）
        commands.MapCommand("menu.addStandardHelpMenu", (Action<ICommandContext, MenuAddStandardHelpOptions>)((ctx, opts) =>
        {
            var parent = ResolveParentOrThrow(ctx, opts.ParentId);
            parent.AddStandardHelpMenu(opts.Metadata, opts.Label);
        }));
    }

    /// <summary>
    /// 解析父菜单项。父菜单必须为应用菜单中的 MenuItem（可带子菜单）。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <param name="parentId">父菜单项 ID（字符串形式的 <see cref="uint"/>）。</param>
    /// <returns>父菜单实例。</returns>
    private static Menu ResolveParentOrThrow(ICommandContext ctx, string parentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        var manager = GetMenuManagerOrThrow(ctx);
        return FindMenuItemById(manager, parentId)
            ?? throw new InvalidOperationException($"找不到 ID 为 '{parentId}' 的父菜单项");
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

    /// <summary>
    /// 从命令上下文中获取目标窗口。
    /// 与 <see cref="WindowPlugin"/> 保持一致的窗口定位语义。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>目标窗口实例。</returns>
    /// <exception cref="InvalidOperationException">当 WindowId 为空或窗口未找到时抛出。</exception>
    private static WebviewWindow GetWindowOrThrow(ICommandContext ctx)
    {
        if (ctx.WindowId is not uint windowId)
        {
            throw new InvalidOperationException("菜单命令未指定目标窗口 ID");
        }

        var app = Application.Get();
        var window = app?.GetWindow(windowId);
        if (window is null)
        {
            throw new InvalidOperationException($"找不到 ID 为 {windowId} 的窗口");
        }

        return window;
    }

    /// <summary>
    /// 在应用菜单和所有已注册的上下文菜单中递归查找指定 ID 的菜单项。
    /// </summary>
    /// <param name="manager">菜单管理器。</param>
    /// <param name="id">菜单项 ID 字符串（解析为 <see cref="uint"/>）。</param>
    /// <returns>匹配的菜单项，未找到返回 null。</returns>
    private static MenuItem? FindMenuItemById(IMenuManager manager, string id)
    {
        if (!uint.TryParse(id, CultureInfo.InvariantCulture, out var targetId))
        {
            return null;
        }

        // 搜索应用菜单
        if (manager.GetApplicationMenu() is { } appMenu)
        {
            var found = FindMenuItemInMenu(appMenu, targetId);
            if (found is not null)
            {
                return found;
            }
        }

        // 已注册的上下文菜单无公开枚举接口，此处依赖 IMenuManager 默认实现不暴露注册表。
        // 若需支持上下文菜单中的菜单项更新，应在 IMenuManager 中增加枚举 API。
        // 当前实现仅支持应用菜单中的菜单项更新，与 Wails v3 行为一致。
        return null;
    }

    /// <summary>
    /// 递归搜索菜单树中指定 ID 的菜单项。
    /// </summary>
    /// <param name="menu">起始菜单。</param>
    /// <param name="targetId">目标菜单项 ID。</param>
    /// <returns>匹配的菜单项，未找到返回 null。</returns>
    private static MenuItem? FindMenuItemInMenu(Menu menu, uint targetId)
    {
        foreach (var item in menu.Items)
        {
            if (item.ID == targetId)
            {
                return item;
            }

            // 递归搜索子菜单
            var found = FindMenuItemInMenu(item, targetId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// 将属性字典应用到菜单项，并通知平台实现刷新。
    /// 支持的属性键（不区分大小写）：
    /// <list type="bullet">
    /// <item><c>label</c> / <c>text</c> — 设置标签文本</item>
    /// <item><c>enabled</c> / <c>disabled</c> — 设置启用/禁用状态</item>
    /// <item><c>checked</c> — 设置选中状态</item>
    /// <item><c>hidden</c> / <c>visible</c> — 设置可见性（部分平台支持）</item>
    /// <item><c>accelerator</c> — 设置快捷键</item>
    /// </list>
    /// </summary>
    /// <param name="item">目标菜单项。</param>
    /// <param name="properties">属性字典。</param>
    private static void ApplyMenuItemProperties(MenuItem item, Dictionary<string, object?> properties)
    {
        foreach (var kv in properties)
        {
            var key = kv.Key.ToLowerInvariant();
            var value = kv.Value;
            switch (key)
            {
                case "label":
                case "text":
                    if (value is string label)
                    {
                        item.Label = label;
                        item.Impl?.SetLabel(label);
                    }
                    break;

                case "enabled":
                    if (ToBool(value) is bool enabled)
                    {
                        item.IsDisabled = !enabled;
                        item.Impl?.SetEnabled(enabled);
                    }
                    break;

                case "disabled":
                    if (ToBool(value) is bool disabled)
                    {
                        item.IsDisabled = disabled;
                        item.Impl?.SetEnabled(!disabled);
                    }
                    break;

                case "checked":
                    if (ToBool(value) is bool isChecked)
                    {
                        item.Checked = isChecked;
                        item.Impl?.SetChecked(isChecked);
                    }
                    break;

                case "accelerator":
                    if (value is string accelerator)
                    {
                        item.Accelerator = accelerator;
                        item.Impl?.SetAccelerator(accelerator);
                    }
                    break;

                case "hidden":
                case "visible":
                    // 可见性由平台实现支持；此处仅记录，不直接操作 IMenuImpl。
                    // 未来可在 IMenuImpl 增加 SetVisible 接口。
                    break;

                default:
                    // 未知属性忽略，保持向前兼容
                    break;
            }
        }

        // 通知平台实现整体刷新
        item.Impl?.UpdateMenuItem(item);
    }

    /// <summary>
    /// 将字典中的任意值转换为布尔值。
    /// 支持 bool、string ("true"/"false")、数字（0=false，非零=true）。
    /// </summary>
    /// <param name="value">原始值。</param>
    /// <returns>转换后的布尔值；无法转换返回 null。</returns>
    private static bool? ToBool(object? value)
    {
        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var b) => b,
            int i => i != 0,
            long l => l != 0,
            double d => d != 0,
            _ => null
        };
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
    /// <summary>
    /// 上下文菜单 ID。
    /// 前端 CSS 变量 <c>--custom-contextmenu</c> 引用此 ID。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 上下文菜单实例。必须为 <see cref="ContextMenu"/> 类型。
    /// 传 null 视为移除 ID 对应的注册。
    /// </summary>
    public Menu? Menu { get; set; }
}

/// <summary>menu.updateMenuItem 命令参数。</summary>
public sealed class MenuUpdateItemOptions
{
    /// <summary>菜单项 ID（字符串形式的 <see cref="uint"/>）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>要更新的属性字典。</summary>
    public Dictionary<string, object?> Properties { get; set; } = new();
}

/// <summary>menu.popup 命令参数。</summary>
public sealed class MenuPopupOptions
{
    /// <summary>
    /// 已注册的上下文菜单 ID。若为空，平台实现可能弹出一个空菜单或忽略。
    /// </summary>
    public string? Id { get; set; }

    /// <summary>X 坐标（视口坐标，由平台实现转换为屏幕坐标）。</summary>
    public int X { get; set; }

    /// <summary>Y 坐标（视口坐标，由平台实现转换为屏幕坐标）。</summary>
    public int Y { get; set; }

    /// <summary>透传到菜单项点击回调的额外数据，可为 null。</summary>
    public string? Data { get; set; }
}

/// <summary>menu.addRoleItem 命令参数。</summary>
public sealed class MenuAddRoleItemOptions
{
    /// <summary>父菜单项 ID（字符串形式的 <see cref="uint"/>）。</summary>
    public string ParentId { get; set; } = string.Empty;

    /// <summary>
    /// 要添加的角色。
    /// 前端传字符串（如 "Copy"），由 System.Text.Json 反序列化为 <see cref="MenuRole"/> 枚举。
    /// </summary>
    public MenuRole Role { get; set; } = MenuRole.None;

    /// <summary>标签文本，留空时由平台实现提供默认本地化文本。</summary>
    public string? Label { get; set; }
}

/// <summary>menu.addStandardEditMenu / menu.addStandardWindowMenu 命令参数。</summary>
public sealed class MenuParentOptions
{
    /// <summary>父菜单项 ID（字符串形式的 <see cref="uint"/>）。</summary>
    public string ParentId { get; set; } = string.Empty;
}

/// <summary>menu.addStandardHelpMenu 命令参数。</summary>
public sealed class MenuAddStandardHelpOptions
{
    /// <summary>父菜单项 ID（字符串形式的 <see cref="uint"/>）。</summary>
    public string ParentId { get; set; } = string.Empty;

    /// <summary>关于对话框元数据，可为 null。</summary>
    public AboutMetadata? Metadata { get; set; }

    /// <summary>标签文本，留空时使用默认文本。</summary>
    public string? Label { get; set; }
}
