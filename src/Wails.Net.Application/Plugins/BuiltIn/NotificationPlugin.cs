using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 通知插件，提供系统通知命令。
/// 通过 <see cref="ICommandContext.Services"/> 从 DI 容器获取 <see cref="NotificationService"/> 实例。
/// </summary>
public class NotificationPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "notification";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册通知相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("notification.show", (Action<ICommandContext, string, string>)((ctx, title, body) =>
        {
            var service = ctx.Services.GetService<NotificationService>();
            service?.SendNotification(title, body);
        }));

        // 请求通知权限。简化实现下始终返回 true（已授权）。
        context.Commands.MapCommand("notification.requestPermission", (Func<ICommandContext, bool>)(ctx => true));

        // 查询通知权限是否已授予。简化实现下始终返回 true。
        context.Commands.MapCommand("notification.isPermissionGranted", (Func<ICommandContext, bool>)(ctx => true));

        // 取消指定 ID 的通知，返回是否取消成功。
        context.Commands.MapCommand("notification.cancel", (Func<ICommandContext, string, bool>)((ctx, id) =>
        {
            var service = ctx.Services.GetService<NotificationService>();
            return service?.CancelNotification(id) ?? false;
        }));

        // 显示通知并返回通知 ID，可用于后续取消操作。
        context.Commands.MapCommand("notification.showWithId", (Func<ICommandContext, string, string, string>)((ctx, title, body) =>
        {
            var service = ctx.Services.GetService<NotificationService>();
            return service?.ShowNotification(title, body, null) ?? string.Empty;
        }));
    }
}
