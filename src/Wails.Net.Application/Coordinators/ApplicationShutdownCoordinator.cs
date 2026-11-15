using Microsoft.Extensions.Hosting;
using Wails.Net.Application.Events;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Services;
using Wails.Net.Application.Streams;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Coordinators;

/// <summary>
/// 关闭流程所需的资源与状态回调（RH-HOST-02）。
/// <para>
/// 由 <see cref="Wails.Net.Application.Application.ShutdownAsync"/> 在调用前按当前字段值构建；
/// 协调器只读取本上下文，不反向依赖 <see cref="Wails.Net.Application.Application"/> 实例，
/// 因此关闭顺序可独立测试。
/// </para>
/// </summary>
internal sealed class ApplicationShutdownContext
{
    /// <summary>应用选项（ShutdownTimeout / OnShutdown / PostShutdown）。</summary>
    public required ApplicationOptions Options { get; init; }

    /// <summary>Host 生命周期，用于触发 ApplicationStopping。</summary>
    public IHostApplicationLifetime? Lifetime { get; init; }

    /// <summary>应用级取消令牌源，关闭开始时取消。</summary>
    public required CancellationTokenSource ApplicationCancellation { get; init; }

    /// <summary>释放信号处理器并清空引用。</summary>
    public required Action ReleaseSignalHandler { get; init; }

    /// <summary>释放 dev 资产反向代理并清空引用（非 dev 模式为 no-op）。</summary>
    public required Action ReleaseDevAssetProxy { get; init; }

    /// <summary>把生命周期状态推进到指定值。</summary>
    public required Action<ApplicationLifecycleState> SetLifecycleState { get; init; }

    /// <summary>标记应用已停止（<c>IsRunning = false</c> 且状态置为 Stopped）。</summary>
    public required Action MarkStopped { get; init; }

    /// <summary>用户注册的关闭任务与启动前 hook（此处只用到关闭任务）。</summary>
    public required ApplicationLifecycleHooks Hooks { get; init; }

    /// <summary>超大事件载荷存储。</summary>
    public required EventPayloadStore EventPayloads { get; init; }

    /// <summary>Streams 管理器。</summary>
    public required StreamManager Streams { get; init; }

    /// <summary>服务生命周期协调器。</summary>
    public required RuntimeServicesCoordinator RuntimeServices { get; init; }

    /// <summary>窗口管理器；为 null 时跳过窗口关闭。</summary>
    public WindowManager? Windows { get; init; }

    /// <summary>传输层；为 null 时跳过。</summary>
    public ITransport? Transport { get; init; }

    /// <summary>
    /// fallback 传输链；主传输之后、资源服务器之前停止（RH-IPC-02 第 4 点）。
    /// 为空表示策略未产出 fallback（默认策略即如此）。
    /// </summary>
    public IReadOnlyList<ITransport> FallbackTransports { get; init; } = [];

    /// <summary>资源服务器；为 null 或未实现 <see cref="IServiceShutdown"/> 时跳过。</summary>
    public Wails.Net.AssetServer.AssetServer? AssetServer { get; init; }

    /// <summary>插件管理器；为 null 时跳过插件关闭。</summary>
    public PluginManager? Plugins { get; init; }

    /// <summary>平台应用；为 null（纯 Server 模式）时跳过平台销毁。</summary>
    public IPlatformApp? PlatformApp { get; init; }
}

/// <summary>
/// 应用关闭协调器（RH-HOST-02）。
/// <para>
/// 收敛原 <c>Application.ShutdownAsync</c> 中的关闭顺序：
/// 生命周期事件 → 取消令牌 → 信号处理器 → 关闭任务/OnShutdown → 事件载荷与 Streams →
/// 窗口 → 传输层 → 资源服务器 → dev 代理 → 插件 → 服务（逆序）→ 平台销毁 → PostShutdown。
/// </para>
/// <para>
/// 每一步的失败都被隔离（除 <see cref="ApplicationOptions.OnShutdown"/> 外与抽取前一致：
/// 用户 OnShutdown 异常仍然向上抛出，避免掩盖用户代码问题）。取消由
/// <see cref="ApplicationOptions.ShutdownTimeout"/> 与外部令牌共同决定，先到者生效。
/// </para>
/// </summary>
internal sealed class ApplicationShutdownCoordinator
{
    private readonly ApplicationShutdownContext _ctx;

    /// <summary>
    /// 构造关闭协调器。
    /// </summary>
    /// <param name="context">关闭上下文（资源与状态回调）。</param>
    public ApplicationShutdownCoordinator(ApplicationShutdownContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _ctx = context;
    }

    /// <summary>
    /// 执行完整关闭流程。
    /// </summary>
    /// <param name="cancellationToken">外部取消令牌（宿主关闭超时或用户主动取消）。</param>
    /// <returns>表示关闭流程的任务。</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _ctx.SetLifecycleState(ApplicationLifecycleState.Stopping);

        // 触发 IHostApplicationLifetime.ApplicationStopping：
        // 让用户代码可以通过 Lifetime.ApplicationStopping.Register(...) 接入标准关闭钩子。
        _ctx.Lifetime?.StopApplication();

