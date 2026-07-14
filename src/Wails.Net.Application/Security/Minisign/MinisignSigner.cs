using NSec.Cryptography;

namespace Wails.Net.Application.Security.Minisign;

/// <summary>
/// minisign 签名器。
/// 对应 Tauri v2 的更新签名工具：BLAKE2b-512 哈希 + Ed25519 签名。
/// <para>
/// 签名流程：
/// <list type="number">
/// <item>使用 BLAKE2b-512 计算数据指纹（64 字节）</item>
/// <item>使用 Ed25519 私钥对指纹进行签名（64 字节）</item>
/// <item>组合指纹和签名为完整签名（128 字节）</item>
/// </list>
/// 验证时按相同流程验签。
/// </para>
/// </summary>
public static class MinisignSigner
{
    /// <summary>BLAKE2b 哈希输出长度（字节）。</summary>
    public const int HashLength = 64;

    /// <summary>Ed25519 签名长度（字节）。</summary>
    public const int SignatureLength = 64;

    /// <summary>完整签名长度（指纹 + Ed25519 签名 = 128 字节）。</summary>
    public const int FullSignatureLength = HashLength + SignatureLength;

    /// <summary>
    /// 生成新的 Ed25519 密钥对。
    /// </summary>
    /// <returns>新生成的 <see cref="MinisignKeyPair"/>。</returns>
    public static MinisignKeyPair GenerateKeyPair()
    {
        var algorithm = SignatureAlgorithm.Ed25519;
        var creationParams = new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
        };
        using var key = Key.Create(algorithm, creationParams);

