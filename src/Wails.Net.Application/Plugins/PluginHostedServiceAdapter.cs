using Microsoft.Extensions.Hosting;

namespace Wails.Net.Application.Plugins;

/// <summary>
/// 插件生命周期 IHostedService 适配器。
/// 将 <see cref="PluginManager"/> 的 <see cref="PluginManager.StartupPluginsAsync"/>
/// 和 <see cref="PluginManager.ShutdownPluginsAsync"/> 适配到 <see cref="IHostedService"/> 的两阶段生命周期。
/// <para>
/// 启动顺序：Host 启动时调用 <see cref="StartAsync"/> → 触发 <see cref="PluginManager.StartupPluginsAsync"/>。
/// 关闭顺序：Host 关闭时调用 <see cref="StopAsync"/> → 触发 <see cref="PluginManager.ShutdownPluginsAsync"/>。
/// </para>
/// <para>
/// 幂等性：<see cref="PluginManager"/> 内部使用 <see cref="Interlocked"/> 保护，
/// 即使 <see cref="Application.Run"/> 和 IHostedService 同时触发，插件也只会启动/关闭一次。
/// </para>
/// <para>
/// 对应 AGENTS.md §1.1.1 技术融合策略：Host/DI/Config/Logging → ASP.NET Core（IHostedService 集成）。
/// </para>
/// </summary>
internal sealed class PluginHostedServiceAdapter : IHostedService
{
    private readonly PluginManager _pluginManager;

    /// <summary>
    /// 构造适配器。
    /// </summary>
    /// <param name="pluginManager">插件管理器实例（由 DI 注入）。</param>
    public PluginHostedServiceAdapter(PluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    /// <summary>
    /// Host 启动时调用，触发插件启动。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步启动操作的任务。</returns>
    public Task StartAsync(CancellationToken cancellationToken)
        => _pluginManager.StartupPluginsAsync(cancellationToken);

    /// <summary>
    /// Host 关闭时调用，触发插件关闭。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步关闭操作的任务。</returns>
    public Task StopAsync(CancellationToken cancellationToken)
        => _pluginManager.ShutdownPluginsAsync(cancellationToken);
}