        // 取消应用级取消令牌。
        _ctx.ApplicationCancellation.Cancel();

        // P2-21：停止默认信号处理器，恢复 SIGINT / SIGTERM 的操作系统默认处置。
        _ctx.ReleaseSignalHandler();

        // R1-4：组合外部 CancellationToken 与 ShutdownTimeout，任一触发即取消后续清理路径，
        // 确保 ShutdownTimeout 真正贯穿整条关闭链。
        using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_ctx.Options.ShutdownTimeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            shutdownCts.CancelAfter(_ctx.Options.ShutdownTimeout);
        }

        var ct = shutdownCts.Token;

        // 关闭任务（options.ShutdownTasks + OnShutdown 注册的任务），单个失败不影响其余。
        _ctx.Hooks.RunShutdownTasks();

        // 触发 OnShutdown 回调（用户代码异常不被吞掉，与抽取前行为一致）。
        _ctx.Options.OnShutdown?.Invoke();

        // 停止超大事件载荷存储的清扫器并释放停放条目。
        _ctx.EventPayloads.Dispose();

        // 关闭 Streams 管理器：拒绝后续预留、清空会话、停止清扫器。
        _ctx.Streams.Dispose();

        CloseWindows();

        await StopTransportAsync(ct).ConfigureAwait(false);
        await StopAssetServerAsync(ct).ConfigureAwait(false);

        // 释放 dev 资产反向代理（P2-12；仅 dev 模式存在）。
        _ctx.ReleaseDevAssetProxy();

        await ShutdownPluginsAsync(ct).ConfigureAwait(false);
        await StopRuntimeServicesAsync(ct).ConfigureAwait(false);
        await ShutdownPlatformAsync(ct).ConfigureAwait(false);

        _ctx.MarkStopped();

        // P1-7：所有清理完成后触发 PostShutdown（异常被吞掉，不影响已完成的关闭流程）。
        try
        {
            _ctx.Options.PostShutdown?.Invoke();
        }
        catch
        {
            // PostShutdown 中的异常不应中断已完成的关闭流程。
        }
    }

    private void CloseWindows()
    {
        var windowManager = _ctx.Windows;
        if (windowManager is null)
        {
            return;
        }

        foreach (var window in windowManager.AllWindows)
        {
            try
            {
                window.Close();
            }
            catch
            {
                // 关闭窗口时的异常不应中断关闭流程。
            }
        }

        windowManager.Clear();
    }

    private async Task StopTransportAsync(CancellationToken ct)
    {
        // 先停 fallback 再停主传输：fallback 依赖主传输的处理器/广播器，
        // 反向顺序会让 fallback 在停机瞬间仍尝试向已关闭的主通道投递。
        foreach (var fallback in _ctx.FallbackTransports)
        {
            await StopTransportCoreAsync(fallback, ct).ConfigureAwait(false);
        }

        if (_ctx.Transport is null)
        {
            return;
        }

        await StopTransportCoreAsync(_ctx.Transport, ct).ConfigureAwait(false);
    }

    private static async Task StopTransportCoreAsync(ITransport transport, CancellationToken ct)
    {
        try
        {
            await transport.StopAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 超时取消时不阻塞关闭流程。
        }
        catch
        {
            // 停止传输层的异常不应中断关闭流程。
        }
    }

    private async Task StopAssetServerAsync(CancellationToken ct)
    {
        if (_ctx.AssetServer is not IServiceShutdown assetShutdown)
        {
            return;
        }

        try
        {
            await assetShutdown.ServiceShutdown(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 超时取消时不阻塞关闭流程。
        }
        catch
        {
            // 停止资源服务器的异常不应中断关闭流程。
        }
    }

    private async Task ShutdownPluginsAsync(CancellationToken ct)
    {
        if (_ctx.Plugins is null)
        {
            return;
        }

        try
        {
            // 在服务逆序关闭之前调用，确保插件能在服务仍然可用时释放资源。
            await _ctx.Plugins.ShutdownPluginsAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 超时取消时不阻塞关闭流程。
        }
        catch
        {
            // 关闭插件的异常不应中断关闭流程。
        }
    }

    private async Task StopRuntimeServicesAsync(CancellationToken ct)
    {
        try
        {
            // P1-1：委托给 RuntimeServicesCoordinator 管理逆序关闭。
            await _ctx.RuntimeServices.StopAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 超时取消时不阻塞关闭流程。
        }
        catch
        {
            // 停止服务的异常不应中断关闭流程。
        }
    }

    private async Task ShutdownPlatformAsync(CancellationToken ct)
    {
        if (_ctx.PlatformApp is null)
        {
            return;
        }

        try
        {
            // R1-4：异步平台销毁（带取消令牌）替代原同步 Destroy。
            await _ctx.PlatformApp.ShutdownAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 平台销毁超时时不阻塞关闭流程。
        }
        catch
        {
            // 平台销毁异常不阻塞关闭流程。
        }
    }
}
