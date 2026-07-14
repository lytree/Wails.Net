using System.Text.RegularExpressions;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.AssetServer.Security;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// NonceInjector 单元测试。
/// 覆盖 nonce 生成、HTML 注入、CSP 头构建。
/// 对应 Tauri v2 的 CSP nonce 防注入机制。
/// </summary>
[NotInParallel]
public sealed class NonceInjectorTests
{
    // === GenerateNonce ===

    [Test]
    public async Task GenerateNonce_ReturnsNonEmptyString()
    {
        var nonce = NonceInjector.GenerateNonce();
        await Assert.That(nonce).IsNotNull();
        await Assert.That(nonce.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task GenerateNonce_ReturnsBase64String()
    {
        // 32 字节随机数 base64 编码后为 44 字符（含 padding）
        var nonce = NonceInjector.GenerateNonce();
        await Assert.That(nonce.Length).IsEqualTo(44);
        // 验证是合法的 base64 字符串
        var bytes = Convert.FromBase64String(nonce);
        await Assert.That(bytes.Length).IsEqualTo(32);
    }

    [Test]
    public async Task GenerateNonce_EachCallIsUnique()
    {
        var nonce1 = NonceInjector.GenerateNonce();
        await Task.Delay(10);
        var nonce2 = NonceInjector.GenerateNonce();
        var nonce3 = NonceInjector.GenerateNonce();

        await Assert.That(nonce1).IsNotEqualTo(nonce2);
        await Assert.That(nonce2).IsNotEqualTo(nonce3);
        await Assert.That(nonce1).IsNotEqualTo(nonce3);
    }

    // === InjectNonce ===

    [Test]
    public async Task InjectNonce_AddsNonceToScriptTags()
    {
        var html = """<html><head><script src="app.js"></script></head></html>""";
        var nonce = "abc123";

        var result = NonceInjector.InjectNonce(html, nonce);

        await Assert.That(result).Contains($"nonce=\"{nonce}\"");
        await Assert.That(result).Contains("<script nonce=\"abc123\" src=\"app.js\">");
    }

    [Test]
    public async Task InjectNonce_DoesNotDuplicateNonceIfPresent()
    {
        var html = """<html><head><script nonce="existing" src="app.js"></script></head></html>""";
        var nonce = "newNonce";

        var result = NonceInjector.InjectNonce(html, nonce);

        // 已有 nonce 的标签不应被注入新的 nonce
        await Assert.That(result).Contains("nonce=\"existing\"");
        await Assert.That(result).DoesNotContain("nonce=\"newNonce\"");
    }

    [Test]
    public async Task InjectNonce_AddsNonceToLinkStylesheetTags()
    {
        var html = """<html><head><link rel="stylesheet" href="style.css"></head></html>""";
        var nonce = "testNonce";

        var result = NonceInjector.InjectNonce(html, nonce);

        await Assert.That(result).Contains($"nonce=\"{nonce}\"");
        await Assert.That(result).Contains("<link nonce=\"testNonce\" rel=\"stylesheet\" href=\"style.css\">");
    }

    [Test]
    public async Task InjectNonce_DoesNotAddNonceToLinkWithoutStylesheet()
    {
        var html = """<html><head><link rel="icon" href="favicon.ico"></head></html>""";
        var nonce = "testNonce";

        var result = NonceInjector.InjectNonce(html, nonce);

        await Assert.That(result).DoesNotContain($"nonce=\"{nonce}\"");
    }

    [Test]
    public async Task InjectNonce_MultipleScriptTags_AllGetNonce()
    {
        var html = """
            <html>
            <head>
            <script src="a.js"></script>
            <script src="b.js"></script>
            <script>console.log("inline");</script>
            </head>
            </html>
            """;
        var nonce = "multiNonce";

        var result = NonceInjector.InjectNonce(html, nonce);

        // 统计 nonce 出现次数：应为 3（每个 script 标签一次）
        var nonceCount = Regex.Matches(result, $"nonce=\"{nonce}\"").Count;
        await Assert.That(nonceCount).IsEqualTo(3);
    }

    [Test]
    public async Task InjectNonce_EmptyHtml_ReturnsEmpty()
    {
        var result = NonceInjector.InjectNonce(string.Empty, "nonce");
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task InjectNonce_NullHtml_ThrowsArgumentNullException()
    {
        await Assert.That(() => NonceInjector.InjectNonce(null!, "nonce"))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task InjectNonce_NullNonce_ThrowsArgumentNullException()
    {
        await Assert.That(() => NonceInjector.InjectNonce("<html></html>", null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task InjectNonce_NoScriptTags_ReturnsUnchanged()
    {
        var html = """<html><body><p>No scripts here</p></body></html>""";
        var result = NonceInjector.InjectNonce(html, "nonce");
        await Assert.That(result).IsEqualTo(html);
    }

    // === BuildCspHeader ===

    [Test]
    public async Task BuildCspHeader_WithExistingScriptSrc_AppendsNonce()
    {
        var baseCsp = "default-src 'self'; script-src 'self'; style-src 'self'";
        var nonce = "abc123";

        var result = NonceInjector.BuildCspHeader(baseCsp, nonce);

        await Assert.That(result).Contains($"'nonce-{nonce}'");
        // 应在 script-src 指令后追加，不应破坏其他指令
        await Assert.That(result).Contains("style-src 'self'");
    }

    [Test]
    public async Task BuildCspHeader_WithoutScriptSrc_AddsNewScriptSrcDirective()
    {
        var baseCsp = "default-src 'self'";
        var nonce = "newNonce";

        var result = NonceInjector.BuildCspHeader(baseCsp, nonce);

        await Assert.That(result).Contains($"script-src 'self' 'nonce-{nonce}'");
        await Assert.That(result).Contains("default-src 'self';");
    }

    [Test]
    public async Task BuildCspHeader_NullBaseCsp_ReturnsNonceOnlyScriptSrc()
    {
        var nonce = "onlyNonce";
        var result = NonceInjector.BuildCspHeader(null, nonce);

        await Assert.That(result).IsEqualTo($"script-src 'self' 'nonce-{nonce}'");
    }

    [Test]
    public async Task BuildCspHeader_EmptyBaseCsp_ReturnsNonceOnlyScriptSrc()
    {
        var nonce = "emptyCsp";
        var result = NonceInjector.BuildCspHeader(string.Empty, nonce);

        await Assert.That(result).IsEqualTo($"script-src 'self' 'nonce-{nonce}'");
    }

    [Test]
    public async Task BuildCspHeader_NullNonce_ThrowsArgumentNullException()
    {
        await Assert.That(() => NonceInjector.BuildCspHeader("script-src 'self'", null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task BuildCspHeader_PreservesScriptSrcPosition()
    {
        var baseCsp = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self'";
        var nonce = "posNonce";

        var result = NonceInjector.BuildCspHeader(baseCsp, nonce);

        // nonce 应追加在 script-src 指令的末尾（即在 style-src 之前的分号处）
        await Assert.That(result).Contains($"'unsafe-inline' 'nonce-{nonce}'");
    }
}
