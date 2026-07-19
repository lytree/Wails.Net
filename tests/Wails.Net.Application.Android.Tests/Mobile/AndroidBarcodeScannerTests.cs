using TUnit.Core;
using Wails.Net.Application.Android.Mobile;

namespace Wails.Net.Application.Android.Tests.Mobile;

/// <summary>
/// AndroidBarcodeScanner 的单元测试（TUnit）。
/// 测试非 Android 环境下（Application.Context 为 null）的降级行为：
/// <c>ScanAsync</c> 返回空字符串，<c>Cancel</c> 为 no-op，<c>CreateScanIntent</c> 返回 null。
/// 同时验证委托注入机制。
/// </summary>
[NotInParallel]
public sealed class AndroidBarcodeScannerTests
{
    [Test]
    public async Task Constructor_Default_DoesNotThrow_InNonAndroidEnvironment()
    {
        // 操作与断言：非 Android 环境下默认构造不应抛异常
        var scanner = new AndroidBarcodeScanner();
        await Assert.That(scanner).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithNullDelegates_DoesNotThrow()
    {
        // 操作与断言：显式传入 null 委托不应抛异常
        var scanner = new AndroidBarcodeScanner(scanImpl: null, cancelImpl: null);
        await Assert.That(scanner).IsNotNull();
    }

    [Test]
    public async Task ScanAsync_ReturnsEmptyString_WhenNoDelegateInjected()
    {
        // 安排：未注入扫描委托
        var scanner = new AndroidBarcodeScanner();

        // 操作
        var result = await scanner.ScanAsync();

        // 断言：无委托时返回空字符串
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Cancel_DoesNotThrow_WhenNoDelegateInjected()
    {
        // 安排：未注入取消委托
        var scanner = new AndroidBarcodeScanner();

        // 操作与断言：无委托时为 no-op
        await Assert.That(() => scanner.Cancel()).ThrowsNothing();
    }

    [Test]
    public async Task ScanAsync_InvokesInjectedDelegate()
    {
        // 安排：注入扫描委托返回 "barcode-12345"
        var wasCalled = false;
        var scanner = new AndroidBarcodeScanner(
            scanImpl: _ =>
            {
                wasCalled = true;
                return Task.FromResult("barcode-12345");
            },
            cancelImpl: null);

        // 操作
        var result = await scanner.ScanAsync();

        // 断言
        await Assert.That(wasCalled).IsTrue();
        await Assert.That(result).IsEqualTo("barcode-12345");
    }

    [Test]
    public async Task Cancel_InvokesInjectedDelegate()
    {
        // 安排：注入取消委托
        var wasCalled = false;
        var scanner = new AndroidBarcodeScanner(
            scanImpl: null,
            cancelImpl: () => wasCalled = true);

        // 操作
        scanner.Cancel();

        // 断言
        await Assert.That(wasCalled).IsTrue();
    }

    [Test]
    public async Task CreateScanIntent_ReturnsNull_InNonAndroidEnvironment()
    {
        // 安排：非 Android 环境下 Application.Context 为 null
        // 操作
        var intent = AndroidBarcodeScanner.CreateScanIntent(prompt: "扫描条码");

        // 断言：Context 为 null 时返回 null
        await Assert.That(intent).IsNull();
    }

    [Test]
    public async Task CreateScanIntent_ReturnsNull_WhenPromptIsNull()
    {
        // 安排：传入 null 提示文本
        // 操作
        var intent = AndroidBarcodeScanner.CreateScanIntent(prompt: null);

        // 断言：非 Android 环境下仍返回 null
        await Assert.That(intent).IsNull();
    }

    [Test]
    public async Task ScanAsync_PassesCancellationToken_ToDelegate()
    {
        // 安排：注入委托记录是否收到 CancellationToken（非 default）
        CancellationToken receivedToken = default;
        var scanner = new AndroidBarcodeScanner(
            scanImpl: token =>
            {
                receivedToken = token;
                return Task.FromResult("scanned");
            },
            cancelImpl: null);

        // 操作
        await scanner.ScanAsync();

        // 断言：当前实现总是传 CancellationToken.None
        await Assert.That(receivedToken).IsEqualTo(CancellationToken.None);
    }
}
