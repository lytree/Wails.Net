using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// 桌面应用构建器，提供 Fluent API 配置应用。
/// 封装 <see cref="HostApplicationBuilder"/>，将现有 <see cref="Application"/> 模式适配为 Generic Host 模式。
/// </summary>
public sealed class DesktopApplicationBuilder
{
    private readonly HostApplicationBuilder _hostBuilder;
    private Action<DesktopHostOptions>? _configureOptions;
    private Action<IPlatformApp>? _configurePlatform;

    /// <summary>DI 服务集合</summary>
    public IServiceCollection Services => _hostBuilder.Services;

    /// <summary>配置构建器</summary>
    public ConfigurationManager Configuration => _hostBuilder.Configuration;

    /// <summary>日志构建器</summary>
    public ILoggingBuilder Logging => _hostBuilder.Logging;

    /// <summary>
    /// 内部构造函数，初始化 HostApplicationBuilder 并注册默认服务。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    internal DesktopApplicationBuilder(string[]? args = null)
    {
        _hostBuilder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args ?? [],
            ContentRootPath = AppContext.BaseDirectory,
        });

        ConfigureServices();
    }

    /// <summary>
    /// 注册默认服务，包括 <see cref="DesktopHostOptions"/> 绑定、
    /// Wails.Net 核心管理器和服务、<see cref="Application"/> 工厂单例和 <see cref="DesktopHostedService"/>。
    /// </summary>
    private void ConfigureServices()
    {
        // 注册 DesktopHostOptions，绑定 appsettings.json 的 "Desktop" 节
        Services.AddOptions<DesktopHostOptions>()
            .Bind(Configuration.GetSection("Desktop"));

        // 注册 Wails.Net 核心管理器（EventProcessor、BindingManager、WindowManager 等）和内置服务
        Services.AddWailsCore();

        // 注册 ApplicationOptions 为工厂单例，从 DesktopHostOptions 映射字段
        // 平台应用（如 WindowsPlatformApp）通过构造函数注入此实例
        Services.AddSingleton(sp =>
        {
            var desktopOpts = sp.GetRequiredService<IOptions<DesktopHostOptions>>().Value;
            return new ApplicationOptions
            {
                Name = desktopOpts.ApplicationName,
                SingleInstance = desktopOpts.SingleInstance,
                Frameless = desktopOpts.Window.Frameless,
            };
        });

        // 注册 Application 为工厂单例，复用已注册的 ApplicationOptions
        Services.AddSingleton<Application>(sp =>
        {
            var options = sp.GetRequiredService<ApplicationOptions>();
            return new Application(options);
        });

        // 注册 DesktopHostedService，将 Application 生命周期适配为 IHostedService
        Services.AddHostedService<DesktopHostedService>();
    }

    /// <summary>
    /// 配置桌面应用选项。
    /// </summary>
    /// <param name="configure">配置回调。</param>
    /// <returns>当前构建器实例，用于链式调用。</returns>
    public DesktopApplicationBuilder Configure(Action<DesktopHostOptions> configure)
    {
        _configureOptions = configure;
        return this;
    }

    /// <summary>
    /// 配置平台应用。
    /// </summary>
    /// <param name="configure">平台应用配置回调。</param>
    /// <returns>当前构建器实例，用于链式调用。</returns>
    public DesktopApplicationBuilder ConfigurePlatform(Action<IPlatformApp> configure)
    {
        _configurePlatform = configure;
        return this;
    }

    /// <summary>
    /// 使用指定平台实现。
    /// </summary>
    /// <typeparam name="TPlatform">平台应用实现类型。</typeparam>
    /// <returns>当前构建器实例，用于链式调用。</returns>
    public DesktopApplicationBuilder UsePlatform<TPlatform>() where TPlatform : class, IPlatformApp
    {
        Services.AddSingleton<IPlatformApp, TPlatform>();
        return this;
    }

    /// <summary>
    /// 构建桌面应用实例。
    /// </summary>
    /// <returns>构建完成的 <see cref="DesktopApplication"/> 实例。</returns>
    public DesktopApplication Build()
    {
        // 应用选项配置回调
        if (_configureOptions is not null)
        {
            Services.Configure(_configureOptions);
        }

        // 构建 Host
        var host = _hostBuilder.Build();

        // 获取配置选项和 Application 单例
        var desktopOpts = host.Services.GetRequiredService<IOptions<DesktopHostOptions>>().Value;
        var application = host.Services.GetRequiredService<Application>();

        // 设置平台应用（若已注册）
        var platformApp = host.Services.GetService<IPlatformApp>();
        if (platformApp is not null)
        {
            application.SetPlatformApp(platformApp);
            _configurePlatform?.Invoke(platformApp);
        }

        // 从 DI 容器初始化 Application 的管理器，替换 DI 已注册的实例
        application.InitializeFromServiceProvider(host.Services);

        return new DesktopApplication(host, application, desktopOpts.ApplicationName);
    }

    /// <summary>
    /// 创建构建器实例。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>新的 <see cref="DesktopApplicationBuilder"/> 实例。</returns>
    public static DesktopApplicationBuilder CreateBuilder(string[]? args = null)
    {
        return new DesktopApplicationBuilder(args);
    }
}
