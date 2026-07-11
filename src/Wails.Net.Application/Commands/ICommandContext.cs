namespace Wails.Net.Application.Commands;

/// <summary>
/// 命令调用上下文。
/// 在命令方法执行时提供运行时环境信息，如 DI 服务容器、当前窗口 ID 和取消令牌。
/// </summary>
public interface ICommandContext
{
    /// <summary>
    /// DI 服务容器，可用于在命令方法内解析其他服务。
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// 当前触发调用的窗口 ID（如果有）。
    /// </summary>
    uint? WindowId { get; }

    /// <summary>
    /// 取消令牌，用于传播取消请求。
    /// </summary>
    CancellationToken CancellationToken { get; }
}
