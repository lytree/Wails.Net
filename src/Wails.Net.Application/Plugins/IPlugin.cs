using Microsoft.Extensions.DependencyInjection;

namespace Wails.Net.Application.Plugins;

/// <summary>
/// 插件接口。
/// 借鉴 Tauri v2 的插件设计，提供 ASP.NET Core 风格的服务注册和配置。
/// </summary>
public interface IPlugin
{
    /// <summary>插件名称</summary>
    string Name { get; }

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>
    /// 配置插件（注册命令、事件等）。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    void Configure(IPluginContext context);
}
