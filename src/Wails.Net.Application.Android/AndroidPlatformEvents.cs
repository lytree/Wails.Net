using Wails.Net.Events;

namespace Wails.Net.Application.Android;

/// <summary>
/// Android 平台事件 ID 常量与公共事件映射。
/// 对应 Wails v3 Go 版本 <c>events_common_android.go</c> 中的 <c>commonApplicationEventMap</c>。
/// <para>
/// Wails v3 在 <c>go-events/events.go</c> 中为 Android 平台定义了一组事件常量（1267~1285），
/// 并在 <c>events_common_android.go</c> 中将其中 7 个事件映射到 <c>Common.*</c> 公共事件，
/// 以便用户代码可以跨平台订阅统一的公共事件。
/// </para>
/// <para>
/// Wails.Net 采用相同策略：Activity 生命周期 / 系统广播接收器触发 Android 平台事件 ID，
/// 通过 <see cref="MapToCommonEvent"/> 转换为公共 <see cref="ApplicationEventType"/>，
/// 由 <see cref="Platform.AndroidPlatformApp"/> 调用 <see cref="Application.HandlePlatformEvent(uint)"/> 分发。
/// </para>
/// </summary>
public static class AndroidPlatformEvents
{
    // ---------------------------------------------------------------------
    // Android 平台事件 ID（对应 Wails v3 events.Android.* 常量）
    // ---------------------------------------------------------------------

    /// <summary>Activity 已创建（对应 Wails v3 Android.ActivityCreated = 1267）。</summary>
    public const uint ActivityCreated = 1267;

    /// <summary>Activity 已启动（对应 Wails v3 Android.ActivityStarted = 1268）。</summary>
    public const uint ActivityStarted = 1268;

    /// <summary>Activity 已恢复（对应 Wails v3 Android.ActivityResumed = 1269）。</summary>
    public const uint ActivityResumed = 1269;

    /// <summary>Activity 已暂停（对应 Wails v3 Android.ActivityPaused = 1270）。</summary>
    public const uint ActivityPaused = 1270;

    /// <summary>Activity 已停止（对应 Wails v3 Android.ActivityStopped = 1271）。</summary>
    public const uint ActivityStopped = 1271;

    /// <summary>Activity 已销毁（对应 Wails v3 Android.ActivityDestroyed = 1272）。</summary>
    public const uint ActivityDestroyed = 1272;

    /// <summary>系统内存不足（对应 Wails v3 Android.ApplicationLowMemory = 1273）。</summary>
    public const uint ApplicationLowMemory = 1273;

    /// <summary>电池状态已更改（对应 Wails v3 Android.BatteryChanged = 1281）。</summary>
    public const uint BatteryChanged = 1281;

    /// <summary>网络状态已更改（对应 Wails v3 Android.NetworkChanged = 1282）。</summary>
    public const uint NetworkChanged = 1282;

    /// <summary>系统主题已更改（对应 Wails v3 Android.ThemeChanged = 1283）。</summary>
    public const uint ThemeChanged = 1283;

    /// <summary>屏幕已锁定（对应 Wails v3 Android.ScreenLocked = 1284）。</summary>
    public const uint ScreenLocked = 1284;

    /// <summary>屏幕已解锁（对应 Wails v3 Android.ScreenUnlocked = 1285）。</summary>
    public const uint ScreenUnlocked = 1285;

    // ---------------------------------------------------------------------
    // Android → Common 事件映射（对应 events_common_android.go 的 commonApplicationEventMap）
    // ---------------------------------------------------------------------

    /// <summary>
    /// Android 平台事件到公共事件的映射表。
    /// 对应 Wails v3 <c>events_common_android.go</c> 中的 <c>commonApplicationEventMap</c>：
    /// <list type="bullet">
    ///   <item><c>Android.ActivityCreated</c> → <c>Common.ApplicationStarted</c></item>
    ///   <item><c>Android.ApplicationLowMemory</c> → <c>Common.LowMemory</c></item>
    ///   <item><c>Android.BatteryChanged</c> → <c>Common.BatteryChanged</c></item>
    ///   <item><c>Android.NetworkChanged</c> → <c>Common.NetworkChanged</c></item>
    ///   <item><c>Android.ThemeChanged</c> → <c>Common.ThemeChanged</c></item>
    ///   <item><c>Android.ScreenLocked</c> → <c>Common.ScreenLocked</c></item>
    ///   <item><c>Android.ScreenUnlocked</c> → <c>Common.ScreenUnlocked</c></item>
    /// </list>
    /// </summary>
    private static readonly Dictionary<uint, ApplicationEventType> _commonEventMap = new()
    {
        { ActivityCreated, ApplicationEventType.Started },
        { ApplicationLowMemory, ApplicationEventType.LowMemory },
        { BatteryChanged, ApplicationEventType.BatteryChanged },
        { NetworkChanged, ApplicationEventType.NetworkChanged },
        { ThemeChanged, ApplicationEventType.ThemeChanged },
        { ScreenLocked, ApplicationEventType.ScreenLocked },
        { ScreenUnlocked, ApplicationEventType.ScreenUnlocked },
    };

    /// <summary>
    /// 将 Android 平台事件 ID 映射到公共 <see cref="ApplicationEventType"/>。
    /// 对应 Wails v3 <c>setupCommonEvents</c> 中的转发逻辑：
    /// 仅 7 个 Android 事件被转发为公共事件，其余（如 ActivityResumed/Paused）保留为 Android 专属事件。
    /// </summary>
    /// <param name="androidEventId">Android 平台事件 ID（见本类常量）。</param>
    /// <returns>对应的公共事件类型；若该 Android 事件未映射到公共事件则返回 null。</returns>
    public static ApplicationEventType? MapToCommonEvent(uint androidEventId)
    {
        return _commonEventMap.TryGetValue(androidEventId, out var common) ? common : null;
    }

    /// <summary>
    /// 判断指定的 Android 平台事件 ID 是否已映射到公共事件。
    /// </summary>
    /// <param name="androidEventId">Android 平台事件 ID。</param>
    /// <returns>已映射返回 true；否则返回 false。</returns>
    public static bool HasCommonMapping(uint androidEventId)
    {
        return _commonEventMap.ContainsKey(androidEventId);
    }
}
