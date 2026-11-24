namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// MIME 到资源类型的映射与后缀推断（对应 res-downloader 的 <c>MimeMap</c> 机制）。
/// </summary>
public static class MimeTypeMap
{
    /// <summary>
    /// 默认 MIME 映射表，与 res-downloader 的默认配置保持一致。
    /// </summary>
    /// <returns>全新的可变字典。</returns>
    public static Dictionary<string, MimeEntry> CreateDefault() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = new() { Type = ResourceClassify.Image, Suffix = ".png" },
        ["image/webp"] = new() { Type = ResourceClassify.Image, Suffix = ".webp" },
        ["image/jpeg"] = new() { Type = ResourceClassify.Image, Suffix = ".jpeg" },
        ["image/jpg"] = new() { Type = ResourceClassify.Image, Suffix = ".jpg" },
        ["image/gif"] = new() { Type = ResourceClassify.Image, Suffix = ".gif" },
        ["image/avif"] = new() { Type = ResourceClassify.Image, Suffix = ".avif" },
        ["image/bmp"] = new() { Type = ResourceClassify.Image, Suffix = ".bmp" },
        ["image/tiff"] = new() { Type = ResourceClassify.Image, Suffix = ".tiff" },
        ["image/heic"] = new() { Type = ResourceClassify.Image, Suffix = ".heic" },
        ["image/x-icon"] = new() { Type = ResourceClassify.Image, Suffix = ".ico" },
        ["image/svg+xml"] = new() { Type = ResourceClassify.Image, Suffix = ".svg" },
        ["image/vnd.adobe.photoshop"] = new() { Type = ResourceClassify.Image, Suffix = ".psd" },
        ["image/jp2"] = new() { Type = ResourceClassify.Image, Suffix = ".jp2" },
        ["image/jpeg2000"] = new() { Type = ResourceClassify.Image, Suffix = ".jp2" },
        ["image/apng"] = new() { Type = ResourceClassify.Image, Suffix = ".apng" },

        ["audio/mpeg"] = new() { Type = ResourceClassify.Audio, Suffix = ".mp3" },
        ["audio/mp3"] = new() { Type = ResourceClassify.Audio, Suffix = ".mp3" },
        ["audio/wav"] = new() { Type = ResourceClassify.Audio, Suffix = ".wav" },
        ["audio/aiff"] = new() { Type = ResourceClassify.Audio, Suffix = ".aiff" },
        ["audio/x-aiff"] = new() { Type = ResourceClassify.Audio, Suffix = ".aiff" },
        ["audio/aac"] = new() { Type = ResourceClassify.Audio, Suffix = ".aac" },
        ["audio/ogg"] = new() { Type = ResourceClassify.Audio, Suffix = ".ogg" },
        ["audio/flac"] = new() { Type = ResourceClassify.Audio, Suffix = ".flac" },
        ["audio/midi"] = new() { Type = ResourceClassify.Audio, Suffix = ".mid" },
        ["audio/x-midi"] = new() { Type = ResourceClassify.Audio, Suffix = ".mid" },
        ["audio/x-ms-wma"] = new() { Type = ResourceClassify.Audio, Suffix = ".wma" },
        ["audio/opus"] = new() { Type = ResourceClassify.Audio, Suffix = ".opus" },
        ["audio/webm"] = new() { Type = ResourceClassify.Audio, Suffix = ".webm" },
        ["audio/mp4"] = new() { Type = ResourceClassify.Audio, Suffix = ".m4a" },
        ["audio/amr"] = new() { Type = ResourceClassify.Audio, Suffix = ".amr" },

        ["video/mp4"] = new() { Type = ResourceClassify.Video, Suffix = ".mp4" },
        ["video/webm"] = new() { Type = ResourceClassify.Video, Suffix = ".webm" },
        ["video/ogg"] = new() { Type = ResourceClassify.Video, Suffix = ".ogv" },
        ["video/x-msvideo"] = new() { Type = ResourceClassify.Video, Suffix = ".avi" },
        ["video/mpeg"] = new() { Type = ResourceClassify.Video, Suffix = ".mpeg" },
        ["video/quicktime"] = new() { Type = ResourceClassify.Video, Suffix = ".mov" },
        ["video/x-ms-wmv"] = new() { Type = ResourceClassify.Video, Suffix = ".wmv" },
        ["video/3gpp"] = new() { Type = ResourceClassify.Video, Suffix = ".3gp" },
        ["video/x-matroska"] = new() { Type = ResourceClassify.Video, Suffix = ".mkv" },
        ["application/mp4"] = new() { Type = ResourceClassify.Video, Suffix = ".mp4" },
        ["video/mp2t"] = new() { Type = ResourceClassify.Video, Suffix = ".ts" },

        ["audio/video"] = new() { Type = ResourceClassify.Live, Suffix = ".flv" },
        ["video/x-flv"] = new() { Type = ResourceClassify.Live, Suffix = ".flv" },
        ["application/dash+xml"] = new() { Type = ResourceClassify.Live, Suffix = ".mpd" },

