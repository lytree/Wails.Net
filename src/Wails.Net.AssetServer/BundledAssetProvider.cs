namespace Wails.Net.AssetServer;

/// <summary>
/// 嵌入式资源提供者，从用户提供的资源读取委托中读取文件。
/// 对应 Wails v3 Go 版本 <c>internal/assetserver/bundledassets</c> 中的 BundledAssetServer。
/// <para>
/// 实现 <see cref="IAssetProvider"/> 接口，支持资源查找及通配符路径匹配。
/// 与 <see cref="BundledAssetServer"/> 相比，此类不继承 <see cref="AssetServer"/>，
/// 纯粹负责资源读取，可被组合到任意 AssetServer 实例中。
/// </para>
/// <para>
/// 遵循 AGENTS.md §3.4 禁令：不直接使用 <c>System.Reflection.Assembly</c>，
/// 资源读取通过 <c>Func&lt;string, byte[]?&gt;</c> 委托抽象，由调用方提供具体实现。
/// 用户可在应用代码中通过 <c>Assembly.GetManifestResourceStream</c> 构造委托，
/// 或使用其他机制（如源生成器生成的静态字典、文件系统等）。
/// </para>
/// </summary>
public class BundledAssetProvider : IAssetProvider
{
    /// <summary>
    /// 资源根路径（通常为程序集名称，用作资源名前缀）。
    /// </summary>
    private readonly string _rootPath;

    /// <summary>
    /// 资源读取委托：接收资源名，返回字节数组或 null。
    /// </summary>
    private readonly Func<string, byte[]?> _resourceReader;

    /// <summary>
    /// 可选的资源名列表，用于通配符匹配。为空时不支持通配符匹配。
    /// </summary>
    private readonly List<string> _resourceNames;

    /// <summary>
    /// 获取资源根路径。
    /// </summary>
    public string RootPath => _rootPath;

    /// <summary>
    /// 获取可选的资源名列表（用于通配符匹配）。
    /// </summary>
    public IReadOnlyList<string> ResourceNames => _resourceNames;

    /// <summary>
    /// 使用指定根路径、资源读取委托和可选资源名列表构造 <see cref="BundledAssetProvider" /> 实例。
    /// </summary>
    /// <param name="rootPath">资源根路径，通常为程序集名称（用于资源名前缀匹配）。</param>
    /// <param name="resourceReader">资源读取委托，接收资源名返回字节数组或 null。</param>
    /// <param name="resourceNames">可选的资源名列表，启用通配符匹配（如 <c>Assembly.GetManifestResourceNames()</c>）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="resourceReader"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="rootPath"/> 为 null 或空字符串。</exception>
    public BundledAssetProvider(string rootPath, Func<string, byte[]?> resourceReader, IEnumerable<string>? resourceNames = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        ArgumentNullException.ThrowIfNull(resourceReader);
        _rootPath = rootPath;
        _resourceReader = resourceReader;
        _resourceNames = resourceNames?.ToList() ?? new List<string>();
    }

    /// <summary>
    /// 异步从嵌入资源中读取指定路径的文件内容。
    /// 依次尝试：精确匹配 → 通配符匹配（若提供资源名列表）→ 根路径前缀匹配。
    /// </summary>
    /// <param name="path">资源路径（相对路径，使用 / 作为分隔符）。</param>
    /// <param name="cancellationToken">取消令牌（资源读取为同步操作，此参数保留供未来扩展）。</param>
    /// <returns>资源内容字节组；若资源不存在则返回 null。</returns>
    public Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Task.FromResult<byte[]?>(null);
        }

        var resourceName = NormalizeResourceName(path);

        // 精确匹配
        var bytes = _resourceReader(resourceName);
        if (bytes is not null)
        {
            return Task.FromResult<byte[]?>(bytes);
        }

        // 通配符匹配：仅当资源名列表非空且资源名包含通配符时启用
        if ((resourceName.Contains('*') || resourceName.Contains('?')) && _resourceNames.Count > 0)
        {
            var matched = FindByPattern(resourceName);
            if (matched is not null)
            {
                bytes = _resourceReader(matched);
                if (bytes is not null)
                {
                    return Task.FromResult<byte[]?>(bytes);
                }
            }
        }

        // 前缀匹配：在资源名前添加根路径
        if (!string.IsNullOrEmpty(_rootPath) && !resourceName.StartsWith(_rootPath + ".", StringComparison.Ordinal))
        {
            var fullName = _rootPath + "." + resourceName;
            bytes = _resourceReader(fullName);
            if (bytes is not null)
            {
                return Task.FromResult<byte[]?>(bytes);
            }
        }

        return Task.FromResult<byte[]?>(null);
    }

    /// <summary>
    /// 获取指定路径资源的最后修改时间（UTC）。
    /// 嵌入式资源无修改时间，始终返回 null。
    /// </summary>
    /// <param name="path">资源路径。</param>
    /// <returns>始终返回 null。</returns>
    public DateTime? GetLastModified(string path) => null;

    /// <summary>
    /// 使用通配符模式在资源名列表中查找匹配的资源名。
    /// 将 <c>*</c> 转为正则 <c>.*</c>，<c>?</c> 转为 <c>.</c>。
    /// </summary>
    /// <param name="pattern">包含通配符的资源名模式。</param>
    /// <returns>首个匹配的资源名；若无匹配则返回 null。</returns>
    private string? FindByPattern(string pattern)
    {
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        var regex = new System.Text.RegularExpressions.Regex(regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var name in _resourceNames)
        {
            if (regex.IsMatch(name))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// 将资源路径规范化为嵌入资源名称。
    /// 将路径分隔符替换为点号，并去除前导分隔符。
    /// </summary>
    /// <param name="path">原始资源路径。</param>
    /// <returns>规范化后的嵌入资源名称。</returns>
    private static string NormalizeResourceName(string path)
    {
        var normalized = path.Replace('/', '.').Replace('\\', '.');
        return normalized.TrimStart('.');
    }
}
