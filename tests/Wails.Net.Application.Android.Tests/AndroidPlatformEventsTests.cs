using TUnit.Core;
using Wails.Net.Events;

namespace Wails.Net.Application.Android.Tests;

/// <summary>
/// AndroidPlatformEvents 的单元测试（TUnit）。
/// 对应 Wails v3 <c>events_common_android.go</c> 中的 <c>commonApplicationEventMap</c>。
/// 验证 7 个 Android 平台事件到公共事件的映射，以及未映射事件的回退行为。
/// </summary>
[NotInParallel]
public sealed class AndroidPlatformEventsTests
{
    // ---------------------------------------------------------------------
    // 常量值测试（验证与 Wails v3 events.go 的常量一致）
    // ---------------------------------------------------------------------

    [Test]
    public async Task ActivityCreated_ConstantMatchesWailsV3()
    {
        // 对应 Wails v3 events.Android.ActivityCreated = 1267
        // 读取到局部变量以避免 TUnitAssertions0005（不应在常量上断言）
        uint actual = AndroidPlatformEvents.ActivityCreated;
        await Assert.That(actual).IsEqualTo(1267u);
    }

    [Test]
    public async Task ApplicationLowMemory_ConstantMatchesWailsV3()
    {
        // 对应 Wails v3 events.Android.ApplicationLowMemory = 1273
        uint actual = AndroidPlatformEvents.ApplicationLowMemory;
        await Assert.That(actual).IsEqualTo(1273u);
    }

    [Test]
    public async Task BatteryChanged_ConstantMatchesWailsV3()
    {
        // 对应 Wails v3 events.Android.BatteryChanged = 1281
        uint actual = AndroidPlatformEvents.BatteryChanged;
        await Assert.That(actual).IsEqualTo(1281u);
    }

    [Test]
    public async Task NetworkChanged_ConstantMatchesWailsV3()
    {
        // 对应 Wails v3 events.Android.NetworkChanged = 1282
        uint actual = AndroidPlatformEvents.NetworkChanged;
        await Assert.That(actual).IsEqualTo(1282u);
    }

    [Test]
    public async Task ThemeChanged_ConstantMatchesWailsV3()
    {
        // 对应 Wails v3 events.Android.ThemeChanged = 1283
        uint actual = AndroidPlatformEvents.ThemeChanged;
        await Assert.That(actual).IsEqualTo(1283u);
    }

    [Test]
    public async Task ScreenLocked_ConstantMatchesWailsV3()
    {
        // 对应 Wails v3 events.Android.ScreenLocked = 1284
        uint actual = AndroidPlatformEvents.ScreenLocked;
        await Assert.That(actual).IsEqualTo(1284u);
    }

    [Test]
    public async Task ScreenUnlocked_ConstantMatchesWailsV3()
    {
        // 对应 Wails v3 events.Android.ScreenUnlocked = 1285
        uint actual = AndroidPlatformEvents.ScreenUnlocked;
        await Assert.That(actual).IsEqualTo(1285u);
    }

    // ---------------------------------------------------------------------
    // 映射测试（验证 7 个 Android → Common 事件映射）
    // ---------------------------------------------------------------------

    [Test]
    public async Task MapToCommonEvent_ActivityCreated_MapsToStarted()
    {
        // 对应 events_common_android.go: Android.ActivityCreated → Common.ApplicationStarted
        var result = AndroidPlatformEvents.MapToCommonEvent(AndroidPlatformEvents.ActivityCreated);
        await Assert.That(result).IsEqualTo(ApplicationEventType.Started);
    }

    [Test]
    public async Task MapToCommonEvent_ApplicationLowMemory_MapsToLowMemory()
    {
        // 对应 events_common_android.go: Android.ApplicationLowMemory → Common.LowMemory
        var result = AndroidPlatformEvents.MapToCommonEvent(AndroidPlatformEvents.ApplicationLowMemory);
        await Assert.That(result).IsEqualTo(ApplicationEventType.LowMemory);
    }

    [Test]
    public async Task MapToCommonEvent_BatteryChanged_MapsToBatteryChanged()
    {
        // 对应 events_common_android.go: Android.BatteryChanged → Common.BatteryChanged
        var result = AndroidPlatformEvents.MapToCommonEvent(AndroidPlatformEvents.BatteryChanged);
        await Assert.That(result).IsEqualTo(ApplicationEventType.BatteryChanged);
    }

