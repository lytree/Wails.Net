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
    /// 构造插件上下文。
    /// </summary>
    /// <param name="services">DI 服务容器。</param>
    /// <param name="commands">命令注册表。</param>
    /// <param name="configuration">配置。</param>
    /// <param name="loggerFactory">日志工厂。</param>
    /// <param name="permissionManager">权限管理器，可为 null；为 null 时使用空权限声明器。</param>
    public PluginContext(
        IServiceCollection services,
        CommandRegistry commands,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        PermissionManager? permissionManager = null)
    {
        Services = services;
        Commands = commands;
        Configuration = configuration;
        LoggerFactory = loggerFactory;
        Permissions = permissionManager is not null
            ? new PermissionRegistrar(permissionManager)
            : NullPermissionRegistrar.Instance;
    }
}
