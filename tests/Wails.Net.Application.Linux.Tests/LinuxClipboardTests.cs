using TUnit.Core;
using Wails.Net.Application.Clipboard;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// LinuxClipboard 的单元测试（TUnit）。
/// 当前 LinuxClipboard 为存根实现，测试验证存根行为契约（不抛出异常、返回约定值）。
/// 完整的 GTK 剪贴板集成测试将在后续阶段实现。
/// </summary>
public sealed class LinuxClipboardTests
{
    /// <summary>
    /// 被测对象。
    /// </summary>
    private readonly LinuxClipboard _clipboard = new();

    [Test]
    public async Task SetText_DoesNotThrow()
    {
        // 操作与断言：当前桩实现不应抛出异常
        await Assert.That(() => _clipboard.SetText("Hello World")).ThrowsNothing();
    }

    [Test]
    public async Task GetText_ReturnsEmptyString()
    {
        // 操作
        var text = _clipboard.GetText();

        // 断言：桩实现返回空字符串
        await Assert.That(text).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SetHTML_DoesNotThrow()
    {
        // 操作与断言：当前桩实现不应抛出异常
        await Assert.That(() => _clipboard.SetHTML("<b>Hello HTML</b>", "fallback")).ThrowsNothing();
    }

    [Test]
    public async Task GetHTML_ReturnsEmptyString()
    {
        // 操作
        var html = _clipboard.GetHTML();

        // 断言：桩实现返回空字符串
        await Assert.That(html).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SetImage_DoesNotThrow_WithValidData()
    {
        // 安排：有效的图片字节数据（PNG 头部）
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        // 操作与断言：当前桩实现不应抛出异常
        await Assert.That(() => _clipboard.SetImage(imageData)).ThrowsNothing();
    }

    [Test]
    public async Task SetImage_DoesNotThrow_WithEmptyData()
    {
        // 安排：空数据
        var imageData = Array.Empty<byte>();

        // 操作与断言：当前桩实现不应抛出异常
        await Assert.That(() => _clipboard.SetImage(imageData)).ThrowsNothing();
    }

    [Test]
    public async Task GetImage_ReturnsNull()
    {
        // 操作
        var image = _clipboard.GetImage();

        // 断言：桩实现返回 null
        await Assert.That(image).IsNull();
    }

    [Test]
    public async Task Clear_DoesNotThrow()
    {
        // 操作与断言：当前桩实现不应抛出异常
        await Assert.That(() => _clipboard.Clear()).ThrowsNothing();
    }
}
