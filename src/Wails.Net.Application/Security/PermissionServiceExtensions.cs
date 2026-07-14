using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Wails.Net.Application.Security;

/// <summary>
/// DI 扩展方法，注册权限相关服务。
/// </summary>
public static class PermissionServiceExtensions
{
    /// <summary>
    /// 添加权限管理服务。
    /// 从 "Wails:Permissions" 配置节绑定 <see cref="PermissionOptions"/>，
    /// 并注册 <see cref="PermissionManager"/> 为单例。
    /// 对应 AGENTS.md §1.1.1 统一配置节命名：根节为 <c>Wails</c>。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">可选的覆盖配置回调。</param>
    /// <returns>当前服务集合，用于链式调用。</returns>
    public static IServiceCollection AddPermissions(this IServiceCollection services, Action<PermissionOptions>? configure = null)
    {
        services.AddOptions<PermissionOptions>()
            .BindConfiguration("Wails:Permissions");

        if (configure != null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<PermissionManager>();

        return services;
    }

    /// <summary>
    /// 启用权限检查。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>当前服务集合，用于链式调用。</returns>
    public static IServiceCollection EnablePermissions(this IServiceCollection services)
    {
        services.Configure<PermissionOptions>(opts => opts.Enabled = true);
        return services;
    }
}
