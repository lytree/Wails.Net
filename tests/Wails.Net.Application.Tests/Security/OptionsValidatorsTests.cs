using Microsoft.Extensions.Options;
using TUnit.Core;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// 核心 Options 启动期校验器单元测试（OPT-CONFIG-002）。
/// 覆盖 <see cref="DesktopHostOptionsValidator"/>、<see cref="PermissionOptionsValidator"/>、
/// <see cref="IpcOptionsValidator"/> 的合法/非法配置场景。
/// 对应优化路线图：错误配置应在 Host 启动期失败，错误消息能指出具体配置路径。
/// </summary>
[NotInParallel]
public sealed class OptionsValidatorsTests
{
    // ---------- DesktopHostOptionsValidator ----------

    [Test]
    public async Task DesktopHostOptions_ValidConfig_Succeeds()
    {
        var validator = new DesktopHostOptionsValidator();
        var options = new DesktopHostOptions
        {
            ApplicationName = "My App",
            Window = { Width = 1280, Height = 720 },
            DevServerUrl = "http://localhost:5173",
        };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task DesktopHostOptions_EmptyApplicationName_Fails()
    {
        var validator = new DesktopHostOptionsValidator();
        var options = new DesktopHostOptions { ApplicationName = "  " };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("Wails:ApplicationName"));
    }

    [Test]
    public async Task DesktopHostOptions_InvalidWindowWidth_Fails()
    {
        var validator = new DesktopHostOptionsValidator();
        var options = new DesktopHostOptions { Window = { Width = 0, Height = 720 } };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("Wails:Window:Width"));
    }

    [Test]
    public async Task DesktopHostOptions_InvalidWindowHeight_Fails()
    {
        var validator = new DesktopHostOptionsValidator();
        var options = new DesktopHostOptions { Window = { Width = 1280, Height = -1 } };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("Wails:Window:Height"));
    }

    [Test]
    public async Task DesktopHostOptions_EmptyDefaultDocument_Fails()
    {
        var validator = new DesktopHostOptionsValidator();
        var options = new DesktopHostOptions { Assets = { DefaultDocument = string.Empty } };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("Wails:Assets:DefaultDocument"));
    }

    [Test]
    public async Task DesktopHostOptions_InvalidDevServerUrl_Fails()
    {
        var validator = new DesktopHostOptionsValidator();
        var options = new DesktopHostOptions { DevServerUrl = "not-a-url" };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("Wails:DevServerUrl"));
    }

    [Test]
    public async Task DesktopHostOptions_FtpDevServerUrl_Fails()
    {
        var validator = new DesktopHostOptionsValidator();
        var options = new DesktopHostOptions { DevServerUrl = "ftp://example.com" };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("Wails:DevServerUrl"));
    }

    // ---------- PermissionOptionsValidator ----------

    [Test]
    public async Task PermissionOptions_ValidConfig_Succeeds()
    {
        var validator = new PermissionOptionsValidator();
        var options = new PermissionOptions
        {
            Permissions = { "core:default", "window:default" },
            CapabilitiesDirectory = "capabilities",
            Scopes = { ["fs:allow-read"] = new ScopeConfig() },
        };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task PermissionOptions_EmptyPermission_Fails()
    {
        var validator = new PermissionOptionsValidator();
        var options = new PermissionOptions { Permissions = { "core:default", " " } };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("Permissions:1"));
    }

    [Test]
    public async Task PermissionOptions_InvalidCapabilitiesDirectory_Fails()
    {
        var validator = new PermissionOptionsValidator();
        // 注意：不能使用 "\x00abilities"，因为 C# 的 \x 转义会贪婪消费后续十六进制数字
        // （'a'、'b' 均为十六进制），导致实际字面量为字符 0xAB 而非 NUL，无法被验证器捕获。
        // 必须使用 "\0"（明确的 NUL 转义）才能命中 Path.GetInvalidPathChars()。
        var options = new PermissionOptions { CapabilitiesDirectory = "cap\0abilities" };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("CapabilitiesDirectory"));
    }

    [Test]
    public async Task PermissionOptions_EmptyScopeKey_Fails()
    {
        var validator = new PermissionOptionsValidator();
        var options = new PermissionOptions { Scopes = { [string.Empty] = new ScopeConfig() } };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("Scopes"));
    }

    // ---------- IpcOptionsValidator ----------

    [Test]
    public async Task IpcOptions_ValidConfig_Succeeds()
    {
        var validator = new IpcOptionsValidator();
        var options = new IpcOptions
        {
            PortStart = 34115,
            PortEnd = 34215,
            AllowedOrigins = { "https://app.example.com", "https://*.example.com" },
        };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task IpcOptions_PortStartBelow1024_Fails()
    {
        var validator = new IpcOptionsValidator();
        var options = new IpcOptions { PortStart = 808, PortEnd = 9090 };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("PortStart"));
    }

    [Test]
    public async Task IpcOptions_PortEndBelowPortStart_Fails()
    {
        var validator = new IpcOptionsValidator();
        var options = new IpcOptions { PortStart = 34115, PortEnd = 34114 };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("PortEnd"));
    }

    [Test]
    public async Task IpcOptions_InvalidAllowedOrigin_Fails()
    {
        var validator = new IpcOptionsValidator();
        var options = new IpcOptions { AllowedOrigins = { "not-a-url" } };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("AllowedOrigins"));
    }

    [Test]
    public async Task IpcOptions_EmptyAllowedOrigin_Fails()
    {
        var validator = new IpcOptionsValidator();
        var options = new IpcOptions { AllowedOrigins = { "" } };

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.Failures).Contains(f => f.Contains("AllowedOrigins:0"));
    }
}
