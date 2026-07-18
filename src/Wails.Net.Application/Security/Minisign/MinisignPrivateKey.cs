using System.Security.Cryptography;
using System.Text;

namespace Wails.Net.Application.Security.Minisign;

/// <summary>
/// minisign 私钥密码加密器（P0-5）。
/// 对应 Tauri v2 的私钥加密存储：使用密码派生密钥（KDF）+ AEAD 加密私钥。
/// <para>
/// 受 minisign 原版私钥文件格式启发，采用自包含的二进制 blob 结构：
/// <code>
/// encrypted_blob (122 字节):
///   sig_alg[2]      = "Ed" (Ed25519)
///   kdf_alg[2]      = "P2" (PBKDF2-HMAC-SHA256)
///   cipher_alg[2]   = "CC" (ChaCha20-Poly1305, .NET 8+ 原生)
///   salt[16]         随机盐值
///   nonce[12]        ChaCha20-Poly1305 nonce
///   iter_count[8]   (u64 LE) PBKDF2 迭代次数
///   ciphertext[80]   = ChaCha20-Poly1305(64 字节私钥) = 64 + 16(tag)
/// </code>
/// </para>
/// <para>
/// <b>互操作说明</b>：本实现采用 PBKDF2-SHA256 + ChaCha20-Poly1305，与 Tauri v2 简化私钥加密
/// 思路对齐（KDF 派生 + AEAD 加密），但不与 minisign 原版（Argon2i/Argon2id +
/// XSalsa20-Poly1305 secretbox）二进制互操作。原因：.NET 10 不内置 Argon2 和 XSalsa20，
/// 为避免引入额外原生库依赖，选择 .NET 原生密码学原语。
/// </para>
/// </summary>
public static class MinisignPrivateKey
{
    /// <summary>Ed25519 签名算法标识。</summary>
    public const string SignatureAlgorithmId = "Ed";

    /// <summary>PBKDF2-HMAC-SHA256 密钥派生算法标识。</summary>
    public const string KdfAlgorithmId = "P2";

    /// <summary>ChaCha20-Poly1305 加密算法标识。</summary>
    public const string CipherAlgorithmId = "CC";

    /// <summary>盐长度（字节）。</summary>
    public const int SaltLength = 16;

    /// <summary>ChaCha20-Poly1305 nonce 长度（字节）。</summary>
    public const int NonceLength = 12;

    /// <summary>迭代次数字段长度（字节，u64 LE）。</summary>
    public const int IterCountLength = 8;

    /// <summary>Ed25519 私钥长度（64 字节：seed + public key）。</summary>
    public const int PrivateKeyLength = 64;

    /// <summary>ChaCha20-Poly1305 认证标签长度（字节）。</summary>
    public const int TagLength = 16;

    /// <summary>密文长度（私钥 + 标签）。</summary>
    public const int CiphertextLength = PrivateKeyLength + TagLength;

    /// <summary>算法标识字段长度（字节）。</summary>
    public const int AlgorithmIdLength = 2;

    /// <summary>加密 blob 总长度（字节）。</summary>
    public const int BlobLength =
        AlgorithmIdLength + AlgorithmIdLength + AlgorithmIdLength +
        SaltLength + NonceLength + IterCountLength + CiphertextLength;

    /// <summary>
    /// 默认 PBKDF2 迭代次数（600,000 次，NIST SP 800-132 推荐）。
    /// </summary>
    public const long DefaultIterations = 600_000;

