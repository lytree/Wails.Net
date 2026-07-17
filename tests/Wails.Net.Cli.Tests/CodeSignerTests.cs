using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// 代码签名器单元测试。
/// 验证 signtool/AzureSignTool 参数构建与环境变量解析逻辑，不依赖真实签名工具。
/// </summary>
[NotInParallel]
public sealed class CodeSignerTests
{
    /// <summary>
    /// 涉及的所有签名环境变量名，用于测试后清理。
    /// </summary>
    private static readonly string[] SignEnvVars =
    {
        "WAILS_SIGN_BACKEND",
        "WAILS_SIGN_CERT_PATH",
        "WAILS_SIGN_CERT_PASSWORD",
        "WAILS_SIGN_AKV_URL",
        "WAILS_SIGN_AKV_CERT",
        "WAILS_SIGN_AZURE_CLIENT_ID",
        "WAILS_SIGN_AZURE_CLIENT_SECRET",
        "WAILS_SIGN_AZURE_TENANT_ID",
        "WAILS_SIGN_TIMESTAMP_URL",
    };

    /// <summary>
    /// 清理所有签名环境变量。
    /// </summary>
    private static void ClearSignEnvVars()
    {
        foreach (var name in SignEnvVars)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    #region BuildSigntoolArgs

    [Test]
    public async Task BuildSigntoolArgs_ContainsCertPath()
    {
        var options = new SignOptions
        {
            Backend = SignBackend.Signtool,
            CertificatePath = "cert.pfx",
            CertificatePassword = "secret",
            TimestampUrl = "http://ts.example.com",
        };

        var args = CodeSigner.BuildSigntoolArgs("app.exe", options);

        await Assert.That(args).Contains("/f \"cert.pfx\"");
        await Assert.That(args).Contains("/p \"secret\"");
        await Assert.That(args).Contains("\"app.exe\"");
    }

    [Test]
    public async Task BuildSigntoolArgs_ContainsTimestampAndDigest()
    {
        var options = new SignOptions
        {
            Backend = SignBackend.Signtool,
            CertificatePath = "cert.pfx",
            CertificatePassword = "pw",
            TimestampUrl = "http://timestamp.digicert.com",
        };

        var args = CodeSigner.BuildSigntoolArgs("output.exe", options);

        await Assert.That(args).Contains("/tr \"http://timestamp.digicert.com\"");
        await Assert.That(args).Contains("/td sha256");
        await Assert.That(args).Contains("/fd sha256");
        await Assert.That(args).StartsWith("sign ");
    }

    #endregion

    #region BuildAzureSignToolArgs

    [Test]
    public async Task BuildAzureSignToolArgs_ContainsKeyVaultParams()
    {
        var options = new SignOptions
        {
            Backend = SignBackend.AzureSignTool,
            KeyVaultUrl = "https://kv.vault.azure.net",
            KeyVaultCertificateName = "my-cert",
            AzureClientId = "client-id",
            AzureClientSecret = "client-secret",
            AzureTenantId = "tenant-id",
            TimestampUrl = "http://ts.example.com",
        };

        var args = CodeSigner.BuildAzureSignToolArgs("app.exe", options);

        await Assert.That(args).Contains("-kvu \"https://kv.vault.azure.net\"");
        await Assert.That(args).Contains("-kvc \"my-cert\"");
        await Assert.That(args).Contains("-kvi \"client-id\"");
        await Assert.That(args).Contains("-kvs \"client-secret\"");
        await Assert.That(args).Contains("-kvt \"tenant-id\"");
        await Assert.That(args).Contains("-tr \"http://ts.example.com\"");
        await Assert.That(args).Contains("-td sha256");
        await Assert.That(args).Contains("-fd sha256");
        await Assert.That(args).Contains("\"app.exe\"");
    }

    #endregion

    #region ResolveFromEnvironment

    [Test]
    public async Task ResolveFromEnvironment_NoEnvVars_ReturnsNull()
    {
        ClearSignEnvVars();
        try
        {
            var result = CodeSigner.ResolveFromEnvironment();

            await Assert.That(result).IsNull();
        }
        finally
        {
            ClearSignEnvVars();
        }
    }

    [Test]
    public async Task ResolveFromEnvironment_WithSigntoolVars_ReturnsOptions()
    {
        ClearSignEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("WAILS_SIGN_BACKEND", "signtool");
            Environment.SetEnvironmentVariable("WAILS_SIGN_CERT_PATH", "/path/to/cert.pfx");
            Environment.SetEnvironmentVariable("WAILS_SIGN_CERT_PASSWORD", "p@ssw0rd");

            var result = CodeSigner.ResolveFromEnvironment();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Backend).IsEqualTo(SignBackend.Signtool);
            await Assert.That(result.CertificatePath).IsEqualTo("/path/to/cert.pfx");
            await Assert.That(result.CertificatePassword).IsEqualTo("p@ssw0rd");
            await Assert.That(result.TimestampUrl).IsEqualTo("http://timestamp.digicert.com");
        }
        finally
        {
            ClearSignEnvVars();
        }
    }

    [Test]
    public async Task ResolveFromEnvironment_WithAzureSigntoolVars_ReturnsOptions()
    {
        ClearSignEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("WAILS_SIGN_BACKEND", "azuresigntool");
            Environment.SetEnvironmentVariable("WAILS_SIGN_AKV_URL", "https://kv.vault.azure.net");
            Environment.SetEnvironmentVariable("WAILS_SIGN_AKV_CERT", "my-cert");
            Environment.SetEnvironmentVariable("WAILS_SIGN_AZURE_CLIENT_ID", "cid");
            Environment.SetEnvironmentVariable("WAILS_SIGN_AZURE_CLIENT_SECRET", "csec");
            Environment.SetEnvironmentVariable("WAILS_SIGN_AZURE_TENANT_ID", "tid");

            var result = CodeSigner.ResolveFromEnvironment();

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Backend).IsEqualTo(SignBackend.AzureSignTool);
            await Assert.That(result.KeyVaultUrl).IsEqualTo("https://kv.vault.azure.net");
            await Assert.That(result.KeyVaultCertificateName).IsEqualTo("my-cert");
            await Assert.That(result.AzureClientId).IsEqualTo("cid");
            await Assert.That(result.AzureClientSecret).IsEqualTo("csec");
            await Assert.That(result.AzureTenantId).IsEqualTo("tid");
        }
        finally
        {
            ClearSignEnvVars();
        }
    }

    #endregion
}
