namespace Wails.Net.Application.Commands;

/// <summary>
/// 命令调用上下文实现。
/// 对应 Wails v3 Go 版本中调用绑定方法时传递的上下文信息。
/// </summary>
internal sealed class CommandContext : ICommandContext
{
    /// <summary>
    /// DI 服务容器。
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// 当前触发调用的窗口 ID（如果有）。
    /// </summary>
    public uint? WindowId { get; }

    /// <summary>
    /// 取消令牌。
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// 构造命令上下文。
    /// </summary>
    /// <param name="services">DI 服务容器。</param>
    /// <param name="windowId">当前窗口 ID，可为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public CommandContext(IServiceProvider services, uint? windowId, CancellationToken cancellationToken)
    {
        Services = services;
        WindowId = windowId;
        CancellationToken = cancellationToken;
    }
}
