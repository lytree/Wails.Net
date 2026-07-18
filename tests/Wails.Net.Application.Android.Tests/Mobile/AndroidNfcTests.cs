using TUnit.Core;
using Wails.Net.Application.Android.Mobile;

namespace Wails.Net.Application.Android.Tests.Mobile;

/// <summary>
/// AndroidNfc 的单元测试（TUnit）。
/// 测试非 Android 环境下（NfcAdapter 为 null）的降级行为：
/// <c>IsAvailable</c> 返回 false，<c>ReadAsync</c> 返回空字符串，<c>WriteAsync</c> 为 no-op，<c>Cancel</c> 为 no-op。
/// </summary>
[NotInParallel]
public sealed class AndroidNfcTests
{
    [Test]
    public async Task Constructor_DoesNotThrow_InNonAndroidEnvironment()
    {
        // 操作与断言：非 Android 环境下构造不应抛异常
        var nfc = new AndroidNfc();
        await Assert.That(nfc).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullDelegates_DoesNotThrow()
    {
        // 操作与断言：显式传入 null 委托不应抛异常
        var nfc = new AndroidNfc(readImpl: null, writeImpl: null, cancelImpl: null);
        await Assert.That(nfc).IsNotNull();
    }

    [Test]
    public async Task IsAvailable_ReturnsFalse_InNonAndroidEnvironment()
    {
        // 安排：非 Android 环境下 NfcAdapter 为 null
        var nfc = new AndroidNfc();

        // 操作与断言
        await Assert.That(nfc.IsAvailable()).IsFalse();
    }

    [Test]
    public async Task ReadAsync_ReturnsEmptyString_WhenNoDelegateInjected()
    {
        // 安排：未注入读委托
        var nfc = new AndroidNfc();

        // 操作
        var result = await nfc.ReadAsync();

        // 断言：无委托时返回空字符串
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteAsync_Completes_WhenNoDelegateInjected()
    {
        // 安排：未注入写委托
        var nfc = new AndroidNfc();

        // 操作与断言：无委托时为 no-op，应正常完成
        await Assert.That(async () => await nfc.WriteAsync("test data")).ThrowsNothing();
    }

    [Test]
    public async Task Cancel_DoesNotThrow_WhenNoDelegateInjected()
    {
        // 安排：未注入取消委托
        var nfc = new AndroidNfc();

        // 操作与断言：无委托时为 no-op
        await Assert.That(() => nfc.Cancel()).ThrowsNothing();
    }

    [Test]
    public async Task ReadAsync_InvokesInjectedDelegate()
    {
        // 安排：注入读委托返回 "nfc-data"
        var wasCalled = false;
        var nfc = new AndroidNfc(
            readImpl: _ =>
            {
                wasCalled = true;
                return Task.FromResult("nfc-data");
            },
            writeImpl: null,
            cancelImpl: null);

        // 操作
        var result = await nfc.ReadAsync();

        // 断言
        await Assert.That(wasCalled).IsTrue();
        await Assert.That(result).IsEqualTo("nfc-data");
    }

    [Test]
    public async Task WriteAsync_InvokesInjectedDelegate()
    {
        // 安排：注入写委托记录写入数据
        string? writtenData = null;
        var nfc = new AndroidNfc(
            readImpl: null,
            writeImpl: (data, _) =>
            {
                writtenData = data;
                return Task.CompletedTask;
            },
            cancelImpl: null);

        // 操作
        await nfc.WriteAsync("hello-nfc");

        // 断言：委托被调用并收到正确数据
        await Assert.That(writtenData).IsEqualTo("hello-nfc");
    }

    [Test]
    public async Task Cancel_InvokesInjectedDelegate()
    {
        // 安排：注入取消委托
        var wasCalled = false;
        var nfc = new AndroidNfc(
            readImpl: null,
            writeImpl: null,
            cancelImpl: () => wasCalled = true);

        // 操作
        nfc.Cancel();

        // 断言
        await Assert.That(wasCalled).IsTrue();
    }

    [Test]
    public async Task WriteAsync_PassesEmptyData_WhenDelegateInjected()
    {
        // 安排：注入写委托记录写入数据
        string? writtenData = null;
        var nfc = new AndroidNfc(
            readImpl: null,
            writeImpl: (data, _) => { writtenData = data; return Task.CompletedTask; },
            cancelImpl: null);

        // 操作：传入空字符串
        await nfc.WriteAsync(string.Empty);

        // 断言：空字符串被正确传递
        await Assert.That(writtenData).IsEqualTo(string.Empty);
    }
}
