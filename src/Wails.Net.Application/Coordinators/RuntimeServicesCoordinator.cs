using Microsoft.Extensions.Logging;
using Wails.Net.Application.Options;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Coordinators;

/// <summary>
/// 运行时服务协调器（审计 P1-1 落地）。
/// <para>
/// 收敛 <see cref="Application"/> 中"注册服务生命周期"的职责：
/// <list type="bullet">
/// <item>启动：按注册顺序调用 <see cref="IServiceStartup.ServiceStartup"/>；</item>
/// <item>关闭：按注册逆序调用 <see cref="IServiceShutdown.ServiceShutdown"/>。</item>
/// </list>
/// 从 <see cref="Application.Run"/> / <see cref="Application.Shutdown"/> 中抽取，
/// 使后续新增的服务生命周期逻辑（重试、超时、依赖顺序、指标）优先进入本协调器，
/// 而不是继续堆到 <see cref="Application"/> 聚合根。
/// </para>
/// </summary>
public sealed class RuntimeServicesCoordinator : IRuntimeCoordinator
{
    private readonly ServiceRegistry _registry;
    private readonly ILogger? _logger;

    /// <summary>协调器名称。</summary>
    public string Name => "runtime-services";

    /// <summary>
    /// 构造运行时服务协调器。
    /// </summary>
    /// <param name="registry">服务注册表（共享 <see cref="Application.Services"/> 同一实例）。</param>
    /// <param name="logger">日志记录器，可为 null。</param>
    public RuntimeServicesCoordinator(ServiceRegistry registry, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// 按注册顺序启动所有实现 <see cref="IServiceStartup"/> 的服务。
    /// 对应 <see cref="Application.Run"/> 中"启动服务（按注册顺序）"阶段。
    /// </summary>
    /// <param name="options">应用选项，透传给 <see cref="IServiceStartup.ServiceStartup"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步启动操作的任务。</returns>
    public Task StartAsync(ApplicationOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var service in _registry.Services)
        {
            if (service is IServiceStartup startup)
            {
                _logger?.LogDebug("启动服务（IServiceStartup）：{ServiceType}", service.GetType().FullName);
                startup.ServiceStartup(options, cancellationToken).GetAwaiter().GetResult();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 按注册逆序关闭所有实现 <see cref="IServiceShutdown"/> 的服务。
    /// 对应 <see cref="Application.Shutdown"/> 中"逆序关闭服务"阶段。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步关闭操作的任务。</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        var services = _registry.Services.ToList();
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i] is IServiceShutdown shutdown)
            {
                _logger?.LogDebug("关闭服务（IServiceShutdown）：{ServiceType}", services[i].GetType().FullName);
                shutdown.ServiceShutdown(cancellationToken).GetAwaiter().GetResult();
            }
        }

        return Task.CompletedTask;
    }
}
