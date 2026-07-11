using System.IO;
using System.Security.Cryptography;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Commands;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// SignerCommand、PlatformCommand、SelfUpdateCommand 的单元测试（TUnit）。
/// 验证 RSA 密钥生成、签名/验证往返、RID 映射、工作负载管理。
/// </summary>
[NotInParallel]
public sealed class SignerPlatformUpdateTests
{
    // ---------------------------------------------------------------------
    // SignerCommand - PEM 导出/导入
    // ---------------------------------------------------------------------

    [Test]
    public async Task SignerCommand_ExportPrivateKeyPem_ContainsPrivateKeyHeader()
    {
        using var rsa = RSA.Create(2048);
        var pem = SignerCommand.ExportPrivateKeyPem(rsa);
        await Assert.That(pem).Contains("PRIVATE KEY");
    }

    [Test]
    public async Task SignerCommand_ExportPublicKeyPem_ContainsPublicKeyHeader()
    {
        using var rsa = RSA.Create(2048);
        var pem = SignerCommand.ExportPublicKeyPem(rsa);
        await Assert.That(pem).Contains("PUBLIC KEY");
    }

    [Test]
    public async Task SignerCommand_ImportExportRoundTrip_Succeeds()
    {
        using var rsa1 = RSA.Create(2048);
        var privPem = SignerCommand.ExportPrivateKeyPem(rsa1);
        var pubPem = SignerCommand.ExportPublicKeyPem(rsa1);

        using var rsa2 = RSA.Create();
        SignerCommand.ImportPrivateKeyPem(rsa2, privPem);

        using var rsa3 = RSA.Create();
        SignerCommand.ImportPublicKeyPem(rsa3, pubPem);

        // 用导入的公钥验证签名
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var signature = rsa2.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        bool isValid = rsa3.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        await Assert.That(isValid).IsTrue();
    }

    // ---------------------------------------------------------------------
    // SignerCommand - 完整签名/验证往返
    // ---------------------------------------------------------------------

    [Test]
    public async Task SignerCommand_SignAndVerifyFile_RoundTrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_signer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 生成密钥对
            var keyPrefix = Path.Combine(tempDir, "testkey");
            var signer = new SignerCommand();
            await signer.GenerateKeyPairAsync(new FileInfo(keyPrefix));

            var keyPath = keyPrefix + ".key";
            var pubKeyPath = keyPrefix + ".pub";

            await Assert.That(File.Exists(keyPath)).IsTrue();
            await Assert.That(File.Exists(pubKeyPath)).IsTrue();

            // 创建测试文件
            var dataFile = Path.Combine(tempDir, "testdata.bin");
            await File.WriteAllBytesAsync(dataFile, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

            // 签名
            var exitSign = await signer.SignFileAsync(
                new FileInfo(dataFile), new FileInfo(keyPath), null);

            await Assert.That(exitSign).IsEqualTo(0);

            var sigFile = dataFile + ".sig";
            await Assert.That(File.Exists(sigFile)).IsTrue();

            // 验证
            var exitVerify = await signer.VerifyFileAsync(
                new FileInfo(dataFile), new FileInfo(pubKeyPath), null);

            await Assert.That(exitVerify).IsEqualTo(0);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task SignerCommand_VerifyTamperedFile_ReturnsFailure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"wails_signer_tamper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 生成密钥对
            var keyPrefix = Path.Combine(tempDir, "testkey");
            var signer = new SignerCommand();
            await signer.GenerateKeyPairAsync(new FileInfo(keyPrefix));

            // 创建测试文件并签名
            var dataFile = Path.Combine(tempDir, "testdata.bin");
            await File.WriteAllBytesAsync(dataFile, new byte[] { 1, 2, 3 });

            await signer.SignFileAsync(
                new FileInfo(dataFile), new FileInfo(keyPrefix + ".key"), null);

            // 篡改文件内容
            await File.WriteAllBytesAsync(dataFile, new byte[] { 9, 9, 9 });

            // 验证应失败
            var exitVerify = await signer.VerifyFileAsync(
                new FileInfo(dataFile), new FileInfo(keyPrefix + ".pub"), null);

            await Assert.That(exitVerify).IsEqualTo(2);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* 忽略 */ }
        }
    }

    [Test]
    public async Task SignerCommand_SignNonExistentFile_ReturnsError()
    {
        var signer = new SignerCommand();
        var exit = await signer.SignFileAsync(
            new FileInfo("/nonexistent/file.bin"),
            new FileInfo("/nonexistent/key.key"),
            null);
        await Assert.That(exit).IsEqualTo(1);
    }

    [Test]
    public async Task SignerCommand_VerifyNonExistentFile_ReturnsError()
    {
        var signer = new SignerCommand();
        var exit = await signer.VerifyFileAsync(
            new FileInfo("/nonexistent/file.bin"),
            new FileInfo("/nonexistent/key.pub"),
            null);
        await Assert.That(exit).IsEqualTo(1);
    }

    // ---------------------------------------------------------------------
    // PlatformCommand
    // ---------------------------------------------------------------------

    [Test]
    public async Task PlatformCommand_IsSupportedRid_WinX64_ReturnsTrue()
    {
        bool result = PlatformCommand.IsSupportedRid("win-x64");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task PlatformCommand_IsSupportedRid_LinuxArm64_ReturnsTrue()
    {
        bool result = PlatformCommand.IsSupportedRid("linux-arm64");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task PlatformCommand_IsSupportedRid_UnknownRid_ReturnsFalse()
    {
        bool result = PlatformCommand.IsSupportedRid("macos-arm64");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task PlatformCommand_IsSupportedRid_CaseInsensitive()
    {
        bool result = PlatformCommand.IsSupportedRid("WIN-X64");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task PlatformCommand_MapRidToWorkload_WinX64_ReturnsMauiWindows()
    {
        var workload = PlatformCommand.MapRidToWorkload("win-x64");
        await Assert.That(workload).IsEqualTo("maui-windows");
    }

    [Test]
    public async Task PlatformCommand_MapRidToWorkload_LinuxX64_ReturnsNull()
    {
        var workload = PlatformCommand.MapRidToWorkload("linux-x64");
        await Assert.That(workload).IsNull();
    }

    [Test]
    public async Task PlatformCommand_GetSupportedRids_ContainsWinX64()
    {
        var rids = PlatformCommand.GetSupportedRids();
        bool containsWinX64 = false;
        foreach (var rid in rids)
        {
            if (rid == "win-x64")
            {
                containsWinX64 = true;
                break;
            }
        }
        await Assert.That(containsWinX64).IsTrue();
    }

    [Test]
    public async Task PlatformCommand_GetSupportedRids_ContainsAtLeastFourRids()
    {
        var rids = PlatformCommand.GetSupportedRids();
        await Assert.That(rids.Length).IsGreaterThan(3);
    }

    // ---------------------------------------------------------------------
    // SelfUpdateCommand
    // ---------------------------------------------------------------------

    [Test]
    public async Task SelfUpdateCommand_Create_DoesNotThrow()
    {
        await Assert.That(() => SelfUpdateCommand.Create()).ThrowsNothing();
    }
}
