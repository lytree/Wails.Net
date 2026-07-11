using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Commands;

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
    /// 构造插件上下文。
    /// </summary>
    /// <param name="services">DI 服务容器。</param>
    /// <param name="commands">命令注册表。</param>
    /// <param name="configuration">配置。</param>
    /// <param name="loggerFactory">日志工厂。</param>
    public PluginContext(
        IServiceCollection services,
        CommandRegistry commands,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        Services = services;
        Commands = commands;
        Configuration = configuration;
        LoggerFactory = loggerFactory;
    }
}
