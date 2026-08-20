using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Wails.Net.Application.Security;

/// <summary>
/// DI 扩展方法，注册权限相关服务。
/// 注意：<see cref="Hosting.ServiceCollectionExtensions.AddWailsManagers"/> 已默认注册
/// <see cref="PermissionManager"/> 和 <see cref="PermissionOptions"/> 配置绑定（Enabled 默认 false）。
/// 本扩展方法提供额外的配置覆盖入口。
/// </summary>
public static class PermissionServiceExtensions
{
    /// <summary>
    /// 添加权限管理服务并覆盖配置。
    /// 从 "Wails:Permissions" 配置节绑定 <see cref="PermissionOptions"/>，
    /// 并应用 <paramref name="configure"/> 回调覆盖字段。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">可选的覆盖配置回调。</param>
    /// <returns>当前服务集合，用于链式调用。</returns>
    public static IServiceCollection AddPermissions(this IServiceCollection services, Action<PermissionOptions>? configure = null)
    {
        // PermissionOptions 配置绑定和 PermissionManager 已由 AddWailsManagers 默认注册，
        // 此处仅应用用户提供的配置覆盖。
        if (configure != null)
        {
            services.Configure(configure);
        }

        return services;
    }

    /// <summary>
    /// 启用权限检查（设置 <see cref="PermissionOptions.Enabled"/> = true）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>当前服务集合，用于链式调用。</returns>
    public static IServiceCollection EnablePermissions(this IServiceCollection services)
    {
        services.Configure<PermissionOptions>(opts => opts.Enabled = true);
        return services;
    }
    /// <summary>
    /// 临时回退到全局 Compatibility 宽松模式（R3-E；仅作 v2.x → v3.0 升级过渡）。
    /// <para>
    /// 通过设置 PermissionOptions.CompatibilityByDefault = true，
    /// 使后续构造的 PermissionOptions 实例的 Mode 默认值为 Compatibility（临时回退）。
    /// 调用方应在 builder 构造（如 CreateBuilder）之后立即调用本方法，
    /// 确保 DI 容器实例化 PermissionOptions 时已生效。
    /// </para>
    /// <para>
    /// R3-E 起，库默认已为 Strict。本方法仅作"临时回退"入口（用于 v2.x → v3.0 升级期未完成 capability 配套的项目）：
    /// 调用本方法会让应用以 Compatibility 模式启动（**仅建议短期过渡**，生产环境应配套 capability JSON 后移除此调用）：
    /// 可与 DenyByDefault=false 配合实现"未显式撤销则放行"的宽松语义。
    /// </para>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>当前服务集合，用于链式调用。</returns>
    public static IServiceCollection EnableCompatibilityByDefault(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        PermissionOptions.CompatibilityByDefault = true;
        return services;
    }
}