        // NSec RawPrivateKey 仅导出 32 字节种子，需拼接 32 字节公钥形成 64 字节完整私钥
        // （minisign 私钥格式：seed[32] + pubkey[32]）
        var seed = key.Export(KeyBlobFormat.RawPrivateKey);
        var publicKey = key.Export(KeyBlobFormat.RawPublicKey);
        var privateKey = new byte[64];
        Buffer.BlockCopy(seed, 0, privateKey, 0, 32);
        Buffer.BlockCopy(publicKey, 0, privateKey, 32, 32);
        return new MinisignKeyPair(privateKey, publicKey);
    }

    /// <summary>
    /// 从 Base64 编码的私钥导入密钥对。
    /// </summary>
    /// <param name="base64PrivateKey">Base64 编码的私钥（64 字节：seed + public key）。</param>
    /// <returns>导入的 <see cref="MinisignKeyPair"/>。</returns>
    public static MinisignKeyPair ImportKeyPair(string base64PrivateKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(base64PrivateKey);
        var privateKey = Convert.FromBase64String(base64PrivateKey);
        return ImportKeyPair(privateKey);
    }

    /// <summary>
    /// 从原始字节私钥导入密钥对。
    /// </summary>
    /// <param name="privateKey">原始 Ed25519 私钥字节（64 字节：seed + public key）。</param>
    /// <returns>导入的 <see cref="MinisignKeyPair"/>。</returns>
    public static MinisignKeyPair ImportKeyPair(byte[] privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (privateKey.Length != 64)
        {
            throw new ArgumentException($"私钥长度必须为 64 字节，实际为 {privateKey.Length}。", nameof(privateKey));
        }

        // 私钥格式：seed[0:32] + pubkey[32:64]
        // NSec RawPrivateKey 仅接受 32 字节种子，需提取前 32 字节导入
        var seed = new byte[32];
        Buffer.BlockCopy(privateKey, 0, seed, 0, 32);
        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Import(algorithm, seed, KeyBlobFormat.RawPrivateKey);
        var publicKey = key.Export(KeyBlobFormat.RawPublicKey);
        return new MinisignKeyPair(privateKey, publicKey);
    }

    /// <summary>
    /// 对数据进行签名。
    /// </summary>
    /// <param name="data">要签名的数据。</param>
    /// <param name="privateKey">Ed25519 私钥字节（64 字节：seed + public key）。</param>
    /// <returns>完整签名（128 字节：BLAKE2b-512 指纹 + Ed25519 签名）。</returns>
    public static byte[] Sign(byte[] data, byte[] privateKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(privateKey);

        if (privateKey.Length != 64)
        {
            throw new ArgumentException($"私钥长度必须为 64 字节，实际为 {privateKey.Length}。", nameof(privateKey));
        }

        // 1. 计算 BLAKE2b-512 哈希（Blake2Fast 2.0.0 默认输出 64 字节）
        var hash = Blake2Fast.Blake2b.ComputeHash(data);

        // 2. 使用 Ed25519 私钥对哈希签名
        // NSec RawPrivateKey 仅接受 32 字节种子，需提取前 32 字节导入
        var seed = new byte[32];
        Buffer.BlockCopy(privateKey, 0, seed, 0, 32);
        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Import(algorithm, seed, KeyBlobFormat.RawPrivateKey);
        var signature = algorithm.Sign(key, hash);

        // 3. 组合指纹和签名
        var result = new byte[FullSignatureLength];
        Buffer.BlockCopy(hash, 0, result, 0, HashLength);
        Buffer.BlockCopy(signature, 0, result, HashLength, SignatureLength);
        return result;
    }

    /// <summary>
    /// 对文件内容进行签名，返回 Base64 编码的签名。
    /// </summary>
    /// <param name="filePath">要签名的文件路径。</param>
    /// <param name="privateKey">Ed25519 私钥字节（64 字节）。</param>
    /// <returns>Base64 编码的完整签名。</returns>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    public static string SignFile(string filePath, byte[] privateKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(privateKey);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"文件不存在: {filePath}", filePath);
        }

        var data = File.ReadAllBytes(filePath);
        var signature = Sign(data, privateKey);
        return Convert.ToBase64String(signature);
    }

    /// <summary>
    /// 将签名写入 .minisign.sig 文件，同时写入公钥到 .minisign.pub 文件。
    /// </summary>
    /// <param name="signatureFilePath">签名文件路径。</param>
    /// <param name="base64Signature">Base64 编码的签名。</param>
    /// <param name="keyPair">密钥对，用于写入公钥文件。</param>
    public static void WriteSignatureFile(string signatureFilePath, string base64Signature, MinisignKeyPair keyPair)
    {
        ArgumentException.ThrowIfNullOrEmpty(signatureFilePath);
        ArgumentException.ThrowIfNullOrEmpty(base64Signature);
        ArgumentNullException.ThrowIfNull(keyPair);

        var content = MinisignFormat.FormatSignatureFile(
            comment: null,
            base64Signature: base64Signature,
            base64KeyFingerprint: keyPair.KeyFingerprint);
        File.WriteAllText(signatureFilePath, content);
    }

    /// <summary>
    /// 将公钥写入文件。
    /// </summary>
    /// <param name="publicKeyFilePath">公钥文件路径。</param>
    /// <param name="keyPair">密钥对。</param>
    public static void WritePublicKeyFile(string publicKeyFilePath, MinisignKeyPair keyPair)
    {
        ArgumentException.ThrowIfNullOrEmpty(publicKeyFilePath);
        ArgumentNullException.ThrowIfNull(keyPair);

        var content = MinisignFormat.FormatPublicKeyFile(
            comment: null,
            base64PublicKey: keyPair.GetPublicKeyBase64(),
            base64KeyFingerprint: keyPair.KeyFingerprint);
        File.WriteAllText(publicKeyFilePath, content);
    }

    /// <summary>
    /// 将私钥写入文件（注意：私钥文件应妥善保管）。
    /// </summary>
    /// <param name="privateKeyFilePath">私钥文件路径。</param>
    /// <param name="keyPair">密钥对。</param>
    public static void WritePrivateKeyFile(string privateKeyFilePath, MinisignKeyPair keyPair)
    {
        ArgumentException.ThrowIfNullOrEmpty(privateKeyFilePath);
        ArgumentNullException.ThrowIfNull(keyPair);

        var content = MinisignFormat.FormatPrivateKeyFile(
            comment: null,
            base64PrivateKey: keyPair.GetPrivateKeyBase64(),
            base64KeyFingerprint: keyPair.KeyFingerprint);
        File.WriteAllText(privateKeyFilePath, content);
    }
}