    /// <summary>
    /// 使用密码加密 Ed25519 私钥。
    /// </summary>
    /// <param name="privateKey">Ed25519 私钥字节（64 字节：seed + public key）。</param>
    /// <param name="password">加密密码。</param>
    /// <param name="iterations">PBKDF2 迭代次数（默认 600,000）。</param>
    /// <param name="salt">可选盐值（16 字节）。为 null 时随机生成。</param>
    /// <param name="nonce">可选 nonce（12 字节）。为 null 时随机生成。</param>
    /// <returns>加密后的 blob（122 字节）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null。</exception>
    /// <exception cref="ArgumentException">私钥长度不正确或密码为空。</exception>
    public static byte[] Encrypt(
        byte[] privateKey,
        string password,
        long iterations = DefaultIterations,
        byte[]? salt = null,
        byte[]? nonce = null)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (privateKey.Length != PrivateKeyLength)
        {
            throw new ArgumentException(
                $"私钥长度必须为 {PrivateKeyLength} 字节，实际为 {privateKey.Length}。",
                nameof(privateKey));
        }
        if (iterations <= 0)
        {
            throw new ArgumentException("迭代次数必须为正数。", nameof(iterations));
        }

        salt ??= RandomNumberGenerator.GetBytes(SaltLength);
        nonce ??= RandomNumberGenerator.GetBytes(NonceLength);

        if (salt.Length != SaltLength)
        {
            throw new ArgumentException($"盐长度必须为 {SaltLength} 字节。", nameof(salt));
        }
        if (nonce.Length != NonceLength)
        {
            throw new ArgumentException($"nonce 长度必须为 {NonceLength} 字节。", nameof(nonce));
        }

