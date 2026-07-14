using System.Security.Cryptography;
using NSec.Cryptography;

namespace Wails.Net.Application.Security.Minisign;

/// <summary>
/// minisign 验证器。
/// 对应 Tauri v2 的更新签名验证：BLAKE2b-512 + Ed25519 验签。
/// </summary>
public static class MinisignVerifier
{
    /// <summary>
    /// 验证数据的签名。
    /// </summary>
    /// <param name="data">原始数据。</param>
    /// <param name="signature">完整签名（128 字节：BLAKE2b-512 指纹 + Ed25519 签名）。</param>
    /// <param name="publicKey">Ed25519 公钥（32 字节）。</param>
    /// <returns>验证通过返回 true，否则返回 false。</returns>
    public static bool Verify(byte[] data, byte[] signature, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(publicKey);

        // 公钥长度必须为 32 字节
        if (publicKey.Length != 32)
        {
            return false;
        }

        // 完整签名长度必须为 128 字节
        if (signature.Length != MinisignSigner.FullSignatureLength)
        {
            return false;
        }

        try
        {
            // 1. 提取 BLAKE2b-512 指纹和 Ed25519 签名
            var hash = new byte[MinisignSigner.HashLength];
            Buffer.BlockCopy(signature, 0, hash, 0, MinisignSigner.HashLength);

            var ed25519Signature = new byte[MinisignSigner.SignatureLength];
            Buffer.BlockCopy(signature, MinisignSigner.HashLength, ed25519Signature, 0, MinisignSigner.SignatureLength);

            // 2. 验证 BLAKE2b-512 指纹与数据一致（防止指纹被替换）
            // Blake2Fast 2.0.0 默认输出 64 字节
            var actualHash = Blake2Fast.Blake2b.ComputeHash(data);
            if (!CryptographicEquals(actualHash, hash))
            {
                return false;
            }

            // 3. 使用 Ed25519 公钥验签（NSec 26.x 中 PublicKey 是 struct，不需要 using）
            var algorithm = SignatureAlgorithm.Ed25519;
            var publicKeyObj = PublicKey.Import(algorithm, publicKey, KeyBlobFormat.RawPublicKey);
            return algorithm.Verify(publicKeyObj, hash, ed25519Signature);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// 验证文件的签名。
    /// </summary>
    /// <param name="filePath">要验证的文件路径。</param>
    /// <param name="base64Signature">Base64 编码的完整签名。</param>
    /// <param name="publicKey">Ed25519 公钥（32 字节）。</param>
    /// <returns>验证通过返回 true，否则返回 false。</returns>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    public static bool VerifyFile(string filePath, string base64Signature, byte[] publicKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentException.ThrowIfNullOrEmpty(base64Signature);
        ArgumentNullException.ThrowIfNull(publicKey);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"文件不存在: {filePath}", filePath);
        }

        var data = File.ReadAllBytes(filePath);
        var signature = Convert.FromBase64String(base64Signature);
        return Verify(data, signature, publicKey);
    }

    /// <summary>
    /// 从公钥文件加载公钥。
    /// </summary>
    /// <param name="publicKeyFilePath">公钥文件路径。</param>
    /// <returns>公钥字节（32 字节）和指纹。</returns>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    /// <exception cref="FormatException">文件格式错误。</exception>
    public static (byte[] PublicKey, string Fingerprint) LoadPublicKey(string publicKeyFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(publicKeyFilePath);

        if (!File.Exists(publicKeyFilePath))
        {
            throw new FileNotFoundException($"公钥文件不存在: {publicKeyFilePath}", publicKeyFilePath);
        }

        var content = File.ReadAllText(publicKeyFilePath);
        if (!MinisignFormat.TryParsePublicKeyFile(content, out var base64Key, out var fingerprint))
        {
            throw new FormatException($"公钥文件格式错误: {publicKeyFilePath}");
        }

        var publicKey = Convert.FromBase64String(base64Key!);
        if (publicKey.Length != 32)
        {
            throw new FormatException($"公钥长度必须为 32 字节，实际为 {publicKey.Length}。");
        }

        return (publicKey, fingerprint!);
    }

    /// <summary>
    /// 从签名文件加载 Base64 签名。
    /// </summary>
    /// <param name="signatureFilePath">签名文件路径。</param>
    /// <returns>Base64 编码的签名和指纹。</returns>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    /// <exception cref="FormatException">文件格式错误。</exception>
    public static (string Base64Signature, string Fingerprint) LoadSignature(string signatureFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(signatureFilePath);

        if (!File.Exists(signatureFilePath))
        {
            throw new FileNotFoundException($"签名文件不存在: {signatureFilePath}", signatureFilePath);
        }

        var content = File.ReadAllText(signatureFilePath);
        if (!MinisignFormat.TryParseSignatureFile(content, out var base64Sig, out var fingerprint))
        {
            throw new FormatException($"签名文件格式错误: {signatureFilePath}");
        }

        return (base64Sig!, fingerprint!);
    }

    /// <summary>
    /// 常量时间比较两个字节序列，防止时序攻击。
    /// </summary>
    private static bool CryptographicEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }
}
