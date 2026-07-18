using TUnit.Core;
using Wails.Net.Application.Android.Mobile;

namespace Wails.Net.Application.Android.Tests.Mobile;

/// <summary>
/// AndroidBiometric 的单元测试（TUnit）。
/// 测试非 Android 环境下（Context 为 null）的降级行为：
/// <c>CheckAvailability</c> 返回 <c>none</c>，<c>AuthenticateAsync</c> 返回 false。
/// </summary>
[NotInParallel]
public sealed class AndroidBiometricTests
{
    [Test]
    public async Task Constructor_DoesNotThrow_InNonAndroidEnvironment()
    {
        // 操作与断言：非 Android 环境下构造不应抛异常
        var biometric = new AndroidBiometric();
        await Assert.That(biometric).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullDelegate_DoesNotThrow()
    {
        // 操作与断言：显式传入 null 委托不应抛异常
        var biometric = new AndroidBiometric(authenticateImpl: null);
        await Assert.That(biometric).IsNotNull();
    }

    [Test]
    public async Task CheckAvailability_ReturnsNone_InNonAndroidEnvironment()
    {
        // 安排：非 Android 环境下 Context 为 null
        var biometric = new AndroidBiometric();

        // 操作
        var availability = biometric.CheckAvailability();

        // 断言：null Context 时返回 "none"
        await Assert.That(availability).IsEqualTo("none");
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFalse_WhenNoDelegateInjected()
    {
        // 安排：未注入认证委托
        var biometric = new AndroidBiometric();

        // 操作
        var result = await biometric.AuthenticateAsync("test reason");

        // 断言：无委托时返回 false
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task AuthenticateAsync_ReturnsFalse_WhenNullDelegateInjected()
    {
        // 安排：显式注入 null 委托
        var biometric = new AndroidBiometric(authenticateImpl: null);

        // 操作
        var result = await biometric.AuthenticateAsync("test reason");

        // 断言
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task AuthenticateAsync_InvokesInjectedDelegate()
    {
        // 安排：注入返回 true 的委托
        var wasCalled = false;
        var biometric = new AndroidBiometric(_ =>
        {
            wasCalled = true;
            return Task.FromResult(true);
        });

        // 操作
        var result = await biometric.AuthenticateAsync("test reason");

        // 断言：委托被调用并返回 true
        await Assert.That(wasCalled).IsTrue();
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task AuthenticateAsync_InvokesInjectedDelegate_ReturningFalse()
    {
        // 安排：注入返回 false 的委托
        var biometric = new AndroidBiometric(_ => Task.FromResult(false));

        // 操作
        var result = await biometric.AuthenticateAsync("test reason");

        // 断言
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task AuthenticateAsync_PassesReason_IgnoresWhenDelegateIsNull()
    {
        // 安排：无委托时 reason 参数被忽略（不抛异常）
        var biometric = new AndroidBiometric();

        // 操作与断言：传入任意 reason 不应抛异常
        await Assert.That(async () => await biometric.AuthenticateAsync("any reason")).ThrowsNothing();
    }
}
