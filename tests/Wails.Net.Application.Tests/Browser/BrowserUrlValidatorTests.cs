using TUnit.Core;
using Wails.Net.Application.Browser;

namespace Wails.Net.Application.Tests.Browser;

/// <summary>
/// <see cref="BrowserUrlValidator"/> 单元测试（TUnit）。
/// 验证 URL 协议白名单（http/https/mailto/tel）和拒绝危险协议（file:/javascript:/data:）的逻辑。
/// 对应 Wails v3 Go 版本 messageprocessor_browser.go 中 ValidateAndSanitizeURL 的行为契约。
/// </summary>
[NotInParallel]
public sealed class BrowserUrlValidatorTests
{
    // ---------------------------------------------------------------------
    // 允许的协议（http / https / mailto / tel）
    // ---------------------------------------------------------------------

    [Test]
    public async Task TryValidate_HttpUrl_ReturnsTrue()
    {
        var ok = BrowserUrlValidator.TryValidate("http://example.com/path?q=1", out var sanitized);

        await Assert.That(ok).IsTrue();
        await Assert.That(sanitized).IsEqualTo("http://example.com/path?q=1");
    }

    [Test]
    public async Task TryValidate_HttpsUrl_ReturnsTrue()
    {
        var ok = BrowserUrlValidator.TryValidate("https://example.com/", out var sanitized);

        await Assert.That(ok).IsTrue();
        await Assert.That(sanitized).IsEqualTo("https://example.com/");
    }

    [Test]
    public async Task TryValidate_MailtoScheme_ReturnsTrue()
    {
        var ok = BrowserUrlValidator.TryValidate("mailto:user@example.com", out var sanitized);

        await Assert.That(ok).IsTrue();
        await Assert.That(sanitized).IsEqualTo("mailto:user@example.com");
    }

    [Test]
    public async Task TryValidate_TelScheme_ReturnsTrue()
    {
        var ok = BrowserUrlValidator.TryValidate("tel:+8613800138000", out var sanitized);

        await Assert.That(ok).IsTrue();
        await Assert.That(sanitized).IsEqualTo("tel:+8613800138000");
    }

    [Test]
    public async Task TryValidate_UpperCaseScheme_NormalizedAndAccepted()
    {
        // 大小写不敏感：HTTPS 也应接受，并规范化为 https
        var ok = BrowserUrlValidator.TryValidate("HTTPS://example.com/", out var sanitized);

        await Assert.That(ok).IsTrue();
        await Assert.That(sanitized?.StartsWith("https://", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    // ---------------------------------------------------------------------
    // 危险协议拒绝（file / javascript / data）
    // ---------------------------------------------------------------------

    [Test]
    public async Task TryValidate_FileScheme_ReturnsFalse()
    {
        var ok = BrowserUrlValidator.TryValidate("file:///C:/Windows/System32/evil.exe", out var sanitized);

        await Assert.That(ok).IsFalse();
        await Assert.That(sanitized).IsNull();
    }

    [Test]
    public async Task TryValidate_JavaScriptScheme_ReturnsFalse()
    {
        var ok = BrowserUrlValidator.TryValidate("javascript:alert(1)", out var sanitized);

        await Assert.That(ok).IsFalse();
        await Assert.That(sanitized).IsNull();
    }

    [Test]
    public async Task TryValidate_DataScheme_ReturnsFalse()
    {
        var ok = BrowserUrlValidator.TryValidate("data:text/html,<script>alert(1)</script>", out var sanitized);

        await Assert.That(ok).IsFalse();
        await Assert.That(sanitized).IsNull();
    }

    [Test]
    public async Task TryValidate_UnknownCustomScheme_ReturnsFalse()
    {
        var ok = BrowserUrlValidator.TryValidate("myapp://deep-link/page", out var sanitized);

        await Assert.That(ok).IsFalse();
        await Assert.That(sanitized).IsNull();
    }

    // ---------------------------------------------------------------------
    // 空值与格式错误
    // ---------------------------------------------------------------------

    [Test]
    public async Task TryValidate_NullUrl_ReturnsFalse()
    {
        var ok = BrowserUrlValidator.TryValidate(null, out var sanitized);

        await Assert.That(ok).IsFalse();
        await Assert.That(sanitized).IsNull();
    }

    [Test]
    public async Task TryValidate_EmptyString_ReturnsFalse()
    {
        var ok = BrowserUrlValidator.TryValidate(string.Empty, out var sanitized);

        await Assert.That(ok).IsFalse();
        await Assert.That(sanitized).IsNull();
    }

    [Test]
    public async Task TryValidate_WhitespaceOnly_ReturnsFalse()
    {
        var ok = BrowserUrlValidator.TryValidate("   ", out var sanitized);

        await Assert.That(ok).IsFalse();
        await Assert.That(sanitized).IsNull();
    }

    [Test]
    public async Task TryValidate_RelativeUrl_ReturnsFalse()
    {
        // 相对 URL 不是绝对路径，Uri.TryCreate 会成功但 Scheme 为空
        var ok = BrowserUrlValidator.TryValidate("/path/to/page", out var sanitized);

        await Assert.That(ok).IsFalse();
        await Assert.That(sanitized).IsNull();
    }

    [Test]
    public async Task TryValidate_BareDomainWithoutScheme_ReturnsFalse()
    {
        // 没有协议的纯域名（如 example.com）应拒绝，防止浏览器将其视为相对路径或本地文件
        var ok = BrowserUrlValidator.TryValidate("example.com", out var sanitized);

        await Assert.That(ok).IsFalse();
        await Assert.That(sanitized).IsNull();
    }

    // ---------------------------------------------------------------------
    // 边界与规范化行为
    // ---------------------------------------------------------------------

    [Test]
    public async Task TryValidate_TrimsWhitespace_AroundValidUrl()
    {
        // 应去除首尾空白后验证
        var ok = BrowserUrlValidator.TryValidate("  https://example.com/  ", out var sanitized);

        await Assert.That(ok).IsTrue();
        await Assert.That(sanitized).IsEqualTo("https://example.com/");
    }

    [Test]
    public async Task TryValidate_HttpUrlWithPort_ReturnsTrue()
    {
        var ok = BrowserUrlValidator.TryValidate("http://localhost:8080/", out var sanitized);

        await Assert.That(ok).IsTrue();
        await Assert.That(sanitized).IsEqualTo("http://localhost:8080/");
    }

    [Test]
    public async Task TryValidate_HttpsUrlWithQueryAndFragment_ReturnsTrue()
    {
        var ok = BrowserUrlValidator.TryValidate("https://example.com/path?q=1&lang=zh#section", out var sanitized);

        await Assert.That(ok).IsTrue();
        await Assert.That(sanitized).IsEqualTo("https://example.com/path?q=1&lang=zh#section");
    }

    [Test]
    public async Task TryValidate_MailtoWithSubject_ReturnsTrue()
    {
        var ok = BrowserUrlValidator.TryValidate("mailto:user@example.com?subject=Hello", out var sanitized);

        await Assert.That(ok).IsTrue();
        await Assert.That(sanitized).IsEqualTo("mailto:user@example.com?subject=Hello");
    }
}
