using System.IO;
using TUnit.Core;
using Wails.Net.Application.Security.Minisign;

namespace Wails.Net.Application.Tests.Security.Minisign;

/// <summary>
/// minisign 签名工具单元测试（TUnit）。
/// 对应主题 H-3.6：覆盖 MinisignKeyPair / MinisignSigner / MinisignVerifier / MinisignFormat。
/// </summary>
[NotInParallel]
public sealed class MinisignTests
{
    // ───────────────────────── MinisignKeyPair ─────────────────────────

    [Test]
    public async Task ComputeFingerprint_ReturnsConsistentValue()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();

        var f1 = MinisignKeyPair.ComputeFingerprint(keyPair.PublicKey);
        var f2 = MinisignKeyPair.ComputeFingerprint(keyPair.PublicKey);

        await Assert.That(f1).IsEqualTo(f2);
    }

    [Test]
    public async Task ComputeFingerprint_ReturnsHexLowerCase_64Chars()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();

        var fingerprint = MinisignKeyPair.ComputeFingerprint(keyPair.PublicKey);

        await Assert.That(fingerprint.Length).IsEqualTo(64);
        await Assert.That(fingerprint).IsEqualTo(fingerprint.ToLowerInvariant());
        // 仅包含 0-9a-f
        foreach (var c in fingerprint)
        {
            await Assert.That(c >= '0' && c <= '9' || c >= 'a' && c <= 'f').IsTrue();
        }
    }

    [Test]
    public async Task Constructor_InvalidPrivateKeyLength_ThrowsArgumentException()
    {
        var invalidPrivateKey = new byte[32]; // 错误长度
        var publicKey = new byte[32];

        await Assert.That(() => new MinisignKeyPair(invalidPrivateKey, publicKey))
            .ThrowsExactly<ArgumentException>();
    }

    // ───────────────────────── MinisignSigner ─────────────────────────

    [Test]
    public async Task GenerateKeyPair_ReturnsValidKeyPair()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();

        await Assert.That(keyPair.PrivateKey.Length).IsEqualTo(64);
        await Assert.That(keyPair.PublicKey.Length).IsEqualTo(32);
        await Assert.That(keyPair.KeyFingerprint.Length).IsEqualTo(64);
    }

    [Test]
    public async Task ImportKeyPair_ValidBase64_ReturnsKeyPair()
    {
        var original = MinisignSigner.GenerateKeyPair();
        var base64PrivateKey = original.GetPrivateKeyBase64();

        var imported = MinisignSigner.ImportKeyPair(base64PrivateKey);

        await Assert.That(imported.PrivateKey).IsEquivalentTo(original.PrivateKey);
        await Assert.That(imported.PublicKey).IsEquivalentTo(original.PublicKey);
        await Assert.That(imported.KeyFingerprint).IsEqualTo(original.KeyFingerprint);
    }

    [Test]
    public async Task ImportKeyPair_InvalidLength_ThrowsArgumentException()
    {
        var invalidBase64 = Convert.ToBase64String(new byte[32]); // 长度错误

        await Assert.That(() => MinisignSigner.ImportKeyPair(invalidBase64))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Sign_AndVerify_RoundTrip()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var data = "Hello, minisign!"u8.ToArray();

        var signature = MinisignSigner.Sign(data, keyPair.PrivateKey);
        var isValid = MinisignVerifier.Verify(data, signature, keyPair.PublicKey);

        await Assert.That(isValid).IsTrue();
    }

    [Test]
    public async Task SignFile_NonExistentFile_ThrowsFileNotFoundException()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non-existent-" + Guid.NewGuid().ToString("N") + ".bin");

        await Assert.That(() => MinisignSigner.SignFile(nonExistentPath, keyPair.PrivateKey))
            .ThrowsExactly<FileNotFoundException>();
    }

    // ───────────────────────── MinisignVerifier ─────────────────────────

    [Test]
    public async Task Verify_ValidSignature_ReturnsTrue()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var data = "test data"u8.ToArray();
        var signature = MinisignSigner.Sign(data, keyPair.PrivateKey);

        var result = MinisignVerifier.Verify(data, signature, keyPair.PublicKey);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Verify_TamperedData_ReturnsFalse()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var data = "original data"u8.ToArray();
        var signature = MinisignSigner.Sign(data, keyPair.PrivateKey);

        // 篡改数据
        data[0] = (byte)(data[0] ^ 0xFF);
        var result = MinisignVerifier.Verify(data, signature, keyPair.PublicKey);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Verify_WrongPublicKey_ReturnsFalse()
    {
        var keyPair1 = MinisignSigner.GenerateKeyPair();
        var keyPair2 = MinisignSigner.GenerateKeyPair();
        var data = "test data"u8.ToArray();
        var signature = MinisignSigner.Sign(data, keyPair1.PrivateKey);

        // 使用错误的公钥验证
        var result = MinisignVerifier.Verify(data, signature, keyPair2.PublicKey);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task VerifyFile_ValidFileSignature_ReturnsTrue()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var tempFile = Path.Combine(Path.GetTempPath(), "minisign-test-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllTextAsync(tempFile, "test file content");

        try
        {
            var base64Sig = MinisignSigner.SignFile(tempFile, keyPair.PrivateKey);
            var result = MinisignVerifier.VerifyFile(tempFile, base64Sig, keyPair.PublicKey);

            await Assert.That(result).IsTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ───────────────────────── MinisignFormat ─────────────────────────

    [Test]
    public async Task FormatSignatureFile_AndTryParse_RoundTrip()
    {
        var base64Sig = Convert.ToBase64String(new byte[128]);
        var fingerprint = "abc123def456";

        var content = MinisignFormat.FormatSignatureFile(null, base64Sig, fingerprint);
        var success = MinisignFormat.TryParseSignatureFile(content, out var parsedSig, out var parsedFingerprint);

        await Assert.That(success).IsTrue();
        await Assert.That(parsedSig).IsEqualTo(base64Sig);
        await Assert.That(parsedFingerprint).IsEqualTo(fingerprint);
    }

    [Test]
    public async Task TryParseSignatureFile_InvalidFormat_ReturnsFalse()
    {
        // 仅 1 行，不符合 3 行格式
        var invalidContent = "only one line";

        var success = MinisignFormat.TryParseSignatureFile(invalidContent, out var sig, out var fingerprint);

        await Assert.That(success).IsFalse();
        await Assert.That(sig).IsNull();
        await Assert.That(fingerprint).IsNull();
    }
}
