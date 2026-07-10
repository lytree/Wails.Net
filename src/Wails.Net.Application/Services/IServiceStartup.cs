using Wails.Net.Application.Options;

namespace Wails.Net.Application.Services;

/// <summary>
/// 服务启动生命周期接口。
/// </summary>
public interface IServiceStartup
{
    /// <summary>
    /// 服务启动。
    /// </summary>
    /// <param name="options">应用选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动操作的异步任务。</returns>
    Task ServiceStartup(ApplicationOptions options, CancellationToken cancellationToken);
}
