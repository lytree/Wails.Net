using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// 桌面应用实现，封装 Generic Host。
/// 对应 ASP.NET Core 风格的 Generic Host 模式，将现有 <see cref="Application"/> 适配为托管应用。
/// </summary>
public sealed class DesktopApplication : IDesktopApplication
{
    private readonly IHost _host;
    private readonly Application _application;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>应用名称</summary>
    public string Name { get; }

    /// <summary>DI 服务容器</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>配置</summary>
    public IConfiguration Configuration => _host.Services.GetRequiredService<IConfiguration>();

    /// <summary>日志工厂</summary>
    public ILoggerFactory LoggerFactory => _host.Services.GetRequiredService<ILoggerFactory>();

    /// <summary>
    /// Host 应用生命周期，用于注册 Started/Stopping/Stopped 回调。
    /// 对应 ASP.NET Core 的 <c>IHostApplicationLifetime</c>。
    /// </summary>
    public IHostApplicationLifetime Lifetime => _host.Services.GetRequiredService<IHostApplicationLifetime>();

    /// <summary>底层 Application 实例（兼容层）</summary>
    public Application Application => _application;

    /// <summary>
    /// 内部构造函数，由 <see cref="DesktopApplicationBuilder.Build"/> 调用。
    /// </summary>
    /// <param name="host">已构建的 Generic Host 实例。</param>
    /// <param name="application">兼容层 Application 实例。</param>
    /// <param name="name">应用名称。</param>
    internal DesktopApplication(IHost host, Application application, string name)
    {
        _host = host;
        _application = application;
        Name = name;
    }

    /// <summary>
    /// 启动应用，阻塞直到应用停止。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码，0 表示正常退出，1 表示发生异常。</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        try
        {
            await _host.RunAsync(linkedCts.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            var logger = _host.Services.GetService<ILogger<DesktopApplication>>();
            logger?.LogError(ex, "应用运行时发生未处理异常");
            return 1;
        }
    }

    /// <summary>
    /// 请求停止应用，取消内部令牌并触发 Application 关闭流程。
    /// </summary>
    public void Stop()
    {
        _cts.Cancel();
        _application.Shutdown();
    }

    /// <summary>
    /// 释放资源，停止应用并释放 Host。
    /// </summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public async ValueTask DisposeAsync()
    {
        _cts.Dispose();
        await ((IAsyncDisposable)_host).DisposeAsync().ConfigureAwait(false);
    }
}
