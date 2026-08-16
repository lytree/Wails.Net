using WailsApplication = Wails.Net.Application.Application;
using Wails.Net.Application.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Plugins.Store.Services;

namespace Wails.Net.Plugins.Store;

/// <summary>
/// 键值存储插件，提供前端持久化键值存储命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-store</c>。
/// 通过 <see cref="ICommandContext.Services"/> 从 DI 容器获取 <see cref="KvStoreService"/> 实例。
/// </summary>
public class StorePlugin : IPlugin
{

    /// <summary>
    /// 全局键监听注册表：每个被订阅的 key 至多挂载一个事件处理器，
    /// 重复调用 <c>store.watch</c> 替换前一次订阅（修复 SEC-H-005 / store.watch 事件泄漏）。
    /// 键为被订阅的键名（不含命名空间前缀），值为事件处理器。
    /// 进程级静态：KvStoreService 是单例，监听器跨窗口共享；
    /// 窗口销毁时调用方需自行调用 <c>store.unwatch</c> 或通过窗口生命周期清理。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Action<string, string?>> _watchers = new();

    /// <summary>插件名称</summary>
    public string Name => "store";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册键值存储相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 声明权限集
        context.Permissions.RegisterPermissionSet("store:default", "键值存储默认权限集",
            "store:allow-get", "store:allow-set", "store:allow-delete");
        context.Permissions.DeclarePermission("store:allow-get", "允许读取键值存储");
        context.Permissions.DeclarePermission("store:allow-set", "允许写入键值存储");
        context.Permissions.DeclarePermission("store:allow-delete", "允许删除键值存储");

        context.Commands.MapCommand("store.get", (Func<ICommandContext, string, string?>)((ctx, key) =>
        {
            var service = ctx.Services.GetService<KvStoreService>();
            return service?.Get(key);
        }));

        context.Commands.MapCommand("store.set", (Action<ICommandContext, string, string>)((ctx, key, value) =>
        {
            var service = ctx.Services.GetService<KvStoreService>();
            service?.Set(key, value);
        }));

        context.Commands.MapCommand("store.delete", (Func<ICommandContext, string, bool>)((ctx, key) =>
        {
            var service = ctx.Services.GetService<KvStoreService>();
            return service?.Delete(key) ?? false;
        }));

        // 检查键是否存在（与前端 wails.store.has API 一致）
        context.Commands.MapCommand("store.has", (Func<ICommandContext, string, bool>)((ctx, key) =>
        {
            var service = ctx.Services.GetService<KvStoreService>();
            return service?.Get(key) is not null;
        }));

        context.Commands.MapCommand("store.keys", (Func<ICommandContext, string[]>)(ctx =>
        {
            var service = ctx.Services.GetService<KvStoreService>();
            return service?.Keys() ?? Array.Empty<string>();
        }));

        context.Commands.MapCommand("store.clear", (Action<ICommandContext>)(ctx =>
        {
            var service = ctx.Services.GetService<KvStoreService>();
            if (service is null)
            {
                return;
            }

            // 删除当前命名空间下的所有键
            foreach (var key in service.Keys())
            {
                service.Delete(key);
            }
        }));

        // 监听键变更：注册回调，当指定键变化时通过事件通知前端。
        // 对应 Tauri plugin-store 的 onKeyChange 功能。
        //
        // SEC-H-005（store.watch 事件泄漏修复）：每个 key 至多挂载一个处理器，
        // 重复调用本命令替换前一次订阅，避免内存泄漏 + 多窗口多触发；
        // 取消订阅使用 store.unwatch（无持有句柄时不抛错，幂等）。
        context.Commands.MapCommand("store.watch", (Action<ICommandContext, string>)((ctx, key) =>
        {
            var service = ctx.Services.GetService<KvStoreService>();
            if (service is null)
            {
                return;
            }

            // 若该 key 已有旧处理器，先解绑（避免多挂载泄漏）
            if (_watchers.TryRemove(key, out var previous))
            {
                service.OnKeyChanged -= previous;
            }

            // 订阅 OnKeyChanged 事件，过滤指定键的变更并转发到应用事件。
            // 注意：handler 必须保持单一引用，才能在 unwatch 时精确解绑（修复前 leak 的根因）。
            Action<string, string?> handler = (changedKey, value) =>
            {
                if (changedKey == key)
                {
                    WailsApplication.Get()?.Events.Emit(
                        "store:changed", new { key = changedKey, value }, null);
                }
            };
            service.OnKeyChanged += handler;
            _watchers[key] = handler;
        }));

        // 取消订阅：与 store.watch 配对。
        // 解绑 KvStoreService.OnKeyChanged 上的处理器并从全局注册表移除。
        // 调用方在窗口销毁或不再需要时调用，避免 store.watch 持续累计导致内存泄漏。
        context.Commands.MapCommand("store.unwatch", (Action<ICommandContext, string>)((ctx, key) =>
        {
            var service = ctx.Services.GetService<KvStoreService>();
            if (service is null)
            {
                return;
            }

            if (_watchers.TryRemove(key, out var handler))
            {
                service.OnKeyChanged -= handler;
            }
            // TryRemove 返回 false 表示无持有句柄（从未 watch 或已 unwatch），幂等不抛错。
        }));
    }
}
