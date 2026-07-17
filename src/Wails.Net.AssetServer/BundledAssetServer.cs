using System.Reflection;

namespace Wails.Net.AssetServer;

/// <summary>
/// 嵌入式资源服务器，从程序集嵌入资源中读取文件。
/// 对应 Wails v3 Go 版本 <c>internal/assetserver/bundledassets</c> 中的 BundledAssetServer。
/// 支持多程序集合并、嵌入式资源查找及通配符路径匹配。
/// <para>
/// M7 起，此类内部委托给 <see cref="BundledAssetProvider"/>（组合模式），
/// 自身仅作为便捷包装保留，以维持向后兼容。新代码应优先直接使用
/// <see cref="BundledAssetProvider"/> + <see cref="AssetServer(AssetOptions, IAssetProvider)"/>。
/// </para>
/// </summary>
public class BundledAssetServer : AssetServer
{
    /// <summary>
    /// 获取内部资源提供者，负责实际的嵌入资源读取。
    /// </summary>
    private BundledAssetProvider ProviderImpl => (BundledAssetProvider)Provider!;

    /// <summary>
    /// 获取嵌入资源所在的程序集列表。
    /// </summary>
    public IReadOnlyList<Assembly> Assemblies => ProviderImpl.Assemblies;

    /// <summary>
    /// 使用指定程序集构造 <see cref="BundledAssetServer" /> 实例。
    /// 资源根路径默认使用程序集名称作为前缀。
    /// </summary>
    /// <param name="assembly">包含嵌入资源的程序集。</param>
    public BundledAssetServer(Assembly assembly)
        : base(CreateOptions(assembly), new BundledAssetProvider(assembly))
    {
    }

    /// <summary>
    /// 使用多个程序集构造 <see cref="BundledAssetServer" /> 实例。
    /// 资源查找时按程序集注册顺序依次查找。
    /// </summary>
    /// <param name="assemblies">包含嵌入资源的程序集集合。</param>
    public BundledAssetServer(IEnumerable<Assembly> assemblies)
        : base(CreateOptions(assemblies.FirstOrDefault()), new BundledAssetProvider(assemblies))
    {
    }

    /// <summary>
    /// 添加额外程序集到资源查找列表。
    /// </summary>
    /// <param name="assembly">要添加的程序集。</param>
    public void AddAssembly(Assembly assembly)
    {
        ProviderImpl.AddAssembly(assembly);
    }

    /// <summary>
    /// 从嵌入资源中读取指定路径的文件内容。
    /// 依次在所有注册的程序集中查找匹配的嵌入资源。
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
    /// 根据程序集创建资源服务器选项。
    /// </summary>
    /// <param name="assembly">包含嵌入资源的程序集。</param>
    /// <returns>配置好的 <see cref="AssetOptions" /> 实例。</returns>
    private static AssetOptions CreateOptions(Assembly? assembly)
    {
        var name = assembly?.GetName().Name ?? "bundled";
        return new AssetOptions
        {
            Handler = "bundled",
            RootPath = name
        };
    }
}
