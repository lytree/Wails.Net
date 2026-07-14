using System.IO;

namespace Wails.Net.Application.Security;

/// <summary>
/// 文件系统作用域，限制权限可访问的文件路径范围。
/// 对应 Tauri v2 的 fs scope：限定文件系统权限（如 fs:allow-read）可访问的路径列表。
/// 支持递归目录匹配（以目录路径为前缀匹配所有子路径）。
/// </summary>
public sealed class FileSystemScope : IScope
{
    private readonly HashSet<string> _allowedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取允许的路径列表（只读）。
    /// </summary>
    public IReadOnlyCollection<string> AllowedPaths => _allowedPaths;

    /// <summary>
    /// 添加允许的路径。
    /// 路径可以是文件或目录；目录路径会递归匹配其下所有子路径。
    /// </summary>
    /// <param name="path">允许的文件或目录路径。</param>
    public void AddPath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _allowedPaths.Add(fullPath);
    }

    /// <summary>
    /// 移除允许的路径。
    /// </summary>
    /// <param name="path">要移除的路径。</param>
    public void RemovePath(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _allowedPaths.Remove(fullPath);
    }

    /// <summary>
    /// 清除所有允许的路径。
    /// </summary>
    public void Clear() => _allowedPaths.Clear();

    /// <summary>
    /// 校验指定路径是否在允许范围内。
    /// 若路径是已添加目录的子路径，或恰好匹配已添加的文件路径，则返回 true。
    /// </summary>
    /// <param name="value">要校验的文件路径。</param>
    /// <returns>在允许范围内返回 true。</returns>
    public bool Allows(string value)
    {
        if (string.IsNullOrEmpty(value) || _allowedPaths.Count == 0) return false;

        try
        {
            var targetPath = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var allowedPath in _allowedPaths)
            {
                // 精确匹配
                if (string.Equals(targetPath, allowedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 目录前缀匹配：若允许路径是目录，则其下所有子路径都允许
                if (targetPath.StartsWith(allowedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            // 路径解析失败（非法路径格式等）视为不允许
            return false;
        }
    }
}

/// <summary>
/// URL 作用域，限制权限可访问的 URL 范围。
/// 对应 Tauri v2 的 http scope：限定 HTTP 请求权限（如 http:allow-fetch）可访问的 URL 模式。
/// 内部委托给 <see cref="UrlWhitelist"/> 进行通配符模式匹配。
/// </summary>
public sealed class UrlScope : IScope
{
    private readonly UrlWhitelist _whitelist = new();

    /// <summary>
    /// 获取允许的 URL 模式列表（只读）。
    /// </summary>
    public IReadOnlyCollection<string> AllowedPatterns => _whitelist.Patterns;

    /// <summary>
    /// 添加允许的 URL 模式（支持 * 通配符，如 https://*.example.com）。
    /// </summary>
    /// <param name="pattern">允许的 URL 模式。</param>
    public void AddPattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        _whitelist.Add(pattern);
    }

    /// <summary>
    /// 校验指定 URL 是否在允许范围内。
    /// </summary>
    /// <param name="value">要校验的 URL。</param>
    /// <returns>在允许范围内返回 true。</returns>
    public bool Allows(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return _whitelist.IsAllowed(value);
    }
}
