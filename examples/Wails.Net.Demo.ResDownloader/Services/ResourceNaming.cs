using System.Text;
using System.Text.RegularExpressions;

namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 资源命名与域名展示相关的工具（对应 res-downloader 的文件命名规则与 Domain 列）。
/// </summary>
public static partial class ResourceNaming
{
    /// <summary>常见的二级公共后缀，用于截取「一级域名」。</summary>
    private static readonly HashSet<string> TwoLevelSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.cn", "net.cn", "org.cn", "gov.cn", "edu.cn", "ac.cn",
        "com.hk", "com.tw", "com.mo", "co.jp", "co.uk", "co.kr", "com.au", "com.sg",
    };

    /// <summary>
    /// 取用于列表展示的一级域名（如 <c>v.douyin.com</c> → <c>douyin.com</c>）。
    /// </summary>
    /// <param name="url">资源直链。</param>
    /// <returns>一级域名；无法解析返回主机名或空串。</returns>
    public static string TopLevelDomain(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var host = uri.Host;
        if (System.Net.IPAddress.TryParse(host, out _))
        {
            return host;
        }

        var labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length <= 2)
        {
            return host;
        }

        var lastTwo = labels[^2] + "." + labels[^1];
        return TwoLevelSuffixes.Contains(lastTwo)
            ? labels[^3] + "." + lastTwo
            : lastTwo;
    }

    /// <summary>
    /// 生成保存文件名（对齐 res-downloader：描述优先、长度截断、可选时间后缀、后缀补全）。
    /// </summary>
    /// <param name="settings">当前设置。</param>
    /// <param name="resource">资源。</param>
    /// <returns>安全的文件名（含后缀）。</returns>
    public static string BuildFileName(AppSettings settings, ResourceItem resource)
    {
        var baseName = string.Empty;

        if (!string.IsNullOrWhiteSpace(resource.Description))
        {
            baseName = Regex.Replace(resource.Description, @"[^\w\p{IsCJKUnifiedIdeographs}]", string.Empty);
            var limit = settings.FilenameLen <= 0 ? 10 : settings.FilenameLen;
            if (baseName.Length > limit)
            {
                baseName = baseName[..limit];
            }
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = GetUrlFileName(resource.Url);
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = ResourceRepository.ComputeSign(resource.Url);
        }

        baseName = Path.GetFileNameWithoutExtension(baseName);
        baseName = Sanitize(baseName);
        if (baseName.Length == 0)
        {
            baseName = "res-" + DateTime.Now.ToString("HHmmss");
        }

        if (settings.FilenameTime)
        {
            baseName += "_" + DateTime.Now.ToString("yyyyMMddHHmmss");
        }

        var suffix = NormalizeSuffix(resource.Suffix, resource.Url);
        return baseName + suffix;
    }

    /// <summary>
    /// 生成嗅探阶段的展示标题（Content-Disposition → URL 文件名 → 域名兜底）。
    /// </summary>
    /// <param name="url">资源直链。</param>
    /// <param name="contentDisposition">响应 Content-Disposition。</param>
    /// <param name="suffix">资源后缀（含点）。</param>
    /// <returns>展示标题。</returns>
    public static string BuildTitle(string url, string? contentDisposition, string suffix)
    {
        var name = ExtractFileNameFromDisposition(contentDisposition);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = GetUrlFileName(url);
        }

        name = Sanitize(Path.GetFileNameWithoutExtension(name));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = TopLevelDomain(url);
        }

        return string.IsNullOrWhiteSpace(name) ? "resource" : name + suffix;
    }

    /// <summary>
    /// 归一化后缀（空 / 非法时按 URL 扩展名兜底）。
    /// </summary>
    /// <param name="suffix">候选后缀。</param>
    /// <param name="url">资源直链。</param>
    /// <returns>含点的后缀。</returns>
    public static string NormalizeSuffix(string? suffix, string url)
    {
        var value = (suffix ?? string.Empty).Trim();
        if (value.Length == 0 || value.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            var extension = MimeTypeMap.GetUrlExtension(url);
            return extension.Length > 0 ? "." + extension : ".bin";
        }

        return value.StartsWith('.') ? value : "." + value;
    }

    /// <summary>
    /// 从 URL 路径取文件名（解码百分号，去掉查询串）。
    /// </summary>
    /// <param name="url">URL。</param>
    /// <returns>文件名；无法解析返回空串。</returns>
    public static string GetUrlFileName(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var path = url;
        var query = path.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            path = path[..query];
        }

        var fragment = path.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0)
        {
            path = path[..fragment];
        }

        var slash = path.LastIndexOf('/');
        var name = slash >= 0 ? path[(slash + 1)..] : path;

        try
        {
            return Uri.UnescapeDataString(name);
        }
        catch (UriFormatException)
        {
            return name;
        }
    }

    /// <summary>
    /// 清理文件名中的非法字符。
    /// </summary>
    /// <param name="name">原始名称。</param>
    /// <returns>安全名称。</returns>
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        var result = builder.ToString().Trim().Trim('.');
        return result.Length > 120 ? result[..120] : result;
    }

    private static string? ExtractFileNameFromDisposition(string? contentDisposition)
    {
        if (string.IsNullOrWhiteSpace(contentDisposition))
        {
            return null;
        }

        var utf8Match = FileNameUtf8Regex().Match(contentDisposition);
        if (utf8Match.Success)
        {
            try
            {
                return Uri.UnescapeDataString(utf8Match.Groups[1].Value);
            }
            catch (UriFormatException)
            {
                return utf8Match.Groups[1].Value;
            }
        }

        var plainMatch = FileNameRegex().Match(contentDisposition);
        return plainMatch.Success ? plainMatch.Groups[1].Value.Trim('"') : null;
    }

    [GeneratedRegex(@"filename\*\s*=\s*UTF-8''([^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FileNameUtf8Regex();

    [GeneratedRegex(@"filename\s*=\s*([^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FileNameRegex();
}
