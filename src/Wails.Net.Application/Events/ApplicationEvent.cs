using Wails.Net.Events;

namespace Wails.Net.Application.Events;

/// <summary>
/// 表示一个应用程序级别的事件。
/// 对应 Wails v3 Go 版本 events.go 中的 ApplicationEvent 结构。
/// <para>
/// 与 <see cref="CustomEvent"/> 不同，应用事件由平台触发（窗口焦点变化、系统主题切换、
/// 电池/网络/显示器变化等），通过 <see cref="EventProcessor.OnApplicationEvent"/> 订阅、
/// <see cref="EventProcessor.EmitApplicationEvent"/> 分发。应用事件支持钩子
/// （<see cref="EventProcessor.RegisterApplicationEventHook"/>）在监听器执行前拦截。
/// </para>
/// </summary>
public sealed class ApplicationEvent
{
    private volatile bool _cancelled;

    /// <summary>
    /// 事件类型 ID（对应 <see cref="ApplicationEventType"/> 的 uint 值）。
    /// </summary>
    public uint Id { get; }

    /// <summary>
    /// 事件数据，可为 null。
    /// </summary>
    public object? Data { get; }

    /// <summary>
    /// 触发事件的窗口 ID；若为应用级事件（无具体窗口来源）则为 null。
    /// </summary>
    public uint? WindowId { get; }

    /// <summary>
    /// 事件是否已被取消。取消后后续监听器不再接收此事件。
    /// </summary>
    public bool IsCancelled => _cancelled;

    /// <summary>
    /// 构造 ApplicationEvent 实例。
    /// </summary>
    /// <param name="id">事件类型 ID。</param>
    /// <param name="data">事件数据。</param>
    /// <param name="windowId">触发事件的窗口 ID，可为 null。</param>
    public ApplicationEvent(uint id, object? data = null, uint? windowId = null)
    {
        Id = id;
        Data = data;
        WindowId = windowId;
    }

    /// <summary>
    /// 取消事件，阻止后续监听器和钩子接收此事件。
    /// </summary>
    public void Cancel()
    {
        _cancelled = true;
    }
}
