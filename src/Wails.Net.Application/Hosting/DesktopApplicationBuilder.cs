using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Security;
using Wails.Net.AssetServer;

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

    /// <summary>
    /// 已注册的插件列表，在 <see cref="Build"/> 时统一初始化。
    /// 由 <see cref="PluginBuilderExtensions.UsePlugin"/> 添加。
    /// </summary>
    private readonly List<IPlugin> _plugins = new();

    /// <summary>
    /// 命令注册表，在 <see cref="Build"/> 时传入插件上下文，由插件通过 MapCommand 注册命令。
    /// </summary>
    private readonly CommandRegistry _commandRegistry = new();

    /// <summary>DI 服务集合</summary>
    public IServiceCollection Services => _hostBuilder.Services;

    /// <summary>配置构建器</summary>
    public ConfigurationManager Configuration => _hostBuilder.Configuration;

    /// <summary>日志构建器</summary>
    public ILoggingBuilder Logging => _hostBuilder.Logging;

    /// <summary>
    /// 添加插件实例到跟踪列表。由 <see cref="PluginBuilderExtensions.UsePlugin"/> 调用。
    /// </summary>
    /// <param name="plugin">插件实例。</param>
    internal void AddPlugin(IPlugin plugin) => _plugins.Add(plugin);

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
        // 注册 DesktopHostOptions，绑定 appsettings.json 的 "Wails" 节。
        // 对应 AGENTS.md §1.1.1 统一配置节命名：根节为 "Wails"。
        Services.AddOptions<DesktopHostOptions>()
            .Bind(Configuration.GetSection("Wails"));

        // 注册 Wails.Net 核心管理器（EventProcessor、BindingManager、WindowManager 等）和内置服务
        Services.AddWailsCore();

        // 注册 ApplicationOptions 为工厂单例，从 IOptionsMonitor<DesktopHostOptions> 映射字段。
        // 使用 IOptionsMonitor<T> 而非 IOptions<T>，支持配置热重载（OnChange 通知）。
        // 对应 AGENTS.md §1.1.1 技术选型：配置使用 Microsoft.Extensions.Options 全栈。
        Services.AddSingleton(sp =>
        {
            var desktopOpts = sp.GetRequiredService<IOptionsMonitor<DesktopHostOptions>>().CurrentValue;
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

        // 注册 PluginHostedServiceAdapter，将插件生命周期适配到 IHostedService
        // 对应 AGENTS.md §1.1.1：Host/DI/Config/Logging → ASP.NET Core
        // PluginManager 内部已使用 Interlocked 保护，与 Application.Run 手动调用共存安全
        Services.AddHostedService<PluginHostedServiceAdapter>();
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
    /// 在构建 Host 之前初始化所有插件（调用 <see cref="IPlugin.Configure"/>），
    /// 在构建 Host 之后创建 <see cref="CommandDispatcher"/> 并注入到 <see cref="Application"/>。
    /// </summary>
    /// <returns>构建完成的 <see cref="DesktopApplication"/> 实例。</returns>
    public DesktopApplication Build()
    {
        // 应用选项配置回调
        if (_configureOptions is not null)
        {
            Services.Configure(_configureOptions);
        }

        // 在构建 Host 之前初始化插件：
        // 此时 IServiceCollection 仍可用，插件可通过 ConfigureServices 注册 DI 服务，
        // 通过 PluginContext.Commands 注册命令（MapCommand 方式）。
        InitializePlugins();

        // 将 CommandRegistry 注册为单例，供 CommandDispatcher 使用
        Services.AddSingleton(_commandRegistry);

        // 构建 Host
        var host = _hostBuilder.Build();

        // 获取配置选项和 Application 单例。
        // 使用 IOptionsMonitor<T>.CurrentValue 而非 IOptions<T>.Value，
        // 确保读取到最新的配置值（支持热重载）。
        var desktopOpts = host.Services.GetRequiredService<IOptionsMonitor<DesktopHostOptions>>().CurrentValue;
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

        // 创建 CommandDispatcher 并注入到 Application
        // CommandDispatcher 需要 CommandRegistry（已由插件填充）和 IServiceProvider（已构建）
        // 自动从 DI 获取 PermissionManager（已由 AddWailsManagers 默认注册，Enabled 默认 false）
        // 对应主题 C：将权限校验接入命令调度链。
        var commandDispatcher = new CommandDispatcher(
            _commandRegistry,
            host.Services,
            host.Services.GetService<ILogger<CommandDispatcher>>(),
            host.Services.GetService<PermissionManager>());
        application.CommandDispatcher = commandDispatcher;

        // 自动创建 AssetServer（仿 Wails v3 静态资源处理）
        // 当配置了 Assets.RootPath 时，创建 FileAssetServer 并设置到 Application.AssetServer
        // 窗口将自动导航到 http://wails.localhost/ 加载前端资源
        if (!string.IsNullOrWhiteSpace(desktopOpts.Assets.RootPath))
        {
            var rootPath = Path.IsPathRooted(desktopOpts.Assets.RootPath)
                ? desktopOpts.Assets.RootPath
                : Path.Combine(AppContext.BaseDirectory, desktopOpts.Assets.RootPath);

            if (Directory.Exists(rootPath))
            {
                var fileAssetServer = new FileAssetServer(
                    rootPath,
                    enableSpaFallback: desktopOpts.Assets.EnableSpaFallback,
                    defaultDocument: desktopOpts.Assets.DefaultDocument);
                application.AssetServer = fileAssetServer;
            }
        }

        // 加载能力文件（Tauri v2 风格的 capabilities/*.json）
        // 必须在窗口创建前完成，确保权限已就绪
        LoadCapabilities(desktopOpts, host.Services);

        // 根据配置创建窗口（多窗口或默认窗口）
        // 在 AssetServer 之后创建，确保窗口导航时资源服务已就绪
        CreateWindowsFromConfig(desktopOpts, application);

        return new DesktopApplication(host, application, desktopOpts.ApplicationName);
    }

    /// <summary>
    /// 根据 HostingAppConfig.Windows 配置创建窗口。
    /// 对应 Tauri v2 的 static windows 配置创建流程。
    /// 优先级：App.Windows 多窗口列表 &gt; Window 默认窗口 &gt; 不创建。
    /// </summary>
    /// <param name="options">宿主配置选项。</param>
    /// <param name="application">Application 实例，用于创建窗口。</param>
    private static void CreateWindowsFromConfig(DesktopHostOptions options, Application application)
    {
        if (options.App?.Windows is { Count: > 0 } windows)
        {
            // 多窗口配置：依次创建每个窗口（第一个为主窗口）
            foreach (var cfg in windows)
            {
                var windowOptions = new WebviewWindowOptions
                {
                    Name = cfg.Name ?? string.Empty,
                    Title = cfg.Title ?? "Wails.Net",
                    Width = cfg.Width,
                    Height = cfg.Height,
                    Resizable = cfg.Resizable,
                    Centered = cfg.Centered,
                    Fullscreen = cfg.Fullscreen,
                    Frameless = cfg.Frameless,
                    AlwaysOnTop = cfg.AlwaysOnTop,
                    URL = cfg.Url ?? string.Empty,
                };
                application.CreateWebviewWindow(windowOptions);
            }
        }
        else if (options.Window is not null)
        {
            // 回退：使用默认窗口配置（向后兼容）
            var window = options.Window;
            application.CreateWebviewWindow(new WebviewWindowOptions
            {
                Title = window.Title,
                Width = window.Width,
                Height = window.Height,
                Frameless = window.Frameless,
            });
        }
        // 两者皆空时不创建窗口（由平台或调用方负责）
    }

    /// <summary>
    /// 从 capabilities 目录加载能力文件并注册到权限管理器。
    /// 对应 Tauri v2 的 capabilities 自动加载机制。
    /// 加载失败不抛异常，仅记录 warning 日志（避免生产环境因目录缺失崩溃）。
    /// </summary>
    /// <param name="options">宿主配置选项。</param>
    /// <param name="services">已构建的 DI 服务提供者。</param>
    private static void LoadCapabilities(DesktopHostOptions options, IServiceProvider services)
    {
        var capabilitiesDir = options.App?.Security?.CapabilitiesDir;
        if (string.IsNullOrEmpty(capabilitiesDir))
        {
            capabilitiesDir = Path.Combine(AppContext.BaseDirectory, "capabilities");
        }
        else if (!Path.IsPathRooted(capabilitiesDir))
        {
            capabilitiesDir = Path.Combine(AppContext.BaseDirectory, capabilitiesDir);
        }

        var loggerFactory = services.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger<DesktopApplicationBuilder>();
        var permissionManager = services.GetService<PermissionManager>();

        if (permissionManager is null)
        {
            logger?.LogDebug("PermissionManager 未注册，跳过能力文件加载。");
            return;
        }

        if (!Directory.Exists(capabilitiesDir))
        {
            logger?.LogDebug("能力文件目录不存在，跳过加载: {Dir}", capabilitiesDir);
            return;
        }

        try
        {
            var capabilities = CapabilityFileLoader.LoadFromDirectory(capabilitiesDir, logger);
            CapabilityFileLoader.RegisterToManager(permissionManager, capabilities);
            logger?.LogInformation("已加载 {Count} 个能力文件", capabilities.Count);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "加载能力文件失败，继续启动");
        }
    }

    /// <summary>
    /// 初始化所有已注册的插件。
    /// 对每个插件依次调用 <see cref="IPlugin.Configure(IPluginContext)"/>，
    /// 使插件可以通过 <see cref="IPluginContext.Commands"/> 注册命令，
    /// 通过 <see cref="IPluginContext.Permissions"/> 声明权限集和作用域。
    /// </summary>
    private void InitializePlugins()
    {
        if (_plugins.Count == 0)
        {
            return;
        }

        // 注意：此时尚未构建 Host，无法从 IServiceProvider 获取 LoggerFactory。
        // 构建临时 ServiceProvider 仅用于获取已注册的 LoggerFactory 和 PermissionManager，
        // 不影响后续 Host 构建。使用 NullLoggerFactory.Instance 作为兜底。
        var tempProvider = _hostBuilder.Services.BuildServiceProvider();
        var loggerFactory = tempProvider.GetService<ILoggerFactory>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var permissionManager = tempProvider.GetService<PermissionManager>();

        // 将 PermissionManager 实例重新注册为单例实例（替换类型注册），
        // 确保后续 Host 构建时复用同一实例，使插件注册的权限在运行时可见。
        if (permissionManager is not null)
        {
            Services.AddSingleton(permissionManager);
        }

        var context = new PluginContext(
            _hostBuilder.Services,
            _commandRegistry,
            _hostBuilder.Configuration,
            loggerFactory,
            permissionManager);

        foreach (var plugin in _plugins)
        {
            plugin.Configure(context);
        }

        // 初始化 Scope 绑定：从 PermissionOptions.Scopes 配置创建 IScope 实例并绑定到权限。
        // 对应 Tauri v2 的 Scope 配置加载：appsettings.json 中的路径/URL 白名单转换为运行时约束。
        if (permissionManager is not null)
        {
            var permOptions = tempProvider.GetService<IOptions<PermissionOptions>>()?.Value;
            if (permOptions is not null)
            {
                ScopeInitializer.Initialize(permissionManager, permOptions, loggerFactory.CreateLogger("ScopeInitializer"));
            }
        }
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