    [Test]
    public async Task MapToCommonEvent_NetworkChanged_MapsToNetworkChanged()
    {
        // 对应 events_common_android.go: Android.NetworkChanged → Common.NetworkChanged
        var result = AndroidPlatformEvents.MapToCommonEvent(AndroidPlatformEvents.NetworkChanged);
        await Assert.That(result).IsEqualTo(ApplicationEventType.NetworkChanged);
    }

    [Test]
    public async Task MapToCommonEvent_ThemeChanged_MapsToThemeChanged()
    {
        // 对应 events_common_android.go: Android.ThemeChanged → Common.ThemeChanged
        var result = AndroidPlatformEvents.MapToCommonEvent(AndroidPlatformEvents.ThemeChanged);
        await Assert.That(result).IsEqualTo(ApplicationEventType.ThemeChanged);
    }

    [Test]
    public async Task MapToCommonEvent_ScreenLocked_MapsToScreenLocked()
    {
        // 对应 events_common_android.go: Android.ScreenLocked → Common.ScreenLocked
        var result = AndroidPlatformEvents.MapToCommonEvent(AndroidPlatformEvents.ScreenLocked);
        await Assert.That(result).IsEqualTo(ApplicationEventType.ScreenLocked);
    }

    [Test]
    public async Task MapToCommonEvent_ScreenUnlocked_MapsToScreenUnlocked()
    {
        // 对应 events_common_android.go: Android.ScreenUnlocked → Common.ScreenUnlocked
        var result = AndroidPlatformEvents.MapToCommonEvent(AndroidPlatformEvents.ScreenUnlocked);
        await Assert.That(result).IsEqualTo(ApplicationEventType.ScreenUnlocked);
    }

    // ---------------------------------------------------------------------
    // 未映射事件回退测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task MapToCommonEvent_ActivityStarted_ReturnsNull()
    {
        // ActivityStarted 未映射到 Common 事件
        var result = AndroidPlatformEvents.MapToCommonEvent(AndroidPlatformEvents.ActivityStarted);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task MapToCommonEvent_ActivityResumed_ReturnsNull()
    {
        var result = AndroidPlatformEvents.MapToCommonEvent(AndroidPlatformEvents.ActivityResumed);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task MapToCommonEvent_ActivityPaused_ReturnsNull()
    {
        var result = AndroidPlatformEvents.MapToCommonEvent(AndroidPlatformEvents.ActivityPaused);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task MapToCommonEvent_ActivityStopped_ReturnsNull()
    {
        var result = AndroidPlatformEvents.MapToCommonEvent(AndroidPlatformEvents.ActivityStopped);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task MapToCommonEvent_ActivityDestroyed_ReturnsNull()
    {
        var result = AndroidPlatformEvents.MapToCommonEvent(AndroidPlatformEvents.ActivityDestroyed);
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task MapToCommonEvent_UnknownEventId_ReturnsNull()
    {
        // 未定义的事件 ID 应返回 null
        var result = AndroidPlatformEvents.MapToCommonEvent(9999u);
        await Assert.That(result).IsNull();
    }

    // ---------------------------------------------------------------------
    // HasCommonMapping 测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task HasCommonMapping_MappedEvent_ReturnsTrue()
    {
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(AndroidPlatformEvents.ActivityCreated)).IsTrue();
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(AndroidPlatformEvents.ApplicationLowMemory)).IsTrue();
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(AndroidPlatformEvents.BatteryChanged)).IsTrue();
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(AndroidPlatformEvents.NetworkChanged)).IsTrue();
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(AndroidPlatformEvents.ThemeChanged)).IsTrue();
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(AndroidPlatformEvents.ScreenLocked)).IsTrue();
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(AndroidPlatformEvents.ScreenUnlocked)).IsTrue();
    }

    [Test]
    public async Task HasCommonMapping_UnmappedEvent_ReturnsFalse()
    {
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(AndroidPlatformEvents.ActivityStarted)).IsFalse();
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(AndroidPlatformEvents.ActivityResumed)).IsFalse();
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(AndroidPlatformEvents.ActivityPaused)).IsFalse();
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(AndroidPlatformEvents.ActivityStopped)).IsFalse();
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(AndroidPlatformEvents.ActivityDestroyed)).IsFalse();
    }

    [Test]
    public async Task HasCommonMapping_UnknownEventId_ReturnsFalse()
    {
        await Assert.That(AndroidPlatformEvents.HasCommonMapping(9999u)).IsFalse();
    }
}
