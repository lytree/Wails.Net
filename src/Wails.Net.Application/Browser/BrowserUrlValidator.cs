using System.Diagnostics.CodeAnalysis;

namespace Wails.Net.Application.Browser;

/// <summary>
/// 浏览器 URL 验证器，对应 Wails v3 Go 版本 messageprocessor_browser.go 中的 ValidateAndSanitizeURL。
/// 仅允许 http/https 协议，拒绝 file:/javascript:/data: 等危险协议，防止 XSS 与本地文件访问。
/// </summary>
public static class BrowserUrlValidator
{
    /// <summary>
    /// 允许的 URI 协议白名单（小写）。
    /// </summary>
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http",
        "https",
        "mailto",
        "tel",
    };

    /// <summary>
    /// 验证并规范化 URL。
    /// </summary>
    /// <param name="url">原始 URL 字符串。</param>
    /// <param name="sanitizedUrl">规范化后的 URL；验证失败时为 null。</param>
    /// <returns>验证是否通过。</returns>
    public static bool TryValidate([NotNullWhen(true)] string? url, [NotNullWhen(true)] out string? sanitizedUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            sanitizedUrl = null;
            return false;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            sanitizedUrl = null;
            return false;
        }

        if (!AllowedSchemes.Contains(uri.Scheme))
        {
            sanitizedUrl = null;
            return false;
        }

        sanitizedUrl = uri.ToString();
        return true;
    }
}
