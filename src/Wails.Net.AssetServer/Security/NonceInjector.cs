using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Wails.Net.AssetServer.Security;

/// <summary>
/// CSP Nonce 注入器。
/// 对应 Tauri v2 的 CSP nonce 防注入机制：生成随机 nonce、注入到 HTML 标签、构建 CSP 头。
/// <para>
/// 使用 <see cref="RandomNumberGenerator"/> 生成密码学安全的 32 字节 base64 nonce，
/// 使用正则表达式匹配 <c>&lt;script&gt;</c> 和 <c>&lt;link rel="stylesheet"&gt;</c> 标签
/// 注入 <c>nonce="..."</c> 属性。
/// </para>
/// </summary>
public static class NonceInjector
{
    /// <summary>
    /// Nonce 字节长度：32 字节（256 位）满足 NIST 推荐的最低安全强度。
    /// </summary>
    private const int NonceByteLength = 32;

    /// <summary>
    /// 匹配未带 nonce 属性的 &lt;script&gt; 标签。
    /// 仅匹配开标签（不含自闭合的 &lt;script/&gt;），避免误处理已带 nonce 的标签。
    /// </summary>
    private static readonly Regex ScriptTagRegex = new(
        @"<script(?![^>]*\snonce=)([^>]*)>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// 匹配未带 nonce 属性的 &lt;link rel="stylesheet"&gt; 标签。
    /// 仅匹配 rel="stylesheet" 的 link 标签。
    /// </summary>
    private static readonly Regex LinkStylesheetRegex = new(
        @"<link(?![^>]*\snonce=)([^>]*\brel=[""']stylesheet[""'][^>]*)>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// 生成一个新的密码学安全 nonce。
    /// </summary>
    /// <returns>32 字节随机数的 base64 编码字符串。</returns>
    public static string GenerateNonce()
    {
        var bytes = RandomNumberGenerator.GetBytes(NonceByteLength);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// 将 nonce 注入到 HTML 的 script 和 link stylesheet 标签。
    /// 仅匹配未带 nonce 属性的标签，避免重复注入。
    /// </summary>
    /// <param name="html">原始 HTML 字符串。</param>
    /// <param name="nonce">要注入的 nonce 值。</param>
    /// <returns>注入 nonce 后的 HTML 字符串。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="html"/> 或 <paramref name="nonce"/> 为 null 时抛出。</exception>
    public static string InjectNonce(string html, string nonce)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(nonce);

        if (html.Length == 0)
        {
            return html;
        }

        var nonceAttr = $" nonce=\"{nonce}\"";

        // 先注入 script 标签，再注入 link stylesheet 标签
        var result = ScriptTagRegex.Replace(html, match =>
        {
            var existingAttrs = match.Groups[1].Value;
            return $"<script{nonceAttr}{existingAttrs}>";
        });

        result = LinkStylesheetRegex.Replace(result, match =>
        {
            var existingAttrs = match.Groups[1].Value;
            return $"<link{nonceAttr}{existingAttrs}>";
        });

        return result;
    }

    /// <summary>
    /// 构建包含 nonce 的 Content-Security-Policy 头部。
    /// 将 <c>'nonce-&lt;value&gt;'</c> 追加到 <c>script-src</c> 指令；
    /// 若基础 CSP 不含 <c>script-src</c>，则追加新的 <c>script-src</c> 指令。
    /// </summary>
    /// <param name="baseCsp">基础 CSP 策略字符串，可为 null 或空。</param>
    /// <param name="nonce">nonce 值。</param>
    /// <returns>包含 nonce 的完整 CSP 头部字符串。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="nonce"/> 为 null 时抛出。</exception>
    public static string BuildCspHeader(string? baseCsp, string nonce)
    {
        ArgumentNullException.ThrowIfNull(nonce);

        var nonceDirective = $"'nonce-{nonce}'";

        if (string.IsNullOrEmpty(baseCsp))
        {
            return $"script-src 'self' {nonceDirective}";
        }

        // 若已存在 script-src 指令，追加 nonce
        var scriptSrcIndex = baseCsp.IndexOf("script-src", StringComparison.OrdinalIgnoreCase);
        if (scriptSrcIndex >= 0)
        {
            // 找到 script-src 指令的结束位置（下一个分号或字符串末尾）
            var semicolonIndex = baseCsp.IndexOf(';', scriptSrcIndex);
            if (semicolonIndex < 0)
            {
                semicolonIndex = baseCsp.Length;
            }

            var insertPosition = semicolonIndex;
            return baseCsp.Insert(insertPosition, $" {nonceDirective}");
        }

        // 若不存在 script-src 指令，追加新的指令
        return $"{baseCsp}; script-src 'self' {nonceDirective}";
    }
}
