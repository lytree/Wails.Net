using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 全局快捷键插件，提供注册和注销全局快捷键的命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-global-shortcut</c>。
/// 通过 <see cref="Application.KeyBindingManager"/> 管理快捷键绑定，
/// 快捷键触发时通过 <see cref="Application.Events"/> 发布 <c>globalshortcut:pressed</c> 事件。
/// </summary>
public class GlobalShortcutPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "globalshortcut";

    /// <summary>
    /// 已注册的快捷键集合。
    /// </summary>
    private readonly HashSet<string> _registered = new();

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册全局快捷键相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("globalshortcut.register", (Action<ICommandContext, string>)((ctx, accelerator) =>
        {
            var app = Application.Get();
            var manager = app?.KeyBindingManager;
            if (manager is null)
            {
                return;
            }

            _registered.Add(accelerator);
            manager.RegisterKeyBinding(accelerator, () =>
            {
                Application.Get()?.Events.Emit("globalshortcut:pressed", accelerator, null);
            });
        }));

        context.Commands.MapCommand("globalshortcut.unregister", (Action<ICommandContext, string>)((ctx, accelerator) =>
        {
            var app = Application.Get();
            var manager = app?.KeyBindingManager;
            if (manager is null)
            {
                return;
            }

            _registered.Remove(accelerator);
            manager.UnregisterKeyBinding(accelerator);
        }));

        context.Commands.MapCommand("globalshortcut.unregisterAll", (Action<ICommandContext>)(ctx =>
        {
            var app = Application.Get();
            var manager = app?.KeyBindingManager;
            if (manager is null)
            {
                return;
            }

            foreach (var accelerator in _registered)
            {
                manager.UnregisterKeyBinding(accelerator);
            }
            _registered.Clear();
        }));

        context.Commands.MapCommand("globalshortcut.isRegistered", (Func<ICommandContext, string, bool>)((ctx, accelerator) =>
            _registered.Contains(accelerator)));
    }
}
