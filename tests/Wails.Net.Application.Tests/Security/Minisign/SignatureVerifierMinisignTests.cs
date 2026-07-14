using System.IO;
using TUnit.Core;
using Wails.Net.Application.Security.Minisign;
using Wails.Net.Application.Services.Updater;

namespace Wails.Net.Application.Tests.Security.Minisign;

/// <summary>
/// SignatureVerifier minisign 路径单元测试（TUnit）。
/// 对应主题 H-3.6：覆盖 VerifyMinisignAsync 的有效签名、篡改文件和缺失公钥三种场景。
/// </summary>
[NotInParallel]
public sealed class SignatureVerifierMinisignTests
{
    /// <summary>
    /// 创建临时文件并写入指定内容，返回文件路径。
    /// </summary>
    private static string CreateTempFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), "wails-sigverify-test-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, content);
        return path;
    }

    [Test]
    public async Task VerifyMinisignAsync_ValidSignature_ReturnsValid()
    {
        // 生成密钥对、签名文件、验证通过
        var keyPair = MinisignSigner.GenerateKeyPair();
        var filePath = CreateTempFile("test archive content"u8.ToArray());

        try
        {
            var base64Sig = MinisignSigner.SignFile(filePath, keyPair.PrivateKey);
            var config = new UpdaterConfig { TrustedPublicKey = keyPair.GetPublicKeyBase64() };
            var verifier = new SignatureVerifier(config);

            var result = await verifier.VerifyMinisignAsync(filePath, base64Sig);

            await Assert.That(result.IsValid).IsTrue();
            await Assert.That(result.Fingerprint).IsEqualTo(keyPair.KeyFingerprint);
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Test]
    public async Task VerifyMinisignAsync_TamperedArchive_ReturnsInvalid()
    {
        // 签名后篡改文件内容
        var keyPair = MinisignSigner.GenerateKeyPair();
        var filePath = CreateTempFile("original content"u8.ToArray());

        try
        {
            var base64Sig = MinisignSigner.SignFile(filePath, keyPair.PrivateKey);
            // 篡改文件内容
            await File.WriteAllTextAsync(filePath, "tampered content");

            var config = new UpdaterConfig { TrustedPublicKey = keyPair.GetPublicKeyBase64() };
            var verifier = new SignatureVerifier(config);

            var result = await verifier.VerifyMinisignAsync(filePath, base64Sig);

            await Assert.That(result.IsValid).IsFalse();
            await Assert.That(result.ErrorMessage).IsNotNull();
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }

    [Test]
    public async Task VerifyMinisignAsync_MissingTrustedPublicKey_ReturnsInvalid()
    {
        // 未配置 TrustedPublicKey 时返回 IsValid=false
        var keyPair = MinisignSigner.GenerateKeyPair();
        var filePath = CreateTempFile("test content"u8.ToArray());

        try
        {
            var base64Sig = MinisignSigner.SignFile(filePath, keyPair.PrivateKey);
            // TrustedPublicKey 未配置
            var config = new UpdaterConfig();
            var verifier = new SignatureVerifier(config);

            var result = await verifier.VerifyMinisignAsync(filePath, base64Sig);

            await Assert.That(result.IsValid).IsFalse();
            await Assert.That(result.ErrorMessage).IsNotNull();
        }
        finally
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
    }
}
