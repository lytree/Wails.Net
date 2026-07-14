using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// URL 白名单单元测试（TUnit）。
/// 对应主题 C：UrlWhitelist 通配符匹配、大小写不敏感、空 URL 处理。
/// </summary>
public sealed class UrlWhitelistTests
{
    [Test]
    public async Task IsAllowed_EmptyUrl_ReturnsFalse()
    {
        var whitelist = new UrlWhitelist();

        await Assert.That(whitelist.IsAllowed("")).IsFalse();
        await Assert.That(whitelist.IsAllowed(null!)).IsFalse();
    }

    [Test]
    public async Task IsAllowed_ExactMatch_ReturnsTrue()
    {
        var whitelist = new UrlWhitelist();
        whitelist.Add("https://example.com");

        await Assert.That(whitelist.IsAllowed("https://example.com")).IsTrue();
    }

    [Test]
    public async Task IsAllowed_WildcardMatch_ReturnsTrue()
    {
        var whitelist = new UrlWhitelist();
        whitelist.Add("https://*.example.com");

        await Assert.That(whitelist.IsAllowed("https://app.example.com")).IsTrue();
        await Assert.That(whitelist.IsAllowed("https://sub.app.example.com")).IsTrue();
    }

    [Test]
    public async Task IsAllowed_NonMatching_ReturnsFalse()
    {
        var whitelist = new UrlWhitelist();
        whitelist.Add("https://example.com");

        await Assert.That(whitelist.IsAllowed("https://evil.com")).IsFalse();
    }

    [Test]
    public async Task IsAllowed_CaseInsensitive_ReturnsTrue()
    {
        var whitelist = new UrlWhitelist();
        whitelist.Add("https://Example.com");

        await Assert.That(whitelist.IsAllowed("https://example.com")).IsTrue();
    }

    [Test]
    public async Task Add_EmptyPattern_ThrowsArgumentException()
    {
        var whitelist = new UrlWhitelist();

        await Assert.That(() => whitelist.Add("")).ThrowsExactly<ArgumentException>();
        await Assert.That(() => whitelist.Add(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Patterns_ReturnsAllAddedPatterns()
    {
        var whitelist = new UrlWhitelist();
        whitelist.Add("https://a.com");
        whitelist.Add("https://b.com");

        await Assert.That(whitelist.Patterns.Count).IsEqualTo(2);
    }
}
