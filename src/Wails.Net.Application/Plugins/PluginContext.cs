using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Plugins;

/// <summary>
/// 插件上下文实现，提供插件配置所需的依赖。
/// </summary>
internal sealed class PluginContext : IPluginContext
{
    /// <summary>DI 服务容器</summary>
    public IServiceCollection Services { get; }

    /// <summary>命令注册表</summary>
    public CommandRegistry Commands { get; }

    /// <summary>配置</summary>
    public IConfiguration Configuration { get; }

    /// <summary>日志工厂</summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// 插件权限声明器。
    /// 当 PermissionManager 可用时返回真实声明器，否则返回 <see cref="NullPermissionRegistrar"/>。
    /// </summary>
    public IPermissionRegistrar Permissions { get; }

    /// <summary>
    /// 进入指定插件的注册作用域。
    /// <para>
    /// 由 <see cref="PluginManager"/> 与 <see cref="Hosting.DesktopApplicationBuilder"/> 在调用
    /// 单个插件的 <c>Configure</c> 前后成对调用，使该插件注册的命令与权限集自动带上归属插件名，
    /// 供 <see cref="Manifests.ManifestGenerator.GeneratePluginsManifest"/> 按插件分组
    /// （插件名与命令/权限前缀不总一致，如 <c>filesystem</c> → <c>fs.*</c>，故必须记录归属）。
    /// </para>
    /// </summary>
    /// <param name="pluginName">插件名；为 null 时清空作用域。</param>
    internal void BeginPlugin(string? pluginName)
    {
        Commands.CurrentPluginScope = pluginName;

        switch (Permissions)
        {
            case DeferredPermissionRegistrar deferred:
                deferred.CurrentPluginScope = pluginName;
                break;
            case PermissionRegistrar registrar:
                registrar.CurrentPluginScope = pluginName;
                break;
            default:
                // NullPermissionRegistrar 等空实现不记录归属。
                break;
        }
    }

    /// <summary>
    /// 构造插件上下文。
    /// </summary>
    /// <param name="services">DI 服务容器。</param>
    /// <param name="commands">命令注册表。</param>
    /// <param name="configuration">配置。</param>
    /// <param name="loggerFactory">日志工厂。</param>
    /// <param name="permissionManager">权限管理器，可为 null；为 null 时使用空权限声明器。</param>
    /// <param name="permissionRegistrar">
    /// 自定义权限声明器（A-2 审计项：延迟收集器 <see cref="DeferredPermissionRegistrar"/>）。
    /// 优先于 <paramref name="permissionManager"/> 派生出的声明器；为 null 时按
    /// <paramref name="permissionManager"/> 是否可用决定使用真实声明器或空声明器。
    /// </param>
    public PluginContext(
        IServiceCollection services,
        CommandRegistry commands,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        PermissionManager? permissionManager = null,
        IPermissionRegistrar? permissionRegistrar = null)
    {
        Services = services;
        Commands = commands;
        Configuration = configuration;
        LoggerFactory = loggerFactory;
        Permissions = permissionRegistrar
            ?? (permissionManager is not null
                ? new PermissionRegistrar(permissionManager)
                : NullPermissionRegistrar.Instance);
    }
}
