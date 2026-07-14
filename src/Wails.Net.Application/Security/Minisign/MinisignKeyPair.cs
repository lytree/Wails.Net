namespace Wails.Net.Application.Security.Minisign;

/// <summary>
/// minisign 密钥对。
/// 对应 Tauri v2 的更新签名密钥：Ed25519 私钥（64 字节）+ 公钥（32 字节）+ 指纹。
/// <para>
/// 指纹为公钥的 BLAKE2b-256 哈希的十六进制小写表示，用于快速比对密钥。
/// </para>
/// </summary>
public sealed class MinisignKeyPair
{
    /// <summary>
    /// Ed25519 私钥（64 字节：32 字节种子 + 32 字节公钥）。
    /// </summary>
    public byte[] PrivateKey { get; }

    /// <summary>
    /// Ed25519 公钥（32 字节）。
    /// </summary>
    public byte[] PublicKey { get; }

    /// <summary>
    /// 公钥指纹（BLAKE2b-256 的十六进制小写表示，64 字符）。
    /// </summary>
    public string KeyFingerprint { get; }

    /// <summary>
    /// 初始化 <see cref="MinisignKeyPair"/>。
    /// </summary>
    /// <param name="privateKey">Ed25519 私钥（64 字节）。</param>
    /// <param name="publicKey">Ed25519 公钥（32 字节）。</param>
    /// <exception cref="ArgumentNullException">参数为 null。</exception>
    /// <exception cref="ArgumentException">密钥长度不符合 Ed25519 规范。</exception>
    public MinisignKeyPair(byte[] privateKey, byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(publicKey);

        if (privateKey.Length != 64)
        {
            throw new ArgumentException($"Ed25519 私钥长度必须为 64 字节，实际为 {privateKey.Length}。", nameof(privateKey));
        }
        if (publicKey.Length != 32)
        {
            throw new ArgumentException($"Ed25519 公钥长度必须为 32 字节，实际为 {publicKey.Length}。", nameof(publicKey));
        }

        PrivateKey = privateKey;
        PublicKey = publicKey;
        KeyFingerprint = ComputeFingerprint(publicKey);
    }

    /// <summary>
    /// 计算公钥的 BLAKE2b-256 指纹。
    /// </summary>
    /// <param name="publicKey">公钥字节。</param>
    /// <returns>十六进制小写表示的指纹（64 字符）。</returns>
    public static string ComputeFingerprint(byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        // Blake2Fast 2.0.0：使用 ComputeHash(digestLength, input) 直接获取 32 字节指纹
        var fingerprint = Blake2Fast.Blake2b.ComputeHash(32, publicKey);
        return Convert.ToHexString(fingerprint).ToLowerInvariant();
    }

    /// <summary>
    /// 将公钥编码为 Base64 字符串。
    /// </summary>
    /// <returns>Base64 编码的公钥。</returns>
    public string GetPublicKeyBase64() => Convert.ToBase64String(PublicKey);

    /// <summary>
    /// 将私钥编码为 Base64 字符串。
    /// </summary>
    /// <returns>Base64 编码的私钥。</returns>
    public string GetPrivateKeyBase64() => Convert.ToBase64String(PrivateKey);
}
