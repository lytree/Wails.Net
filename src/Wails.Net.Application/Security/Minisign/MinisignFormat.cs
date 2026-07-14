namespace Wails.Net.Application.Security.Minisign;

/// <summary>
/// minisign 签名文件格式定义。
/// 对应 Tauri v2 的更新签名格式（简化版）：
/// 签名文件为文本格式，包含注释行、Base64 编码的签名和公钥指纹。
/// <para>
/// 文件格式（3 行）：
/// <code>
/// untrusted comment: Wails.Net signature
/// &lt;Base64 编码的签名&gt;
/// &lt;BLAKE2b-32 公钥指纹（十六进制）&gt;
/// </code>
/// </para>
/// </summary>
public static class MinisignFormat
{
    /// <summary>签名文件的默认注释行。</summary>
    public const string SignatureHeader = "untrusted comment: Wails.Net signature";

    /// <summary>公钥文件的默认注释行。</summary>
    public const string PublicKeyHeader = "untrusted comment: Wails.Net public key";

    /// <summary>私钥文件的默认注释行。</summary>
    public const string PrivateKeyHeader = "untrusted comment: Wails.Net private key";

    /// <summary>minisign 签名算法标识（Ed25519 + BLAKE2b）。</summary>
    public const string SignatureAlgorithm = "Ed25519";

    /// <summary>
    /// 格式化签名文件内容。
    /// </summary>
    /// <param name="comment">注释行（可为空，使用默认值）。</param>
    /// <param name="base64Signature">Base64 编码的签名。</param>
    /// <param name="base64KeyFingerprint">Base64 或十六进制编码的公钥指纹。</param>
    /// <returns>完整的签名文件内容。</returns>
    public static string FormatSignatureFile(string? comment, string base64Signature, string base64KeyFingerprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(base64Signature);
        ArgumentException.ThrowIfNullOrEmpty(base64KeyFingerprint);
        var header = string.IsNullOrEmpty(comment) ? SignatureHeader : comment;
        return $"{header}\n{base64Signature}\n{base64KeyFingerprint}\n";
    }

    /// <summary>
    /// 格式化公钥文件内容。
    /// </summary>
    /// <param name="comment">注释行（可为空，使用默认值）。</param>
    /// <param name="base64PublicKey">Base64 编码的公钥。</param>
    /// <param name="base64KeyFingerprint">公钥指纹。</param>
    /// <returns>完整的公钥文件内容。</returns>
    public static string FormatPublicKeyFile(string? comment, string base64PublicKey, string base64KeyFingerprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(base64PublicKey);
        ArgumentException.ThrowIfNullOrEmpty(base64KeyFingerprint);
        var header = string.IsNullOrEmpty(comment) ? PublicKeyHeader : comment;
        return $"{header}\n{base64PublicKey}\n{base64KeyFingerprint}\n";
    }

    /// <summary>
    /// 格式化私钥文件内容。
    /// </summary>
    /// <param name="comment">注释行（可为空，使用默认值）。</param>
    /// <param name="base64PrivateKey">Base64 编码的私钥。</param>
    /// <param name="base64KeyFingerprint">公钥指纹。</param>
    /// <returns>完整的私钥文件内容。</returns>
    public static string FormatPrivateKeyFile(string? comment, string base64PrivateKey, string base64KeyFingerprint)
    {
        ArgumentException.ThrowIfNullOrEmpty(base64PrivateKey);
        ArgumentException.ThrowIfNullOrEmpty(base64KeyFingerprint);
        var header = string.IsNullOrEmpty(comment) ? PrivateKeyHeader : comment;
        return $"{header}\n{base64PrivateKey}\n{base64KeyFingerprint}\n";
    }

    /// <summary>
    /// 解析签名文件内容，返回签名和指纹。
    /// </summary>
    /// <param name="content">签名文件内容。</param>
    /// <param name="base64Signature">输出的 Base64 签名。</param>
    /// <param name="keyFingerprint">输出的公钥指纹。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryParseSignatureFile(string content, out string? base64Signature, out string? keyFingerprint)
    {
        base64Signature = null;
        keyFingerprint = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3)
        {
            return false;
        }

        // 第 1 行是注释，第 2 行是签名，第 3 行是指纹
        base64Signature = lines[1].Trim();
        keyFingerprint = lines[2].Trim();
        return !string.IsNullOrEmpty(base64Signature) && !string.IsNullOrEmpty(keyFingerprint);
    }

    /// <summary>
    /// 解析公钥文件内容，返回公钥和指纹。
    /// </summary>
    /// <param name="content">公钥文件内容。</param>
    /// <param name="base64PublicKey">输出的 Base64 公钥。</param>
    /// <param name="keyFingerprint">输出的公钥指纹。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryParsePublicKeyFile(string content, out string? base64PublicKey, out string? keyFingerprint)
    {
        base64PublicKey = null;
        keyFingerprint = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3)
        {
            return false;
        }

        base64PublicKey = lines[1].Trim();
        keyFingerprint = lines[2].Trim();
        return !string.IsNullOrEmpty(base64PublicKey) && !string.IsNullOrEmpty(keyFingerprint);
    }

    /// <summary>
    /// 解析私钥文件内容，返回私钥和指纹。
    /// </summary>
    /// <param name="content">私钥文件内容。</param>
    /// <param name="base64PrivateKey">输出的 Base64 私钥。</param>
    /// <param name="keyFingerprint">输出的公钥指纹。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryParsePrivateKeyFile(string content, out string? base64PrivateKey, out string? keyFingerprint)
    {
        base64PrivateKey = null;
        keyFingerprint = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3)
        {
            return false;
        }

        base64PrivateKey = lines[1].Trim();
        keyFingerprint = lines[2].Trim();
        return !string.IsNullOrEmpty(base64PrivateKey) && !string.IsNullOrEmpty(keyFingerprint);
    }
}
