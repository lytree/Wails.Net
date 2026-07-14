using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins;

/// <summary>
/// 插件上下文，提供插件配置所需的依赖。
/// </summary>
public interface IPluginContext
{
    /// <summary>DI 服务容器</summary>
    IServiceCollection Services { get; }

    /// <summary>命令注册表</summary>
    CommandRegistry Commands { get; }

    /// <summary>配置</summary>
    IConfiguration Configuration { get; }

    /// <summary>日志工厂</summary>
    ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// 插件权限声明器，用于在 <see cref="IPlugin.Configure"/> 中注册插件自身的权限集和作用域。
    /// 对应 Tauri v2 插件的 permissions 声明机制。
    /// </summary>
    IPermissionRegistrar Permissions { get; }
}
