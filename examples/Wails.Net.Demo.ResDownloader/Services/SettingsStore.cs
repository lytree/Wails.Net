using System.Text.Json;

namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 应用设置的读写（JSON 持久化到应用数据目录）。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private AppSettings _current;

    /// <summary>
    /// 构造设置存储并立即从磁盘加载。
    /// </summary>
    public SettingsStore()
    {
        _current = LoadFromDisk();
        Normalize(_current);
    }

    /// <summary>设置文件路径。</summary>
    public string FilePath { get; } = Path.Combine(AppPaths.DataDirectory, "settings.json");

    /// <summary>
    /// 获取当前设置的快照。
    /// </summary>
    /// <returns>设置副本。</returns>
    public AppSettings Get()
    {
        lock (_gate)
        {
            return _current.Clone();
        }
    }

    /// <summary>
    /// 保存设置并写入磁盘。
    /// </summary>
    /// <param name="settings">新设置。</param>
    /// <returns>规范化后的设置副本。</returns>
    public AppSettings Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = settings.Clone();
        Normalize(normalized);

        lock (_gate)
        {
            _current = normalized;
            TryWrite(normalized);
            return normalized.Clone();
        }
    }

    /// <summary>
    /// 恢复默认设置（同时删除磁盘文件）。
    /// </summary>
    /// <returns>默认设置。</returns>
    public AppSettings Reset()
    {
        var defaults = new AppSettings();
        Normalize(defaults);

        lock (_gate)
        {
            _current = defaults;
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (IOException)
            {
                // 文件被占用时忽略，下次保存会覆盖
            }

            return defaults.Clone();
        }
    }

    /// <summary>
    /// 规范化并校验设置取值，避免非法配置导致代理无法启动。
    /// </summary>
    /// <param name="settings">待规范化的设置（就地修改）。</param>
    public static void Normalize(AppSettings settings)
    {
        settings.Host = NormalizeHost(settings.Host);
        settings.Port = settings.Port is >= 1 and <= 65535 ? settings.Port : 8899;
        settings.Quality = Math.Clamp(settings.Quality, 0, 4);
        settings.FilenameLen = Math.Clamp(settings.FilenameLen, 0, 9999);
        settings.TaskNumber = Math.Clamp(settings.TaskNumber <= 0 ? 8 : settings.TaskNumber, 1, 64);
        settings.DownNumber = Math.Clamp(settings.DownNumber <= 0 ? 3 : settings.DownNumber, 1, 10);
        settings.Theme = NormalizeChoice(settings.Theme, ["light", "dark", "auto"], "light");
        settings.Locale = NormalizeChoice(settings.Locale, ["zh", "en"], "zh");
        settings.UpstreamProxy = (settings.UpstreamProxy ?? string.Empty).Trim();
        settings.UseHeaders = string.IsNullOrWhiteSpace(settings.UseHeaders) ? "default" : settings.UseHeaders.Trim();
        settings.UserAgent = string.IsNullOrWhiteSpace(settings.UserAgent)
            ? new AppSettings().UserAgent
            : settings.UserAgent.Trim();
        settings.Rule = settings.Rule ?? "*";
        settings.SaveDirectory = AppPaths.EnsureDirectory(settings.SaveDirectory);
        settings.MimeMap = NormalizeMimeMap(settings.MimeMap);
    }

    /// <summary>
    /// 校验 MimeMap：空的用默认表，非法项丢弃，后缀补齐或回落为 default。
    /// </summary>
    /// <param name="map">原始映射。</param>
    /// <returns>规范化后的映射。</returns>
    private static Dictionary<string, MimeEntry> NormalizeMimeMap(Dictionary<string, MimeEntry>? map)
    {
        if (map is null || map.Count == 0)
        {
            return MimeTypeMap.CreateDefault();
        }

        var result = new Dictionary<string, MimeEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in map)
        {
            var mime = MimeTypeMap.NormalizeMime(key);
            if (mime.Length == 0 || value is null || string.IsNullOrWhiteSpace(value.Type))
            {
                continue;
            }

            var suffix = (value.Suffix ?? "default").Trim();
            if (!suffix.Equals("default", StringComparison.OrdinalIgnoreCase) && !suffix.StartsWith('.'))
            {
                suffix = "." + suffix;
            }

            result[mime] = new MimeEntry
            {
                Type = value.Type.Trim().ToLowerInvariant(),
                Suffix = suffix.Equals("default", StringComparison.OrdinalIgnoreCase) ? "default" : suffix.ToLowerInvariant(),
            };
        }

        return result.Count == 0 ? MimeTypeMap.CreateDefault() : result;
    }

    private static string NormalizeHost(string? host)
    {
        var value = (host ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "127.0.0.1";
        }

        // 只允许本机回环或明确的 IPv4/IPv6/主机名，避免误填导致监听失败
        if (value is "localhost" or "*" or "+")
        {
            return value == "localhost" ? "127.0.0.1" : value;
        }

        return Uri.CheckHostName(value) == UriHostNameType.Unknown ? "127.0.0.1" : value;
    }

    private static string NormalizeChoice(string? value, string[] allowed, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return allowed.Contains(normalized) ? normalized : fallback;
    }

    private AppSettings LoadFromDisk()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 设置文件损坏时退回默认值，避免应用无法启动
            return new AppSettings();
        }
    }

    private void TryWrite(AppSettings settings)
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception)
        {
            // 磁盘只读等情况下忽略持久化失败，内存设置仍然生效
        }
    }
}
