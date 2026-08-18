using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// 桌面应用宿主服务，将现有 <see cref="Application"/> 生命周期适配为 <see cref="IHostedService"/>。
/// 在 <see cref="StartAsync"/> 中后台启动 <see cref="Application.Run"/>（阻塞主循环），
/// 在 <see cref="StopAsync"/> 中触发 <see cref="Application.Shutdown"/>。
/// <para>
/// OPT-RUNTIME-002（2026-08-15）：<see cref="StartAsync"/> 不再立即返回，
/// 而是等待 <see cref="Application.WaitUntilPlatformStartedAsync"/>（平台消息循环就绪信号），
/// 使 Host 启动语义与 UI 就绪对齐。原 <c>_startedTcs</c>（从未被 await 的"退出信号"）已移除。
/// </para>
/// </summary>
internal sealed class DesktopHostedService : IHostedService, IDisposable
{
    private readonly ILogger<DesktopHostedService> _logger;
    private readonly Application _application;
    private readonly IHostApplicationLifetime _lifetime;
    private Thread? _uiThread;

    /// <summary>
    /// 构造函数，由 DI 容器注入依赖。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="application">兼容层 Application 实例。</param>
    /// <param name="lifetime">Host 应用生命周期，用于在 UI 线程退出后通知 Host 停止。</param>
    public DesktopHostedService(
        ILogger<DesktopHostedService> logger,
        Application application,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _application = application;
        _lifetime = lifetime;
    }

    /// <summary>
    /// 启动服务，在专用 STA 线程运行 <see cref="Application.Run"/>（阻塞调用），
    /// 并等待平台就绪信号后返回（OPT-RUNTIME-002）。
    /// <para>
    /// Win32 消息循环和 WebView2 要求在 STA 线程上运行，因此不能使用线程池线程（MTA）。
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示平台启动就绪（或超时）的任务。</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("桌面应用启动中...");

        // 注册全局异常处理器，捕获未处理的异常和未观察的 Task 异常。
        // 防止应用因未捕获异常而静默崩溃，便于诊断问题。
        RegisterGlobalExceptionHandlers();

        // 在专用 STA 线程运行 Application.Run()（因为它是阻塞的，且需要 STA 线程）
        _uiThread = new Thread(() =>
        {
            try
            {
                _application.Run();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Application.Run() 发生异常");
                // 平台就绪信号失败：使 StartAsync 的等待快速失败（而非超时 30s）
                try
                {
                    _application.MarkPlatformStartFailed(ex);
                }
                catch
                {
                    // 标记失败不可用时忽略（信号可能已 SetResult，属竞态边界）
                }
            }
            finally
            {
                // UI 线程退出后通知 Generic Host 停止，否则 Host 会一直等待导致进程不退出。
                // 对应窗口关闭按钮触发 PostQuitMessage(0) → 消息循环退出 → Application.Run() 返回的场景。
                try
                {
                    _lifetime.StopApplication();
                }
                catch
                {
                    // 通知 Host 停止失败时忽略，避免掩盖原始异常
                }
            }
        })
        {
            Name = "Wails.Net UI Thread",
            IsBackground = true,
        };
        // Win32 消息循环和 WebView2 要求 STA 线程；Linux/GTK 无此要求但设置为 STA 也不影响。
        if (OperatingSystem.IsWindows())
        {
            _uiThread.SetApartmentState(ApartmentState.STA);
        }
        _uiThread.Start();

        // OPT-RUNTIME-002：等待平台就绪（默认 30s 超时）。
        // 使 Host.StartAsync 的返回语义与 UI 主循环就绪对齐，而非启动线程后立即返回。
        // 平台主循环异常时此处直接抛出，Host 启动失败可正确传播。
        var started = await _application.WaitUntilPlatformStartedAsync();
        if (!started)
        {
            _logger.LogWarning("等待平台启动就绪超时，Host 继续启动（应用可能在无平台模式下运行）");
        }
    }

    /// <summary>
    /// 注册全局异常处理器，捕获两类未处理异常：
    /// 1. <see cref="AppDomain.UnhandledException"/> — CLR 未捕获的异常（所有线程）。
    /// 2. <see cref="TaskScheduler.UnobservedTaskException"/> — 未观察的 Task 异常。
    /// 所有异常通过 <see cref="ILogger"/> 记录为错误级别日志，便于诊断问题。
    /// </summary>
    private void RegisterGlobalExceptionHandlers()
    {
        // AppDomain 级未处理异常：捕获所有线程抛出的未处理异常。
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            _logger.LogError(ex, "全局未处理异常 (IsTerminating={IsTerminating})", e.IsTerminating);
        };

        // TaskScheduler 未观察异常：Task 中抛出但未被 await/检查 的异常。
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger.LogError(e.Exception, "未观察的 Task 异常");
            e.SetObserved(); // 标记已观察，防止进程被强制终止
        };
    }

    /// <summary>
    /// 停止服务，触发 <see cref="Application.ShutdownAsync(CancellationToken)"/>（R1-4）。
    /// <para>
    /// 与 ASP.NET Core <c>IHostedService.StopAsync</c> 契约对齐：
    /// <list type="bullet">
    /// <item>外部 <paramref name="cancellationToken"/>（来自 Host.ShutdownTimeout）串联到 Application.ShutdownAsync，</item>
    /// <item>Application 内部进一步与 <c>ApplicationOptions.ShutdownTimeout</c> 组合，任一触发即取消关闭流程，</item>
    /// <item>UI 线程等待超时取 min(外部 cancellationToken 剩余时间, 5s)，避免 Host 关闭被卡住。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌。Host.ShutdownTimeout 触发时此令牌被取消。</param>
    /// <returns>关闭完成的任务。</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("桌面应用停止中...");

        // R1-4：使用 ShutdownAsync 异步关闭路径，串联 Host 取消令牌与 ApplicationOptions.ShutdownTimeout。
        try
        {
            await _application.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host 关闭超时取消时不视为错误，仅记录日志。
            _logger.LogWarning("Application.ShutdownAsync 因 Host 关闭超时被取消（ShutdownTimeout 已贯穿）");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Application.ShutdownAsync 发生异常");
        }

        // 等待 UI 线程退出。R1-4：使用 min(外部 cancellationToken 剩余时间, 5s) 作为上限，
        // 当 Host.ShutdownTimeout 已触发时立即放弃等待，避免拖慢 Host 整体关闭。
        if (_uiThread is not null && _uiThread.IsAlive)
        {
            TimeSpan joinTimeout;
            if (cancellationToken.CanBeCanceled && cancellationToken.IsCancellationRequested)
            {
                // Host 关闭超时已触发，立即放弃等待
                joinTimeout = TimeSpan.Zero;
            }
            else
            {
                joinTimeout = TimeSpan.FromSeconds(5);
            }

            try
            {
                if (!_uiThread.Join(joinTimeout))
                {
                    _logger.LogWarning("UI 线程未在 {Timeout} 内退出", joinTimeout);
                }
            }
            catch
            {
                // 超时后放弃等待
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 释放资源，确保应用已关闭。
    /// </summary>
    public void Dispose()
    {
        _application.Shutdown();
    }
}
