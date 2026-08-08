namespace Wails.Net.Plugins.Updater.Services;

/// <summary>
/// 更新资产匹配器：决定 release 资产是否被采纳为下载源。
/// 对应 Wails v3 updater 的资产匹配器（matcher）机制。
/// </summary>
/// <remarks>
/// 对齐 Wails v3 beta.2（PR #5861）：默认匹配器忽略安装程序（installer）资产，
/// 避免把 <c>*-installer.exe</c> / <c>*-setup.exe</c> / <c>*.msi</c> 等安装包
/// 误当作应用更新包下载。
/// </remarks>
public static class UpdateAssetMatcher
{
    /// <summary>
    /// 默认排除的资产名称关键词（不区分大小写的子串匹配）。
    /// 覆盖常见安装包命名：installer / setup / msi。
    /// </summary>
    private static readonly string[] DefaultExcludedKeywords = ["installer", "setup", ".msi"];

    /// <summary>
    /// 判断资产名称是否命中默认排除规则（安装程序类资产）。
    /// </summary>
    /// <param name="assetName">资产文件名（可含路径，仅取文件名部分判断）。</param>
    /// <returns>命中排除规则返回 true。</returns>
    public static bool IsDefaultExcluded(string assetName)
    {
        if (string.IsNullOrEmpty(assetName))
        {
            return false;
        }

        var name = Path.GetFileName(assetName);
        foreach (var keyword in DefaultExcludedKeywords)
        {
            if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
