namespace Wails.Net.Application.Services;

/// <summary>
/// 服务关闭生命周期接口。
/// </summary>
public interface IServiceShutdown
{
    /// <summary>
    /// 服务关闭。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示关闭操作的异步任务。</returns>
    Task ServiceShutdown(CancellationToken cancellationToken);
}
