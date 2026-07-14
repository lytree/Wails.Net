using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// IPC 来源校验器单元测试（TUnit）。
/// 对应主题 C：IpcOriginValidator 本地源放行、白名单源匹配、非法源拒绝。
/// </summary>
public sealed class IpcOriginValidatorTests
{
    [Test]
    public async Task Validate_NullOrigin_ReturnsTrue()
    {
        // 安排
        var whitelist = new UrlWhitelist();
        var validator = new IpcOriginValidator(whitelist);

        // 操作与断言：空 Origin 总是允许（WebView 内部通信）
        await Assert.That(validator.Validate(null)).IsTrue();
        await Assert.That(validator.Validate("")).IsTrue();
    }

    [Test]
    public async Task Validate_LocalhostOrigin_ReturnsTrue()
    {
        var validator = new IpcOriginValidator(new UrlWhitelist());

        await Assert.That(validator.Validate("http://localhost:34115")).IsTrue();
        await Assert.That(validator.Validate("http://127.0.0.1:34115")).IsTrue();
    }

    [Test]
    public async Task Validate_WailsOrigin_ReturnsTrue()
    {
        var validator = new IpcOriginValidator(new UrlWhitelist());

        await Assert.That(validator.Validate("wails://localhost")).IsTrue();
        await Assert.That(validator.Validate("https://wails.localhost")).IsTrue();
    }

    [Test]
    public async Task Validate_WhitelistedOrigin_ReturnsTrue()
    {
        // 安排：白名单包含外部源
        var whitelist = new UrlWhitelist();
        whitelist.Add("https://*.example.com");
        var validator = new IpcOriginValidator(whitelist);

        // 操作与断言
        await Assert.That(validator.Validate("https://app.example.com")).IsTrue();
    }

    [Test]
    public async Task Validate_NonWhitelistedOrigin_ReturnsFalse()
    {
        // 安排：白名单为空
        var validator = new IpcOriginValidator(new UrlWhitelist());

        // 操作与断言：非本地源且不在白名单中
        await Assert.That(validator.Validate("https://evil.com")).IsFalse();
    }

    [Test]
    public async Task Constructor_NullWhitelist_ThrowsArgumentNullException()
    {
        await Assert.That(() => new IpcOriginValidator(null!)).ThrowsExactly<ArgumentNullException>();
    }
}
