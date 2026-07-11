namespace Wails.Net.Application.Security;

/// <summary>
/// URL 白名单，用于限制外部导航和资源加载。
/// 对应 Tauri v2 的 allowedUrls 安全配置。
/// </summary>
public sealed class UrlWhitelist
{
    private readonly HashSet<string> _allowedPatterns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>添加允许的 URL 模式（支持 * 通配符，如 https://*.example.com）</summary>
    public void Add(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        _allowedPatterns.Add(pattern);
    }

    /// <summary>检查 URL 是否在白名单中</summary>
    public bool IsAllowed(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        foreach (var pattern in _allowedPatterns)
        {
            if (MatchesPattern(url, pattern)) return true;
        }
        return false;
    }

    /// <summary>通配符模式匹配（* 匹配任意字符）</summary>
    private static bool MatchesPattern(string url, string pattern)
    {
        // 将 * 转为正则 .*，其他字符转义
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(url, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>获取所有允许的模式</summary>
    public IReadOnlyCollection<string> Patterns => _allowedPatterns;
}
