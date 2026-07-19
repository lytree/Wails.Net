namespace Wails.Net.AssetServer;

/// <summary>
/// 嵌入式资源服务器，从用户提供的资源读取委托中读取文件。
/// 对应 Wails v3 Go 版本 <c>internal/assetserver/bundledassets</c> 中的 BundledAssetServer。
/// 支持通配符路径匹配与多名称查找。
/// <para>
/// M7 起，此类内部委托给 <see cref="BundledAssetProvider"/>（组合模式），
/// 自身仅作为便捷包装保留，以维持向后兼容。新代码应优先直接使用
/// <see cref="BundledAssetProvider"/> + <see cref="AssetServer(AssetOptions, IAssetProvider)"/>。
/// </para>
/// <para>
/// 遵循 AGENTS.md §3.4 禁令：不直接使用 <c>System.Reflection.Assembly</c>，
/// 资源读取通过 <c>Func&lt;string, byte[]?&gt;</c> 委托抽象，由调用方提供具体实现。
/// 用户可在应用代码中通过 <c>Assembly.GetManifestResourceStream</c> 构造委托，
/// 或使用其他机制（如源生成器生成的静态字典、文件系统等）。
/// </para>
/// </summary>
public class BundledAssetServer : AssetServer
{
    /// <summary>
    /// 获取内部资源提供者，负责实际的嵌入资源读取。
    /// </summary>
    private BundledAssetProvider ProviderImpl => (BundledAssetProvider)Provider!;

    /// <summary>
    /// 获取资源根路径。
    /// </summary>
    public string RootPath => ProviderImpl.RootPath;

    /// <summary>
    /// 获取可选的资源名列表（用于通配符匹配）。
    /// </summary>
    public IReadOnlyList<string> ResourceNames => ProviderImpl.ResourceNames;

    /// <summary>
    /// 使用指定根路径、资源读取委托和可选资源名列表构造 <see cref="BundledAssetServer" /> 实例。
    /// </summary>
    /// <param name="rootPath">资源根路径，通常为程序集名称（用于资源名前缀匹配）。</param>
    /// <param name="resourceReader">资源读取委托，接收资源名返回字节数组或 null。</param>
    /// <param name="resourceNames">可选的资源名列表，启用通配符匹配。</param>
    /// <param name="enableSpaFallback">是否启用 SPA 路由回退，默认为 false。</param>
    /// <param name="defaultDocument">SPA 回退使用的默认文档名称，默认为 "index.html"。</param>
    public BundledAssetServer(
        string rootPath,
        Func<string, byte[]?> resourceReader,
        IEnumerable<string>? resourceNames = null,
        bool enableSpaFallback = false,
        string defaultDocument = "index.html")
        : base(
            CreateOptions(rootPath, enableSpaFallback, defaultDocument),
            new BundledAssetProvider(rootPath, resourceReader, resourceNames))
    {
    }

    /// <summary>
    /// 从嵌入资源中读取指定路径的文件内容。
    /// 依次尝试：精确匹配 → 通配符匹配（若提供资源名列表）→ 根路径前缀匹配。
    /// </summary>
    /// <param name="path">资源路径（相对路径，使用 / 作为分隔符）。</param>
    /// <returns>资源内容字节组；若资源不存在则返回 null。</returns>
    public byte[]? ReadResource(string path)
    {
        return ProviderImpl.ReadAsync(path).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 核心资源读取方法，委托给 <see cref="BundledAssetProvider.ReadAsync" /> 从嵌入资源读取。
    /// </summary>
    /// <param name="path">请求的资源路径。</param>
    /// <returns>资源内容字节组；若资源不存在则返回 null。</returns>
    protected override byte[]? ReadAssetCore(string path)
    {
        return ProviderImpl.ReadAsync(path).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 根据根路径和 SPA 回退配置创建资源服务器选项。
    /// </summary>
    /// <param name="rootPath">资源根路径。</param>
    /// <param name="enableSpaFallback">是否启用 SPA 路由回退。</param>
    /// <param name="defaultDocument">SPA 回退使用的默认文档名称。</param>
    /// <returns>配置好的 <see cref="AssetOptions" /> 实例。</returns>
    private static AssetOptions CreateOptions(string rootPath, bool enableSpaFallback, string defaultDocument)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        return new AssetOptions
        {
            Handler = "bundled",
            RootPath = rootPath,
            EnableSpaFallback = enableSpaFallback,
            DefaultDocument = string.IsNullOrEmpty(defaultDocument) ? "index.html" : defaultDocument
        };
    }
}
