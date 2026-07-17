using System.Reflection;

namespace Wails.Net.AssetServer;

/// <summary>
/// 嵌入式资源提供者，从程序集嵌入资源中读取文件。
/// 对应 Wails v3 Go 版本 <c>internal/assetserver/bundledassets</c> 中的 BundledAssetServer。
/// <para>
/// 实现 <see cref="IAssetProvider"/> 接口，支持多程序集合并、嵌入式资源查找及通配符路径匹配。
/// 与 <see cref="BundledAssetServer"/> 相比，此类不继承 <see cref="AssetServer"/>，
/// 纯粹负责资源读取，可被组合到任意 AssetServer 实例中。
/// </para>
/// </summary>
public class BundledAssetProvider : IAssetProvider
{
    /// <summary>
    /// 包含嵌入资源的程序集列表（支持多程序集合并查找）。
    /// </summary>
    private readonly List<Assembly> _assemblies = new();

    /// <summary>
    /// 嵌入资源名称缓存：程序集 → 排序后的资源名列表，用于通配符匹配。
    /// </summary>
    private readonly Dictionary<Assembly, List<string>> _resourceNamesCache = new();

    /// <summary>
    /// 获取嵌入资源所在的程序集列表。
    /// </summary>
    public IReadOnlyList<Assembly> Assemblies => _assemblies;

    /// <summary>
    /// 使用指定程序集构造 <see cref="BundledAssetProvider" /> 实例。
    /// </summary>
    /// <param name="assembly">包含嵌入资源的程序集。</param>
    public BundledAssetProvider(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assemblies.Add(assembly);
    }

    /// <summary>
    /// 使用多个程序集构造 <see cref="BundledAssetProvider" /> 实例。
    /// 资源查找时按程序集注册顺序依次查找。
    /// </summary>
    /// <param name="assemblies">包含嵌入资源的程序集集合。</param>
    public BundledAssetProvider(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        foreach (var assembly in assemblies)
        {
            _assemblies.Add(assembly);
        }
    }

    /// <summary>
    /// 添加额外程序集到资源查找列表。
    /// </summary>
    /// <param name="assembly">要添加的程序集。</param>
    public void AddAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (!_assemblies.Contains(assembly))
        {
            _assemblies.Add(assembly);
        }
    }

    /// <summary>
    /// 异步从嵌入资源中读取指定路径的文件内容。
    /// 依次在所有注册的程序集中查找匹配的嵌入资源。
    /// </summary>
    /// <param name="path">资源路径（相对路径，使用 / 作为分隔符）。</param>
    /// <param name="cancellationToken">取消令牌（嵌入资源读取为同步操作，此参数保留供未来扩展）。</param>
    /// <returns>资源内容字节组；若资源不存在则返回 null。</returns>
    public Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Task.FromResult<byte[]?>(null);
        }

        var resourceName = NormalizeResourceName(path);

        foreach (var assembly in _assemblies)
        {
            // 精确匹配
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is not null)
            {
                return Task.FromResult<byte[]?>(ReadStream(stream));
            }

            // 通配符匹配：若资源名包含通配符，使用模式匹配
            if (resourceName.Contains('*') || resourceName.Contains('?'))
            {
                var matched = FindByPattern(assembly, resourceName);
                if (matched is not null)
                {
                    using var matchedStream = assembly.GetManifestResourceStream(matched);
                    if (matchedStream is not null)
                    {
                        return Task.FromResult<byte[]?>(ReadStream(matchedStream));
                    }
                }
            }

            // 后缀匹配：尝试在资源名前添加程序集默认命名空间
            var assemblyName = assembly.GetName().Name;
            if (!string.IsNullOrEmpty(assemblyName) && !resourceName.StartsWith(assemblyName + ".", StringComparison.Ordinal))
            {
                var fullName = assemblyName + "." + resourceName;
                using var fullStream = assembly.GetManifestResourceStream(fullName);
                if (fullStream is not null)
                {
                    return Task.FromResult<byte[]?>(ReadStream(fullStream));
                }
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
    /// 从流中读取全部内容到字节数组。
    /// </summary>
    /// <param name="stream">输入流。</param>
    /// <returns>流内容的字节数组。</returns>
    private static byte[] ReadStream(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>
    /// 获取指定程序集的所有嵌入资源名称（带缓存）。
    /// </summary>
    /// <param name="assembly">目标程序集。</param>
    /// <returns>排序后的资源名称列表。</returns>
    private List<string> GetResourceNames(Assembly assembly)
    {
        if (!_resourceNamesCache.TryGetValue(assembly, out var names))
        {
            names = [.. assembly.GetManifestResourceNames()];
            names.Sort(StringComparer.Ordinal);
            _resourceNamesCache[assembly] = names;
        }

        return names;
    }

    /// <summary>
    /// 使用通配符模式在程序集嵌入资源中查找匹配的资源名。
    /// 将 <c>*</c> 转为正则 <c>.*</c>，<c>?</c> 转为 <c>.</c>。
    /// </summary>
    /// <param name="assembly">目标程序集。</param>
    /// <param name="pattern">包含通配符的资源名模式。</param>
    /// <returns>首个匹配的资源名；若无匹配则返回 null。</returns>
    private string? FindByPattern(Assembly assembly, string pattern)
    {
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        var regex = new System.Text.RegularExpressions.Regex(regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var name in GetResourceNames(assembly))
        {
            if (regex.IsMatch(name))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// 将资源路径规范化为程序集嵌入资源名称。
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
