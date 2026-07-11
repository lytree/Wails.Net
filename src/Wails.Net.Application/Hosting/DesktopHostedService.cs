using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// 桌面应用宿主服务，将现有 <see cref="Application"/> 生命周期适配为 <see cref="IHostedService"/>。
/// 在 <see cref="StartAsync"/> 中后台启动 <see cref="Application.Run"/>（阻塞主循环），
/// 在 <see cref="StopAsync"/> 中触发 <see cref="Application.Shutdown"/>。
/// </summary>
internal sealed class DesktopHostedService : IHostedService, IDisposable
{
    private readonly ILogger<DesktopHostedService> _logger;
    private readonly Application _application;
    private Thread? _uiThread;
    private readonly TaskCompletionSource _startedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 构造函数，由 DI 容器注入依赖。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="application">兼容层 Application 实例。</param>
    public DesktopHostedService(ILogger<DesktopHostedService> logger, Application application)
    {
        _logger = logger;
        _application = application;
    }

    /// <summary>
    /// 启动服务，在专用 STA 线程运行 <see cref="Application.Run"/>（阻塞调用）。
    /// Win32 消息循环和 WebView2 要求在 STA 线程上运行，因此不能使用线程池线程（MTA）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动初始化已完成的任务。</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("桌面应用启动中...");

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
                _startedTcs.TrySetException(ex);
            }
            finally
            {
                _startedTcs.TrySetResult();
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

        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止服务，触发 <see cref="Application.Shutdown"/>。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已完成的任务。</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("桌面应用停止中...");
        _application.Shutdown();

        // 等待 UI 线程退出
        if (_uiThread is not null && _uiThread.IsAlive)
        {
            try
            {
                _uiThread.Join(TimeSpan.FromSeconds(5));
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
