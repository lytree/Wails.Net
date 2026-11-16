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

    /// <summary>
    /// 本次调用绑定的双向通道（RH-IPC-05，可为 null）。
    /// <para>
    /// 前端通过 <c>wails.call(name, args, { channel })</c> 携带 channelId 时，调度器
    /// 从 <see cref="Wails.Net.Application.Channels.ChannelManager"/> 解析出对应通道实例注入此属性。
    /// 命令内部可用 <c>ctx.Channel?.SendAsync(progress)</c> 向前端回传进度——
    /// 即 Channel-on-argument 模式（Updater 下载进度等长耗时任务的关键原语）。
    /// </para>
    /// <para>
    /// 默认接口实现返回 null（DIM），因此既有 <see cref="ICommandContext"/> 自定义实现
    /// 无需修改即可继续编译。
    /// </para>
    /// </summary>
    Wails.Net.Application.Channels.IChannel? Channel => null;
}
