using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 键值存储插件，提供前端持久化键值存储命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-store</c>。
/// 通过 <see cref="ICommandContext.Services"/> 从 DI 容器获取 <see cref="KvStoreService"/> 实例。
/// </summary>
public class StorePlugin : IPlugin
{
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
        context.Commands.MapCommand("store.watch", (Action<ICommandContext, string>)((ctx, key) =>
        {
            var service = ctx.Services.GetService<KvStoreService>();
            if (service is null)
            {
                return;
            }

            // 订阅 OnKeyChanged 事件，过滤指定键的变更并转发到应用事件。
            service.OnKeyChanged += (changedKey, value) =>
            {
                if (changedKey == key)
                {
                    Application.Get()?.Events.Emit(
                        "store:changed", new { key = changedKey, value }, null);
                }
            };
        }));
    }
}
