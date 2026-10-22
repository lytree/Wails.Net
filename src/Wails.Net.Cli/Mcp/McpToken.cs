using System.Security.Cryptography;
using System.Text;

namespace Wails.Net.Cli.Mcp;

/// <summary>
/// MCP 会话令牌生成与常量时间比较。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 的 <c>newMCPToken</c> 与 <c>subtle.ConstantTimeCompare</c>。
/// </summary>
internal static class McpToken
{
    /// <summary>
    /// 生成 32 字节随机令牌（Base64Url 无填充，43 字符）。
    /// </summary>
    /// <returns>会话令牌。</returns>
    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// 常量时间比较令牌，避免计时侧信道泄漏。
    /// </summary>
    /// <param name="candidate">调用方提供的令牌。</param>
    /// <param name="expected">服务端令牌。</param>
    /// <returns>令牌一致时为 <c>true</c>。</returns>
    public static bool Matches(string? candidate, string expected)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var candidateBytes = Encoding.UTF8.GetBytes(candidate);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return candidateBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes);
    }
}
