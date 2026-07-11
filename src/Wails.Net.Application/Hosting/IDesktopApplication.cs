using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// 桌面应用接口，封装 Microsoft.Extensions.Hosting.IHost。
/// 提供应用生命周期管理和 DI 容器访问。
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