        // 1. PBKDF2-HMAC-SHA256 派生 32 字节密钥
        var keyBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            (int)iterations,
            HashAlgorithmName.SHA256,
            32);

        // 2. ChaCha20-Poly1305 加密私钥
        //    .NET API 要求 ciphertext 缓冲区长度 = 明文长度（64 字节），tag 是独立输出（16 字节）
        //    blob 的 ciphertext 字段（80 字节）= ciphertext(64) + tag(16)
        var ciphertext = new byte[PrivateKeyLength];
        var tag = new byte[TagLength];
        using var chaCha20 = new System.Security.Cryptography.ChaCha20Poly1305(keyBytes);
        chaCha20.Encrypt(nonce, privateKey, ciphertext, tag);

        // 3. 组装 blob
        var blob = new byte[BlobLength];
        var offset = 0;
        Encoding.ASCII.GetBytes(SignatureAlgorithmId, 0, 2, blob, offset);
        offset += AlgorithmIdLength;
        Encoding.ASCII.GetBytes(KdfAlgorithmId, 0, 2, blob, offset);
        offset += AlgorithmIdLength;
        Encoding.ASCII.GetBytes(CipherAlgorithmId, 0, 2, blob, offset);
        offset += AlgorithmIdLength;
        Buffer.BlockCopy(salt, 0, blob, offset, SaltLength);
        offset += SaltLength;
        Buffer.BlockCopy(nonce, 0, blob, offset, NonceLength);
        offset += NonceLength;
        BitConverter.TryWriteBytes(blob.AsSpan(offset), (ulong)iterations);
        offset += IterCountLength;
        // 密文字段（80 字节）= ciphertext(64) + tag(16)
        Buffer.BlockCopy(ciphertext, 0, blob, offset, PrivateKeyLength);
        offset += PrivateKeyLength;
        Buffer.BlockCopy(tag, 0, blob, offset, TagLength);
        return blob;
    }

    /// <summary>
    /// 使用密码解密私钥 blob。
    /// </summary>
    /// <param name="encryptedBlob">加密 blob（122 字节）。</param>
    /// <param name="password">解密密码。</param>
    /// <returns>解密后的 Ed25519 私钥（64 字节）。</returns>
    /// <exception cref="ArgumentNullException">参数为 null。</exception>
    /// <exception cref="ArgumentException">blob 长度不正确或密码为空。</exception>
    /// <exception cref="FormatException">blob 格式错误（算法标识不匹配）。</exception>
    /// <exception cref="CryptographicException">密码错误或数据被篡改（MAC 校验失败）。</exception>
    public static byte[] Decrypt(byte[] encryptedBlob, string password)
    {
        ArgumentNullException.ThrowIfNull(encryptedBlob);
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (encryptedBlob.Length != BlobLength)
        {
            throw new ArgumentException(
                $"加密 blob 长度必须为 {BlobLength} 字节，实际为 {encryptedBlob.Length}。",
                nameof(encryptedBlob));
        }

        var offset = 0;
        var sigAlg = Encoding.ASCII.GetString(encryptedBlob, offset, AlgorithmIdLength);
        offset += AlgorithmIdLength;
        var kdfAlg = Encoding.ASCII.GetString(encryptedBlob, offset, AlgorithmIdLength);
        offset += AlgorithmIdLength;
        var cipherAlg = Encoding.ASCII.GetString(encryptedBlob, offset, AlgorithmIdLength);
        offset += AlgorithmIdLength;

        if (sigAlg != SignatureAlgorithmId)
        {
            throw new FormatException($"不支持的签名算法：{sigAlg}（期望 {SignatureAlgorithmId}）。");
        }
        if (kdfAlg != KdfAlgorithmId)
        {
            throw new FormatException($"不支持的 KDF 算法：{kdfAlg}（期望 {KdfAlgorithmId}）。");
        }
        if (cipherAlg != CipherAlgorithmId)
        {
            throw new FormatException($"不支持的加密算法：{cipherAlg}（期望 {CipherAlgorithmId}）。");
        }

        var salt = new byte[SaltLength];
        Buffer.BlockCopy(encryptedBlob, offset, salt, 0, SaltLength);
        offset += SaltLength;

        var nonce = new byte[NonceLength];
        Buffer.BlockCopy(encryptedBlob, offset, nonce, 0, NonceLength);
        offset += NonceLength;

        var iterCount = (long)BitConverter.ToUInt64(encryptedBlob, offset);
        offset += IterCountLength;
        if (iterCount <= 0)
        {
            throw new FormatException($"迭代次数无效：{iterCount}。");
        }

        // blob 的 ciphertext 字段（80 字节）= 实际密文(64) + tag(16)
        // .NET ChaCha20Poly1305.Decrypt 要求 ciphertext 参数长度 = plaintext 长度（64），
        // tag 是独立参数（16 字节）。
        var cipherBytes = new byte[PrivateKeyLength];
        Buffer.BlockCopy(encryptedBlob, offset, cipherBytes, 0, PrivateKeyLength);
        offset += PrivateKeyLength;
        var tag = new byte[TagLength];
        Buffer.BlockCopy(encryptedBlob, offset, tag, 0, TagLength);

        // 派生密钥并解密；密码错误时抛出 CryptographicException
        var keyBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            (int)iterCount,
            HashAlgorithmName.SHA256,
            32);

        var decrypted = new byte[PrivateKeyLength];
        using var chaCha20 = new System.Security.Cryptography.ChaCha20Poly1305(keyBytes);
        try
        {
            chaCha20.Decrypt(nonce, cipherBytes, tag, decrypted);
        }
        catch (AuthenticationTagMismatchException)
        {
            // 密码错误或数据被篡改时，Poly1305 标签验证失败。
            // 统一转为 CryptographicException，使调用方无需了解 AEAD 内部异常类型。
            throw new CryptographicException("密码错误或私钥数据已被篡改。");
        }
        return decrypted;
    }

    /// <summary>
    /// 判断私钥文件内容是否为加密格式。
    /// </summary>
    /// <param name="fileContent">私钥文件文本内容。</param>
    /// <returns>是加密格式返回 true，否则为明文格式。</returns>
    public static bool IsEncryptedFormat(string fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return false;
        }

        if (!MinisignFormat.TryParsePrivateKeyFile(fileContent, out var base64Key, out _))
        {
            return false;
        }

        if (string.IsNullOrEmpty(base64Key))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64Key);
            return bytes.Length == BlobLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
