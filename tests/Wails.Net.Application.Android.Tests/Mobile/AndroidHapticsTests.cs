using TUnit.Core;
using Wails.Net.Application.Android.Mobile;
using Wails.Net.Application.Plugins.Mobile;

namespace Wails.Net.Application.Android.Tests.Mobile;

/// <summary>
/// AndroidHaptics 的单元测试（TUnit）。
/// 测试非 Android 环境下（Vibrator 为 null）的降级行为。
/// 注意：在 Windows CI 环境下 <c>Android.App.Application.Context</c> 返回 null，
/// 导致 <c>_vibrator</c> 为 null，所有方法应降级为 no-op。
/// </summary>
[NotInParallel]
public sealed class AndroidHapticsTests
{
    [Test]
    public async Task Constructor_DoesNotThrow_InNonAndroidEnvironment()
    {
        // 操作与断言：非 Android 环境下构造不应抛异常，_vibrator 为 null
        var haptics = new AndroidHaptics();
        await Assert.That(haptics).IsNotNull();
    }

    [Test]
    public async Task Vibrate_DoesNotThrow_WhenVibratorIsNull()
    {
        // 安排：非 Android 环境下 _vibrator 为 null
        var haptics = new AndroidHaptics();

        // 操作与断言：null Vibrator 时为 no-op，不应抛异常
        await Assert.That(() => haptics.Vibrate(200)).ThrowsNothing();
    }

    [Test]
    public async Task Vibrate_DoesNotThrow_WhenDurationIsZeroOrNegative()
    {
        // 安排
        var haptics = new AndroidHaptics();

        // 操作与断言：duration <= 0 时跳过（不应抛异常）
        await Assert.That(() => haptics.Vibrate(0)).ThrowsNothing();
        await Assert.That(() => haptics.Vibrate(-100)).ThrowsNothing();
    }

    [Test]
    public async Task Cancel_DoesNotThrow_WhenVibratorIsNull()
    {
        // 安排
        var haptics = new AndroidHaptics();

        // 操作与断言：null Vibrator 时为 no-op
        await Assert.That(() => haptics.Cancel()).ThrowsNothing();
    }

    [Test]
    public async Task Notify_DoesNotThrow_WhenVibratorIsNull()
    {
        // 安排
        var haptics = new AndroidHaptics();

        // 操作与断言：所有 NotificationType 枚举值都不会抛异常
        foreach (var type in Enum.GetValues<NotificationType>())
        {
            await Assert.That(() => haptics.Notify(type)).ThrowsNothing();
        }
    }

    [Test]
    public async Task Notify_Success_DoesNotThrow_InNonAndroidEnvironment()
    {
        // 安排
        var haptics = new AndroidHaptics();

        // 操作与断言：success 类型对应 50ms 震动
        await Assert.That(() => haptics.Notify(NotificationType.Success)).ThrowsNothing();
    }

    [Test]
    public async Task Notify_Warning_DoesNotThrow_InNonAndroidEnvironment()
    {
        var haptics = new AndroidHaptics();
        await Assert.That(() => haptics.Notify(NotificationType.Warning)).ThrowsNothing();
    }

    [Test]
    public async Task Notify_Error_DoesNotThrow_InNonAndroidEnvironment()
    {
        var haptics = new AndroidHaptics();
        await Assert.That(() => haptics.Notify(NotificationType.Error)).ThrowsNothing();
    }
}
