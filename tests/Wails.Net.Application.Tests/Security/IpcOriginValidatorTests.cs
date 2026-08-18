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

    // 审计 §3.4：精确化本地源校验后的绕过串测试。
    // 原 IsLocalOrigin 使用 StartsWith("http://localhost") 等前缀匹配，会被
    // 同源异义字符串绕过。下列用例验证 Uri.TryCreate + 严格 Host 比对的兜底能力。

    [Test]
    public async Task Validate_HttpLocalhostWithEvilSuffix_ReturnsFalse()
    {
        // 攻击串：http://localhost.evil.example —— 前缀像 localhost 但 host 是 evil.example
        var validator = new IpcOriginValidator(new UrlWhitelist());
        await Assert.That(validator.Validate("http://localhost.evil.example")).IsFalse();
    }

    [Test]
    public async Task Validate_HttpIPv4LoopbackWithEvilSuffix_ReturnsFalse()
    {
        // 攻击串：http://127.0.0.1.evil.example —— 127.0.0.1 后接 .evil.example
        var validator = new IpcOriginValidator(new UrlWhitelist());
        await Assert.That(validator.Validate("http://127.0.0.1.evil.example")).IsFalse();
    }

    [Test]
    public async Task Validate_HttpLocalhostNoSuffix_ReturnsFalse()
    {
        // 攻击串：http://localhostfoo —— 前缀像 localhost 但 host 是 localhostfoo
        var validator = new IpcOriginValidator(new UrlWhitelist());
        await Assert.That(validator.Validate("http://localhostfoo")).IsFalse();
    }

    [Test]
    public async Task Validate_HttpsWailsLocalhostWithEvilSuffix_ReturnsFalse()
    {
        // 攻击串：https://wails.localhost.evil.example
        var validator = new IpcOriginValidator(new UrlWhitelist());
        await Assert.That(validator.Validate("https://wails.localhost.evil.example")).IsFalse();
    }

    [Test]
    public async Task Validate_HttpLocalhostUserInfoTrick_ReturnsFalse()
    {
        // 攻击串：http://localhost@evil.com —— userinfo 拆分后 Host=evil.com
        var validator = new IpcOriginValidator(new UrlWhitelist());
        await Assert.That(validator.Validate("http://localhost@evil.com")).IsFalse();
    }

    [Test]
    public async Task Validate_HttpsLocalhostWithoutWailsPrefix_ReturnsFalse()
    {
        // https scheme 下只信任 wails.localhost，普通 localhost 不放行。
        var validator = new IpcOriginValidator(new UrlWhitelist());
        await Assert.That(validator.Validate("https://localhost")).IsFalse();
    }

    [Test]
    public async Task Validate_HttpIPv6Loopback_ReturnsTrue()
    {
        // 正确正向：IPv6 loopback
        var validator = new IpcOriginValidator(new UrlWhitelist());
        await Assert.That(validator.Validate("http://[::1]:8080")).IsTrue();
    }

    [Test]
    public async Task Validate_HttpsWailsLocalhostWithPort_ReturnsTrue()
    {
        // 正确正向：https wails.localhost 带端口
        var validator = new IpcOriginValidator(new UrlWhitelist());
        await Assert.That(validator.Validate("https://wails.localhost:8443")).IsTrue();
    }

    [Test]
    public async Task Validate_WailsSchemeArbitraryPath_ReturnsTrue()
    {
        // 正确正向：wails:// 自定义 scheme 视为整 scheme 可信
        var validator = new IpcOriginValidator(new UrlWhitelist());
        await Assert.That(validator.Validate("wails://anything/with/path")).IsTrue();
    }

    [Test]
    public async Task Validate_MalformedOriginNoScheme_ReturnsFalse()
    {
        // 解析失败（非绝对 URI）一律拒绝
        var validator = new IpcOriginValidator(new UrlWhitelist());
        await Assert.That(validator.Validate("not-a-url")).IsFalse();
        await Assert.That(validator.Validate("http://")).IsFalse();
    }

    [Test]
    public async Task Validate_IframeWithLocalOriginButEvilTop_ReturnsFalse()
    {
        // iframe 场景：Origin 是 localhost 但 TopOrigin 是绕过串，整体应拒。
        var info = new OriginInfo
        {
            Origin = "http://localhost",
            TopOrigin = "http://localhost.evil.example",
            IsMainFrame = false,
        };
        var validator = new IpcOriginValidator(new UrlWhitelist());
        await Assert.That(validator.Validate(info)).IsFalse();
    }

    [Test]
    public async Task Validate_IframeWithBothLocalOrigins_ReturnsTrue()
    {
        // iframe 场景：Origin 与 TopOrigin 都是合法的 loopback，整体应放行。
        var info = new OriginInfo
        {
            Origin = "http://localhost:3000",
            TopOrigin = "http://127.0.0.1:3000",
            IsMainFrame = false,
        };
        var validator = new IpcOriginValidator(new UrlWhitelist());
        await Assert.That(validator.Validate(info)).IsTrue();
    }
}