        ["application/vnd.apple.mpegurl"] = new() { Type = ResourceClassify.M3u8, Suffix = ".m3u8" },
        ["application/x-mpegurl"] = new() { Type = ResourceClassify.M3u8, Suffix = ".m3u8" },
        ["application/x-mpeg"] = new() { Type = ResourceClassify.M3u8, Suffix = ".m3u8" },
        ["audio/x-mpegurl"] = new() { Type = ResourceClassify.M3u8, Suffix = ".m3u8" },
        ["audio/mpegurl"] = new() { Type = ResourceClassify.M3u8, Suffix = ".m3u8" },

        ["application/pdf"] = new() { Type = ResourceClassify.Pdf, Suffix = ".pdf" },
        ["application/vnd.ms-powerpoint"] = new() { Type = ResourceClassify.Ppt, Suffix = ".ppt" },
        ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] =
            new() { Type = ResourceClassify.Ppt, Suffix = ".pptx" },
        ["application/vnd.ms-excel"] = new() { Type = ResourceClassify.Xls, Suffix = ".xls" },
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] =
            new() { Type = ResourceClassify.Xls, Suffix = ".xlsx" },
        ["text/csv"] = new() { Type = ResourceClassify.Xls, Suffix = ".csv" },
        ["application/msword"] = new() { Type = ResourceClassify.Doc, Suffix = ".doc" },
        ["application/rtf"] = new() { Type = ResourceClassify.Doc, Suffix = ".rtf" },
        ["text/rtf"] = new() { Type = ResourceClassify.Doc, Suffix = ".rtf" },
        ["application/vnd.oasis.opendocument.text"] = new() { Type = ResourceClassify.Doc, Suffix = ".odt" },
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] =
            new() { Type = ResourceClassify.Doc, Suffix = ".docx" },
        ["application/epub+zip"] = new() { Type = ResourceClassify.Doc, Suffix = ".epub" },

        ["font/woff"] = new() { Type = ResourceClassify.Font, Suffix = ".woff" },
        ["font/woff2"] = new() { Type = ResourceClassify.Font, Suffix = ".woff2" },
        ["application/font-woff"] = new() { Type = ResourceClassify.Font, Suffix = ".woff" },
        ["application/x-font-ttf"] = new() { Type = ResourceClassify.Font, Suffix = ".ttf" },

        ["application/octet-stream"] = new() { Type = ResourceClassify.Stream, Suffix = "default" },
        ["binary/octet-stream"] = new() { Type = ResourceClassify.Stream, Suffix = "default" },
        ["application/force-download"] = new() { Type = ResourceClassify.Stream, Suffix = "default" },
    };

    /// <summary>常见扩展名到资源类型的兜底映射（用于 MIME 为 octet-stream 的 CDN）。</summary>
    private static readonly Dictionary<string, (string Classify, string Suffix)> ExtensionMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["mp4"] = (ResourceClassify.Video, ".mp4"),
            ["m4v"] = (ResourceClassify.Video, ".m4v"),
            ["mov"] = (ResourceClassify.Video, ".mov"),
            ["mkv"] = (ResourceClassify.Video, ".mkv"),
            ["webm"] = (ResourceClassify.Video, ".webm"),
            ["avi"] = (ResourceClassify.Video, ".avi"),
            ["ts"] = (ResourceClassify.Video, ".ts"),
            ["m3u8"] = (ResourceClassify.M3u8, ".m3u8"),
            ["mpd"] = (ResourceClassify.Live, ".mpd"),
            ["flv"] = (ResourceClassify.Live, ".flv"),
            ["mp3"] = (ResourceClassify.Audio, ".mp3"),
            ["m4a"] = (ResourceClassify.Audio, ".m4a"),
            ["aac"] = (ResourceClassify.Audio, ".aac"),
            ["wav"] = (ResourceClassify.Audio, ".wav"),
            ["flac"] = (ResourceClassify.Audio, ".flac"),
            ["ogg"] = (ResourceClassify.Audio, ".ogg"),
            ["opus"] = (ResourceClassify.Audio, ".opus"),
            ["jpg"] = (ResourceClassify.Image, ".jpg"),
            ["jpeg"] = (ResourceClassify.Image, ".jpeg"),
            ["png"] = (ResourceClassify.Image, ".png"),
            ["gif"] = (ResourceClassify.Image, ".gif"),
            ["webp"] = (ResourceClassify.Image, ".webp"),
            ["bmp"] = (ResourceClassify.Image, ".bmp"),
            ["avif"] = (ResourceClassify.Image, ".avif"),
            ["heic"] = (ResourceClassify.Image, ".heic"),
            ["svg"] = (ResourceClassify.Image, ".svg"),
            ["pdf"] = (ResourceClassify.Pdf, ".pdf"),
            ["ppt"] = (ResourceClassify.Ppt, ".ppt"),
            ["pptx"] = (ResourceClassify.Ppt, ".pptx"),
            ["xls"] = (ResourceClassify.Xls, ".xls"),
            ["xlsx"] = (ResourceClassify.Xls, ".xlsx"),
            ["csv"] = (ResourceClassify.Xls, ".csv"),
            ["doc"] = (ResourceClassify.Doc, ".doc"),
            ["docx"] = (ResourceClassify.Doc, ".docx"),
            ["rtf"] = (ResourceClassify.Doc, ".rtf"),
            ["ttf"] = (ResourceClassify.Font, ".ttf"),
            ["woff"] = (ResourceClassify.Font, ".woff"),
            ["woff2"] = (ResourceClassify.Font, ".woff2"),
        };

    /// <summary>下载时默认重放的请求头（对应 res-downloader 的 <c>default</c> 集合）。</summary>
    public static readonly string[] DefaultReplayHeaders =
    [
        "User-Agent", "Referer", "Cookie", "Origin", "Accept", "Accept-Language", "X-Requested-With",
    ];

    /// <summary>
    /// 根据 Content-Type 与 URL 解析资源类型与后缀。
    /// </summary>
    /// <param name="map">MIME 映射表。</param>
    /// <param name="contentType">响应 Content-Type（可含 charset）。</param>
    /// <param name="url">资源直链，用于扩展名兜底。</param>
    /// <returns>解析结果；不属于可拦截资源时返回 <c>null</c>。</returns>
    public static ResourceTypeInfo? Resolve(
        IReadOnlyDictionary<string, MimeEntry> map, string? contentType, string url)
    {
        var mime = NormalizeMime(contentType);
        var extension = GetUrlExtension(url);
        var mapped = mime.Length > 0 && map.TryGetValue(mime, out var entry) ? entry : null;

        if (mapped is null)
        {
            // MIME 未知：仅当 URL 扩展名是明确的媒体类型时才兜底，避免把 HTML 噪声收进列表
            return extension.Length > 0 && ExtensionMap.TryGetValue(extension, out var guess)
                ? new ResourceTypeInfo(guess.Classify, guess.Suffix)
                : null;
        }

        var suffix = mapped.Suffix;
        var classify = string.IsNullOrWhiteSpace(mapped.Type) ? ResourceClassify.Stream : mapped.Type;

        if (string.Equals(suffix, "default", StringComparison.OrdinalIgnoreCase))
        {
            if (extension.Length > 0)
            {
                suffix = "." + extension;

                // octet-stream 等通用类型可以借扩展名细化
                if (classify == ResourceClassify.Stream && ExtensionMap.TryGetValue(extension, out var refined))
                {
                    classify = refined.Classify;
                }
            }
            else
            {
                suffix = ".bin";
            }
        }

        if (!suffix.StartsWith('.'))
        {
            suffix = "." + suffix;
        }

        return new ResourceTypeInfo(classify, suffix);
    }

    /// <summary>
    /// 取 MIME 的类型/后缀（不依赖 URL 扩展名），用于响应头改写判断。
    /// </summary>
    /// <param name="map">MIME 映射表。</param>
    /// <param name="contentType">响应 Content-Type。</param>
    /// <returns>映射项；未配置返回 <c>null</c>。</returns>
    public static MimeEntry? Find(IReadOnlyDictionary<string, MimeEntry> map, string? contentType)
    {
        var mime = NormalizeMime(contentType);
        return mime.Length > 0 && map.TryGetValue(mime, out var entry) ? entry : null;
    }

    /// <summary>取 MimeMap 中出现过的全部资源类型（去重，用于前端筛选项）。</summary>
    /// <param name="map">MIME 映射表。</param>
    /// <returns>类型列表。</returns>
    public static List<string> DistinctClassifies(IReadOnlyDictionary<string, MimeEntry> map) =>
        map.Values
            .Select(entry => entry.Type)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 规范化 Content-Type（小写、去参数）。
    /// </summary>
    /// <param name="contentType">原始 Content-Type。</param>
    /// <returns>规范化的 MIME。</returns>
    public static string NormalizeMime(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        var index = contentType.IndexOf(';', StringComparison.Ordinal);
        var mime = index >= 0 ? contentType[..index] : contentType;
        return mime.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// 从 URL 路径中取扩展名（小写，不含点）。
    /// </summary>
    /// <param name="url">URL。</param>
    /// <returns>扩展名；无法解析返回空字符串。</returns>
    public static string GetUrlExtension(string? url)
    {
        if (string.IsNullOrEmpty(url))
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
        if (slash >= 0)
        {
            path = path[(slash + 1)..];
        }

        var dot = path.LastIndexOf('.');
        if (dot < 0 || dot == path.Length - 1)
        {
            return string.Empty;
        }

        var extension = path[(dot + 1)..].ToLowerInvariant();
        return extension.Length is > 0 and <= 8 ? extension : string.Empty;
    }
}

/// <summary>
/// 资源类型解析结果。
/// </summary>
/// <param name="Classify">资源类型（见 <see cref="ResourceClassify"/>）。</param>
/// <param name="Suffix">保存后缀（含点）。</param>
public sealed record ResourceTypeInfo(string Classify, string Suffix);
