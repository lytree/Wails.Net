using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// 桌面应用接口，封装 Microsoft.Extensions.Hosting.IHost。
/// 提供应用生命周期管理和 DI 容器访问。
/// 对应 AGENTS.md §1.1.1 技术选型：宿主统一使用 <c>Microsoft.Extensions.Hosting</c>。
/// </summary>
public interface IDesktopApplication : IAsyncDisposable
{
    /// <summary>应用名称</summary>
    string Name { get; }

    /// <summary>DI 服务容器</summary>
    IServiceProvider Services { get; }

    /// <summary>配置</summary>
    IConfiguration Configuration { get; }

    /// <summary>日志工厂</summary>
    ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Host 应用生命周期，用于注册 Started/Stopping/Stopped 回调。
    /// 对应 ASP.NET Core 的 <c>IHostApplicationLifetime</c>，
    /// 让用户代码可以接入标准生命周期钩子。
    /// </summary>
    IHostApplicationLifetime Lifetime { get; }

    /// <summary>底层 Application 实例（兼容层）</summary>
    Application Application { get; }

    /// <summary>
    /// 启动应用。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码，0 表示正常退出。</returns>
    Task<int> RunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 请求停止应用。
    /// </summary>
    void Stop();
}
