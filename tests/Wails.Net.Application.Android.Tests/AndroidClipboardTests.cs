using TUnit.Core;
using Wails.Net.Application.Clipboard;

namespace Wails.Net.Application.Android.Tests;

/// <summary>
/// AndroidClipboard 的单元测试（TUnit）。
/// 测试非 Android 环境下（ClipboardManager 为 null）的降级行为。
/// 注意：在 Windows CI 环境下 <c>Android.App.Application.Context</c> 返回 null，
/// 导致 <c>_clipboardManager</c> 为 null，所有方法应降级到 no-op 或返回空值。
/// </summary>
[NotInParallel]
public sealed class AndroidClipboardTests
{
    [Test]
    public async Task Constructor_DoesNotThrow_InNonAndroidEnvironment()
    {
        // 操作与断言：非 Android 环境下构造不应抛异常，_clipboardManager 为 null
        var clipboard = new AndroidClipboard();
        await Assert.That(clipboard).IsNotNull();
    }

    [Test]
    public async Task SetText_DoesNotThrow_WhenClipboardManagerIsNull()
    {
        // 安排：非 Android 环境下 _clipboardManager 为 null
        var clipboard = new AndroidClipboard();

        // 操作与断言：null ClipboardManager 时为 no-op，不应抛异常
        await Assert.That(() => clipboard.SetText("hello")).ThrowsNothing();
    }

    [Test]
    public async Task GetText_ReturnsEmptyString_WhenClipboardManagerIsNull()
    {
        // 安排
        var clipboard = new AndroidClipboard();

        // 操作
        var text = clipboard.GetText();

        // 断言：null ClipboardManager 时返回空字符串
        await Assert.That(text).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SetHTML_DoesNotThrow_WhenClipboardManagerIsNull()
    {
        // 安排
        var clipboard = new AndroidClipboard();

        // 操作与断言：null ClipboardManager 时为 no-op
        await Assert.That(() => clipboard.SetHTML("<b>html</b>", "fallback")).ThrowsNothing();
    }

    [Test]
    public async Task GetHTML_ReturnsEmptyString_WhenClipboardManagerIsNull()
    {
        // 安排
        var clipboard = new AndroidClipboard();

        // 操作
        var html = clipboard.GetHTML();

        // 断言：null ClipboardManager 时返回空字符串
        await Assert.That(html).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SetImage_DoesNotThrow_AlwaysNoOp()
    {
        // 安排：骨架实现为 no-op
        var clipboard = new AndroidClipboard();
        var imageData = new byte[] { 1, 2, 3 };

        // 操作与断言：骨架实现不抛异常
        await Assert.That(() => clipboard.SetImage(imageData)).ThrowsNothing();
    }

    [Test]
    public async Task GetImage_ReturnsNull_AlwaysSkeletonImplementation()
    {
        // 安排：骨架实现返回 null
        var clipboard = new AndroidClipboard();

        // 操作
        var image = clipboard.GetImage();

        // 断言：骨架实现返回 null
        await Assert.That(image).IsNull();
    }

    [Test]
    public async Task SetFiles_DoesNotThrow_WhenNullFilesProvided()
    {
        // 安排
        var clipboard = new AndroidClipboard();

        // 操作与断言：传入 null 不应抛异常（提前返回）
        await Assert.That(() => clipboard.SetFiles(null!)).ThrowsNothing();
    }

    [Test]
    public async Task SetFiles_DoesNotThrow_WhenEmptyArrayProvided()
    {
        // 安排
        var clipboard = new AndroidClipboard();

        // 操作与断言：传入空数组不应抛异常（提前返回）
        await Assert.That(() => clipboard.SetFiles(System.Array.Empty<string>())).ThrowsNothing();
    }

    [Test]
    public async Task GetFiles_ReturnsEmptyArray_WhenClipboardManagerIsNull()
    {
        // 安排
        var clipboard = new AndroidClipboard();

        // 操作
        var files = clipboard.GetFiles();

        // 断言：null ClipboardManager 时返回空数组
        await Assert.That(files).IsNotNull();
        await Assert.That(files.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Clear_DoesNotThrow_WhenClipboardManagerIsNull()
    {
        // 安排
        var clipboard = new AndroidClipboard();

        // 操作与断言：null ClipboardManager 时为 no-op
        await Assert.That(() => clipboard.Clear()).ThrowsNothing();
    }
}
