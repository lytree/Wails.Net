using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Plugins;

/// <summary>
/// 插件管理器，负责插件发现、加载和生命周期管理。
/// </summary>
public sealed class PluginManager
{
    private readonly List<IPlugin> _plugins = new();
    private readonly IServiceProvider _services;
    private readonly ILogger<PluginManager>? _logger;

    /// <summary>
    /// 已注册的插件只读列表。
    /// </summary>
    public IReadOnlyList<IPlugin> Plugins => _plugins.AsReadOnly();

    /// <summary>
    /// 构造插件管理器。
    /// </summary>
    /// <param name="services">DI 服务容器，用于从 DI 收集插件实例。</param>
    /// <param name="logger">日志器，可为 null。</param>
    public PluginManager(IServiceProvider services, ILogger<PluginManager>? logger = null)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// 注册插件实例。
    /// </summary>
    /// <param name="plugin">插件实例。</param>
    public void Register(IPlugin plugin)
    {
        _plugins.Add(plugin);
        _logger?.LogInformation("插件已注册: {Name}", plugin.Name);
    }

    /// <summary>
    /// 注册插件类型（使用无参构造函数创建实例）。
    /// </summary>
    /// <typeparam name="TPlugin">插件类型，必须有无参构造函数。</typeparam>
    /// <returns>创建的插件实例。</returns>
    public TPlugin Register<TPlugin>() where TPlugin : class, IPlugin, new()
    {
        var plugin = new TPlugin();
        Register(plugin);
        return plugin;
    }

    /// <summary>
    /// 从 DI 容器收集所有已注册的 <see cref="IPlugin"/> 实例并注册到管理器。
    /// </summary>
    public void RegisterFromServices()
    {
        var plugins = _services.GetService<IEnumerable<IPlugin>>();
        if (plugins is null)
        {
            return;
        }

        foreach (var plugin in plugins)
        {
            // 避免重复注册
            if (!_plugins.Contains(plugin))
            {
                Register(plugin);
            }
        }
    }

    /// <summary>
    /// 初始化所有插件（注册服务、配置命令、声明权限）。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    /// <param name="commands">命令注册表。</param>
    /// <param name="configuration">配置。</param>
    /// <param name="loggerFactory">日志工厂。</param>
    public void InitializeAll(
        IServiceCollection services,
        CommandRegistry commands,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        // 从 DI 容器解析 PermissionManager（可能未注册，为 null 时使用空权限声明器）
        var permissionManager = _services.GetService<PermissionManager>();
        var context = new PluginContext(services, commands, configuration, loggerFactory, permissionManager);

        foreach (var plugin in _plugins)
        {
            try
            {
                plugin.ConfigureServices(services);
                plugin.Configure(context);
                _logger?.LogInformation("插件已初始化: {Name}", plugin.Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "插件初始化失败: {Name}", plugin.Name);
                throw;
            }
        }
    }

    /// <summary>
    /// 启动所有插件（按注册顺序）。
    /// 在 <see cref="Application.Run"/> 中、OnAfterStart 回调之后调用。
    /// 对应 Wails v3 的 Startup() 和 Tauri v2 的 setup() 钩子。
    /// 单个插件启动失败不影响其他插件，但会记录错误日志。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步启动操作的任务。</returns>
    public async Task StartupPluginsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var plugin in _plugins)
        {
            try
            {
                await plugin.StartupAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("插件已启动: {Name}", plugin.Name);
            }
            catch (Exception ex)
            {
                // 单个插件启动失败不应中断其他插件的启动
                _logger?.LogError(ex, "插件启动失败: {Name}", plugin.Name);
            }
        }
    }

    /// <summary>
    /// 关闭所有插件（按注册的逆序）。
    /// 在 <see cref="Application.Shutdown"/> 中、关闭任务执行之后调用。
    /// 对应 Wails v3 的 Shutdown() 和 Tauri v2 的 on_drop 钩子。
    /// 单个插件关闭失败不影响其他插件，但会记录错误日志。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步关闭操作的任务。</returns>
    public async Task ShutdownPluginsAsync(CancellationToken cancellationToken = default)
    {
        // 逆序关闭，确保依赖关系正确（后注册的插件先关闭）
        for (var i = _plugins.Count - 1; i >= 0; i--)
        {
            try
            {
                await _plugins[i].ShutdownAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("插件已关闭: {Name}", _plugins[i].Name);
            }
            catch (Exception ex)
            {
                // 单个插件关闭失败不应中断其他插件的关闭
                _logger?.LogError(ex, "插件关闭失败: {Name}", _plugins[i].Name);
            }
        }
    }

    /// <summary>
    /// 从程序集自动发现并加载插件（使用无参构造函数创建实例）。
    /// </summary>
    /// <param name="assembly">要扫描的程序集。</param>
    public void DiscoverFromAssembly(Assembly assembly)
    {
        var pluginTypes = assembly.GetTypes()
            .Where(t => typeof(IPlugin).IsAssignableFrom(t)
                && !t.IsAbstract
                && !t.IsInterface
                && t.GetConstructor(Type.EmptyTypes) is not null);

        foreach (var type in pluginTypes)
        {
            try
            {
                var plugin = (IPlugin)Activator.CreateInstance(type)!;
                Register(plugin);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "插件发现失败: {Type}", type.Name);
            }
        }
    }
}
