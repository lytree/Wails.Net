namespace Wails.Net.Events;

/// <summary>
/// 提供通用事件名称及保留事件名称检查的静态类。
/// 用于防止自定义事件名称与系统事件名称发生冲突。
/// </summary>
public static class CommonEvents
{
    /// <summary>
    /// 已知事件名称集合，包含所有系统保留事件名称。
    /// 用于防止自定义事件名称与系统事件名称冲突。
    /// </summary>
    public static readonly HashSet<string> KnownEventNames = new()
    {
        KnownEvents.Startup,
        KnownEvents.Shutdown,
        KnownEvents.ThemeChanged,
        KnownEvents.FileDropped,
        KnownEvents.WindowClosing,
        KnownEvents.WindowClosed,
        KnownEvents.WindowFocus,
        KnownEvents.WindowFocusLost,
        KnownEvents.DPIChanged,
        KnownEvents.BatteryChanged,
        KnownEvents.NetworkChanged,
        KnownEvents.ClipboardChanged,
        KnownEvents.SystemTrayClick,
        KnownEvents.SystemTrayMenuOpen,
    };

    /// <summary>
    /// 判断指定名称是否为已知的系统事件名称（保留名称）。
    /// </summary>
    /// <param name="name">要检查的事件名称。</param>
    /// <returns>如果名称是保留的系统事件名称，则返回 true；否则返回 false。</returns>
    public static bool IsKnownEvent(string name) => KnownEventNames.Contains(name);
}
