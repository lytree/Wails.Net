namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 应用数据目录与默认下载目录的解析。
/// </summary>
public static class AppPaths
{
    /// <summary>应用数据目录（证书、设置文件所在位置）。</summary>
    public static string DataDirectory { get; } = ResolveDataDirectory();

    /// <summary>默认下载目录。</summary>
    public static string DefaultDownloadDirectory { get; } = ResolveDownloadDirectory();

    private static string ResolveDataDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetTempPath();
        }

        var dir = Path.Combine(root, "Wails.Net.ResDownloader");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ResolveDownloadDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var downloads = Path.Combine(userProfile, "Downloads");
            if (Directory.Exists(downloads))
            {
                return downloads;
            }
        }

        var fallback = Path.Combine(DataDirectory, "downloads");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>
    /// 确保目录存在，并返回规范化的绝对路径。
    /// </summary>
    /// <param name="path">候选目录。</param>
    /// <returns>可用的目录绝对路径。</returns>
    public static string EnsureDirectory(string? path)
    {
        var target = string.IsNullOrWhiteSpace(path) ? DefaultDownloadDirectory : path.Trim();
        Directory.CreateDirectory(target);
        return target;
    }
}
