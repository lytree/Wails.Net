using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.Mobile;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wails.Net.Application.Commands;

namespace Wails.Net.Plugins.Mobile;

/// <summary>
/// 运行时权限插件，提供权限检查与请求命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-permissions</c>。
/// <para>
/// 命令通过 <see cref="IPlatformPermissions"/> 抽象接口委托到平台实现。
/// Server 模式 / 桌面平台下降级为 <see cref="NullPermissionsImpl"/>（Check 返回 <c>granted</c>，Request 全部返回 <c>granted</c>）。
/// </para>
/// </summary>
public class PermissionsPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "permissions";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// 注册 <see cref="IPlatformPermissions"/> 的默认降级实现 <see cref="NullPermissionsImpl"/>。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPlatformPermissions, NullPermissionsImpl>();
    }

    /// <summary>
    /// 配置插件，注册权限相关命令。
    /// 命令名采用 <c>permissions.&lt;action&gt;</c> 格式，对齐 Tauri v2 命名约定。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Permissions.RegisterPermissionSet("permissions:default", "权限插件默认权限集",
            "permissions:allow-check", "permissions:allow-request");
        context.Permissions.DeclarePermission("permissions:allow-check", "允许检查权限状态");
        context.Permissions.DeclarePermission("permissions:allow-request", "允许请求权限授权");

        var commands = context.Commands;

        // 检查权限状态
        commands.MapCommand("permissions.check",
            (Func<ICommandContext, string, Task<string>>)((ctx, permission) =>
                ResolvePermissions(ctx).CheckAsync(permission)));

        // 请求一组权限（CancellationToken 由 ICommandContext 提供，不作为 JSON 参数暴露给前端）
        commands.MapCommand("permissions.request",
            (Func<ICommandContext, string[], Task<PermissionRequestResult[]>>)((ctx, perms) =>
                ResolvePermissions(ctx).RequestAsync(perms, ctx.CancellationToken)));
    }

    /// <summary>
    /// 从命令上下文的服务容器解析 <see cref="IPlatformPermissions"/>。
    /// </summary>
    /// <param name="ctx">命令上下文。</param>
    /// <returns>平台权限实现实例。</returns>
    private static IPlatformPermissions ResolvePermissions(ICommandContext ctx)
    {
        return ctx.Services.GetService(typeof(IPlatformPermissions)) as IPlatformPermissions
            ?? NullPermissionsImpl.Instance;
    }

    /// <summary>
    /// 空实现的权限管理器，作为 Server 模式 / 桌面平台的降级实现。
    /// Check 返回 <c>granted</c>，Request 全部返回 <c>granted</c>（桌面平台无运行时权限概念）。
    /// </summary>
    private sealed class NullPermissionsImpl : IPlatformPermissions
    {
        /// <summary>单例实例。</summary>
        public static readonly NullPermissionsImpl Instance = new();

        public Task<string> CheckAsync(string permission)
        {
            // 降级：桌面平台默认全部授权
            return Task.FromResult("granted");
        }

        public Task<PermissionRequestResult[]> RequestAsync(string[] permissions, CancellationToken cancellationToken)
        {
            // 降级：所有权限视为已授权
            var results = permissions.Select(p => new PermissionRequestResult
            {
                Permission = p,
                State = "granted",
            }).ToArray();

            return Task.FromResult(results);
        }
    }
}
