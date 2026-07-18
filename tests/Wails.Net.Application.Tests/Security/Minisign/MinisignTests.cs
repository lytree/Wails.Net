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

    // ───────────────────── MinisignPrivateKey (P0-5) ─────────────────────

    [Test]
    public async Task MinisignPrivateKey_EncryptDecrypt_RoundTrip()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        const string password = "P@ssw0rd-测试-2026";

        var encrypted = MinisignPrivateKey.Encrypt(keyPair.PrivateKey, password, iterations: 10_000);
        var decrypted = MinisignPrivateKey.Decrypt(encrypted, password);

        await Assert.That(encrypted.Length).IsEqualTo(MinisignPrivateKey.BlobLength);
        await Assert.That(decrypted).IsEquivalentTo(keyPair.PrivateKey);
    }

    [Test]
    public async Task MinisignPrivateKey_Encrypt_DeterministicWithFixedSaltAndNonce()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        const string password = "deterministic-test";
        var salt = new byte[MinisignPrivateKey.SaltLength];
        var nonce = new byte[MinisignPrivateKey.NonceLength];
        for (var i = 0; i < salt.Length; i++) salt[i] = (byte)i;
        for (var i = 0; i < nonce.Length; i++) nonce[i] = (byte)(i + 1);

        var enc1 = MinisignPrivateKey.Encrypt(keyPair.PrivateKey, password, 10_000, salt, nonce);
        var enc2 = MinisignPrivateKey.Encrypt(keyPair.PrivateKey, password, 10_000, salt, nonce);

        await Assert.That(enc1).IsEquivalentTo(enc2);
    }

    [Test]
    public async Task MinisignPrivateKey_Encrypt_BlobLengthIsCorrect()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();

        var encrypted = MinisignPrivateKey.Encrypt(keyPair.PrivateKey, "test", iterations: 100);

        // 122 = 2(sig) + 2(kdf) + 2(cipher) + 16(salt) + 12(nonce) + 8(iter) + 64(plaintext) + 16(tag)
        await Assert.That(encrypted.Length).IsEqualTo(122);
        await Assert.That(encrypted.Length).IsEqualTo(MinisignPrivateKey.BlobLength);
    }

    [Test]
    public async Task MinisignPrivateKey_Decrypt_WrongPassword_ThrowsCryptographicException()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var encrypted = MinisignPrivateKey.Encrypt(keyPair.PrivateKey, "correct-password", iterations: 10_000);

        await Assert.That(() => MinisignPrivateKey.Decrypt(encrypted, "wrong-password"))
            .ThrowsExactly<System.Security.Cryptography.CryptographicException>();
    }

    [Test]
    public async Task MinisignPrivateKey_Encrypt_InvalidPrivateKeyLength_ThrowsArgumentException()
    {
        var invalidKey = new byte[32]; // 错误长度

        await Assert.That(() => MinisignPrivateKey.Encrypt(invalidKey, "password", iterations: 100))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task MinisignPrivateKey_Encrypt_EmptyPassword_ThrowsArgumentException()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();

        await Assert.That(() => MinisignPrivateKey.Encrypt(keyPair.PrivateKey, "", iterations: 100))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task MinisignPrivateKey_Encrypt_InvalidIterations_ThrowsArgumentException()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();

        await Assert.That(() => MinisignPrivateKey.Encrypt(keyPair.PrivateKey, "password", iterations: 0))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task MinisignPrivateKey_Encrypt_InvalidSaltLength_ThrowsArgumentException()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var badSalt = new byte[8]; // 错误长度

        await Assert.That(() => MinisignPrivateKey.Encrypt(keyPair.PrivateKey, "password", 100, badSalt, null))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task MinisignPrivateKey_Encrypt_InvalidNonceLength_ThrowsArgumentException()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var validSalt = new byte[MinisignPrivateKey.SaltLength];
        var badNonce = new byte[8];

        await Assert.That(() => MinisignPrivateKey.Encrypt(keyPair.PrivateKey, "password", 100, validSalt, badNonce))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task MinisignPrivateKey_Decrypt_InvalidBlobLength_ThrowsArgumentException()
    {
        var badBlob = new byte[64]; // 错误长度

        await Assert.That(() => MinisignPrivateKey.Decrypt(badBlob, "password"))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task MinisignPrivateKey_Decrypt_InvalidSigAlgorithmId_ThrowsFormatException()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var encrypted = MinisignPrivateKey.Encrypt(keyPair.PrivateKey, "password", iterations: 100);
        // 破坏 sig_alg 字段（前 2 字节）
        encrypted[0] = (byte)'X';
        encrypted[1] = (byte)'X';

        await Assert.That(() => MinisignPrivateKey.Decrypt(encrypted, "password"))
            .ThrowsExactly<FormatException>();
    }

    [Test]
    public async Task MinisignPrivateKey_Decrypt_InvalidKdfAlgorithmId_ThrowsFormatException()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var encrypted = MinisignPrivateKey.Encrypt(keyPair.PrivateKey, "password", iterations: 100);
        // 破坏 kdf_alg 字段（偏移 2）
        encrypted[2] = (byte)'X';
        encrypted[3] = (byte)'X';

        await Assert.That(() => MinisignPrivateKey.Decrypt(encrypted, "password"))
            .ThrowsExactly<FormatException>();
    }

    [Test]
    public async Task MinisignPrivateKey_Decrypt_InvalidCipherAlgorithmId_ThrowsFormatException()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var encrypted = MinisignPrivateKey.Encrypt(keyPair.PrivateKey, "password", iterations: 100);
        // 破坏 cipher_alg 字段（偏移 4）
        encrypted[4] = (byte)'X';
        encrypted[5] = (byte)'X';

        await Assert.That(() => MinisignPrivateKey.Decrypt(encrypted, "password"))
            .ThrowsExactly<FormatException>();
    }

    [Test]
    public async Task MinisignPrivateKey_IsEncryptedFormat_EncryptedBlob_ReturnsTrue()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var encrypted = MinisignPrivateKey.Encrypt(keyPair.PrivateKey, "password", iterations: 100);
        var base64Blob = Convert.ToBase64String(encrypted);
        var fileContent = MinisignFormat.FormatPrivateKeyFile(null, base64Blob, keyPair.KeyFingerprint);

        var result = MinisignPrivateKey.IsEncryptedFormat(fileContent);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task MinisignPrivateKey_IsEncryptedFormat_Plaintext_ReturnsFalse()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var fileContent = MinisignFormat.FormatPrivateKeyFile(
            null,
            keyPair.GetPrivateKeyBase64(),
            keyPair.KeyFingerprint);

        var result = MinisignPrivateKey.IsEncryptedFormat(fileContent);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task MinisignPrivateKey_IsEncryptedFormat_InvalidContent_ReturnsFalse()
    {
        await Assert.That(MinisignPrivateKey.IsEncryptedFormat("")).IsFalse();
        await Assert.That(MinisignPrivateKey.IsEncryptedFormat("   ")).IsFalse();
        await Assert.That(MinisignPrivateKey.IsEncryptedFormat("invalid content")).IsFalse();
    }

    // ─────────────── MinisignSigner 加密私钥文件 (P0-5) ───────────────

    [Test]
    public async Task WriteEncryptedPrivateKeyFile_LoadPrivateKeyFile_RoundTrip()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        const string password = "round-trip-test-密码";
        var tempFile = Path.Combine(Path.GetTempPath(), "minisign-enc-" + Guid.NewGuid().ToString("N") + ".key");

        try
        {
            MinisignSigner.WriteEncryptedPrivateKeyFile(tempFile, keyPair, password, iterations: 10_000);

            // 文件应是加密格式
            var content = File.ReadAllText(tempFile);
            await Assert.That(MinisignPrivateKey.IsEncryptedFormat(content)).IsTrue();

            // 加载并验证
            var loaded = MinisignSigner.LoadPrivateKeyFile(tempFile, password);
            await Assert.That(loaded.PrivateKey).IsEquivalentTo(keyPair.PrivateKey);
            await Assert.That(loaded.PublicKey).IsEquivalentTo(keyPair.PublicKey);
            await Assert.That(loaded.KeyFingerprint).IsEqualTo(keyPair.KeyFingerprint);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Test]
    public async Task LoadPrivateKeyFile_PlaintextFormat_NoPasswordRequired()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var tempFile = Path.Combine(Path.GetTempPath(), "minisign-plain-" + Guid.NewGuid().ToString("N") + ".key");

        try
        {
            // 写入明文格式
            MinisignSigner.WritePrivateKeyFile(tempFile, keyPair);

            // 不提供密码也能加载
            var loaded = MinisignSigner.LoadPrivateKeyFile(tempFile);
            await Assert.That(loaded.PrivateKey).IsEquivalentTo(keyPair.PrivateKey);
            await Assert.That(loaded.PublicKey).IsEquivalentTo(keyPair.PublicKey);
            await Assert.That(loaded.KeyFingerprint).IsEqualTo(keyPair.KeyFingerprint);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Test]
    public async Task LoadPrivateKeyFile_EncryptedWithoutPassword_ThrowsArgumentException()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var tempFile = Path.Combine(Path.GetTempPath(), "minisign-no-pwd-" + Guid.NewGuid().ToString("N") + ".key");

        try
        {
            MinisignSigner.WriteEncryptedPrivateKeyFile(tempFile, keyPair, "secret", iterations: 100);

            await Assert.That(() => MinisignSigner.LoadPrivateKeyFile(tempFile))
                .ThrowsExactly<ArgumentException>();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Test]
    public async Task LoadPrivateKeyFile_EncryptedWrongPassword_ThrowsCryptographicException()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var tempFile = Path.Combine(Path.GetTempPath(), "minisign-bad-pwd-" + Guid.NewGuid().ToString("N") + ".key");

        try
        {
            MinisignSigner.WriteEncryptedPrivateKeyFile(tempFile, keyPair, "correct", iterations: 100);

            await Assert.That(() => MinisignSigner.LoadPrivateKeyFile(tempFile, "wrong"))
                .ThrowsExactly<System.Security.Cryptography.CryptographicException>();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Test]
    public async Task LoadPrivateKeyFile_NonExistentFile_ThrowsFileNotFoundException()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non-existent-" + Guid.NewGuid().ToString("N") + ".key");

        await Assert.That(() => MinisignSigner.LoadPrivateKeyFile(nonExistentPath))
            .ThrowsExactly<FileNotFoundException>();
    }

    [Test]
    public async Task WriteEncryptedPrivateKeyFile_EmptyPassword_ThrowsArgumentException()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        var tempFile = Path.Combine(Path.GetTempPath(), "minisign-empty-pwd-" + Guid.NewGuid().ToString("N") + ".key");

        try
        {
            await Assert.That(() => MinisignSigner.WriteEncryptedPrivateKeyFile(tempFile, keyPair, "", iterations: 100))
                .ThrowsExactly<ArgumentException>();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Test]
    public async Task WriteEncryptedPrivateKeyFile_DefaultIterations_Used()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        const string password = "default-iter-test";
        var tempFile = Path.Combine(Path.GetTempPath(), "minisign-default-iter-" + Guid.NewGuid().ToString("N") + ".key");

        try
        {
            // 使用默认迭代次数
            MinisignSigner.WriteEncryptedPrivateKeyFile(tempFile, keyPair, password);

            var content = File.ReadAllText(tempFile);
            await Assert.That(MinisignPrivateKey.IsEncryptedFormat(content)).IsTrue();

            // 应该能正常加载
            var loaded = MinisignSigner.LoadPrivateKeyFile(tempFile, password);
            await Assert.That(loaded.PrivateKey).IsEquivalentTo(keyPair.PrivateKey);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Test]
    public async Task EncryptedPrivateKey_StillUsableForSigning()
    {
        var keyPair = MinisignSigner.GenerateKeyPair();
        const string password = "signing-test";
        var tempFile = Path.Combine(Path.GetTempPath(), "minisign-sign-" + Guid.NewGuid().ToString("N") + ".key");
        var data = "encrypted key signing test"u8.ToArray();

        try
        {
            MinisignSigner.WriteEncryptedPrivateKeyFile(tempFile, keyPair, password, iterations: 100);

            // 加载加密私钥
            var loaded = MinisignSigner.LoadPrivateKeyFile(tempFile, password);

            // 使用加载的私钥签名，应该能用原公钥验签
            var signature = MinisignSigner.Sign(data, loaded.PrivateKey);
            var isValid = MinisignVerifier.Verify(data, signature, keyPair.PublicKey);

            await Assert.That(isValid).IsTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
