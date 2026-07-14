using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// CORS 选项单元测试（TUnit）。
/// 对应主题 C：CorsOptions ResolveAllowedOrigin 本地源、白名单源、非法源。
/// </summary>
public sealed class CorsOptionsTests
{
    [Test]
    public async Task ResolveAllowedOrigin_Disabled_ReturnsNull()
    {
        var cors = new CorsOptions { Enabled = false };

        await Assert.That(cors.ResolveAllowedOrigin("https://example.com")).IsNull();
    }

    [Test]
    public async Task ResolveAllowedOrigin_NullOrigin_ReturnsNull()
    {
        var cors = new CorsOptions();

        await Assert.That(cors.ResolveAllowedOrigin(null)).IsNull();
        await Assert.That(cors.ResolveAllowedOrigin("")).IsNull();
    }

    [Test]
    public async Task ResolveAllowedOrigin_Localhost_ReturnsOrigin()
    {
        var cors = new CorsOptions();

        await Assert.That(cors.ResolveAllowedOrigin("http://localhost:34115")).IsEqualTo("http://localhost:34115");
        await Assert.That(cors.ResolveAllowedOrigin("http://127.0.0.1:34115")).IsEqualTo("http://127.0.0.1:34115");
    }

    [Test]
    public async Task ResolveAllowedOrigin_WailsOrigin_ReturnsOrigin()
    {
        var cors = new CorsOptions();

        await Assert.That(cors.ResolveAllowedOrigin("wails://localhost")).IsEqualTo("wails://localhost");
        await Assert.That(cors.ResolveAllowedOrigin("https://wails.localhost")).IsEqualTo("https://wails.localhost");
    }

    [Test]
    public async Task ResolveAllowedOrigin_WhitelistedOrigin_ReturnsOrigin()
    {
        var cors = new CorsOptions
        {
            AllowedOrigins = { "https://app.example.com" }
        };

        await Assert.That(cors.ResolveAllowedOrigin("https://app.example.com")).IsEqualTo("https://app.example.com");
    }

    [Test]
    public async Task ResolveAllowedOrigin_NonWhitelistedOrigin_ReturnsNull()
    {
        var cors = new CorsOptions
        {
            AllowedOrigins = { "https://app.example.com" }
        };

        await Assert.That(cors.ResolveAllowedOrigin("https://evil.com")).IsNull();
    }

    [Test]
    public async Task ResolveAllowedOrigin_EmptyWhitelist_LocalOriginStillAllowed()
    {
        // 安排：AllowedOrigins 为空，但本地源仍应允许
        var cors = new CorsOptions();

        await Assert.That(cors.ResolveAllowedOrigin("http://localhost:8080")).IsEqualTo("http://localhost:8080");
    }

    [Test]
    public async Task Default_AllowedMethods_ContainsGetPostOptions()
    {
        var cors = new CorsOptions();

        await Assert.That(cors.AllowedMethods).Contains("GET");
        await Assert.That(cors.AllowedMethods).Contains("POST");
        await Assert.That(cors.AllowedMethods).Contains("OPTIONS");
    }
}
