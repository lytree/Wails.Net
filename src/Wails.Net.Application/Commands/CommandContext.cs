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
    /// 当前触发调用的窗口名（如果有）。
    /// 用于窗口级 Capability 运行时隔离。
    /// </summary>
    public string? WindowName { get; }

    /// <summary>
    /// 当前调用来源 URL（前端页面 origin）。
    /// 用于 Capability.remote 字段的运行时校验；
    /// 传入 <c>null</c> 或空字符串视为本地源。
    /// </summary>
    public string? Origin { get; }

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
    /// <param name="windowName">当前窗口名，可为 null；建议与 <paramref name="windowId"/> 同时提供以便窗口级权限校验。</param>
    /// <param name="origin">调用来源 URL，可为 null；非空时用于 Capability.remote 远程 URL 校验。</param>
    public CommandContext(
        IServiceProvider services,
        uint? windowId,
        CancellationToken cancellationToken,
        string? windowName = null,
        string? origin = null)
    {
        Services = services;
        WindowId = windowId;
        CancellationToken = cancellationToken;
        WindowName = windowName;
        Origin = origin;
    }
}
