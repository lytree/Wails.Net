namespace Wails.Net.Application.Commands;

/// <summary>
/// 命令调用上下文。
/// 在命令方法执行时提供运行时环境信息，如 DI 服务容器、当前窗口 ID、窗口名和取消令牌。
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
    /// 当前触发调用的窗口名（如果有）。
    /// 用于窗口级 Capability 运行时隔离：调度器据此过滤该窗口可用的权限。
    /// 对应 Tauri v2 Capability.Windows 字段的运行时校验。
    /// </summary>
    string? WindowName { get; }

    /// <summary>
    /// 当前调用来源 URL（前端页面 origin）。
    /// 用于 Capability.remote 字段的运行时校验：
    /// 当权限附带远程 URL 限制时，调度器据此校验远程来源是否在允许模式集合内。
    /// 传入 <c>null</c> 或空字符串视为本地源（如 wails:// 协议或系统调用）。
    /// </summary>
    string? Origin { get; }

    /// <summary>
    /// 取消令牌，用于传播取消请求。
    /// </summary>
    CancellationToken CancellationToken { get; }
}
