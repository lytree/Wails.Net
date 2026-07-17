using System.Net;

namespace Wails.Net.AssetServer;

/// <summary>
/// 文件系统资源服务器，从文件系统读取资源（开发模式）。
/// 对应 Wails v3 Go 版本 <c>internal/assetserver/fileassets</c> 中的 FileAssetServer。
/// 支持 Range 请求、ETag 缓存及 Last-Modified 头（由基类 AssetServer 统一处理）。
/// <para>
/// M7 起，此类内部委托给 <see cref="FileAssetProvider"/>（组合模式），
/// 自身仅作为便捷包装保留，以维持向后兼容。新代码应优先直接使用
/// <see cref="FileAssetProvider"/> + <see cref="AssetServer(AssetOptions, IAssetProvider)"/>。
/// </para>
/// </summary>
public class FileAssetServer : AssetServer
{
    /// <summary>
    /// 获取内部资源提供者，负责实际的文件系统读取。
    /// </summary>
    private FileAssetProvider ProviderImpl => (FileAssetProvider)Provider!;

    /// <summary>
    /// 获取资源根路径。
    /// </summary>
    public string RootPath => ProviderImpl.RootPath;

    /// <summary>
    /// 使用指定根路径构造 <see cref="FileAssetServer" /> 实例。
    /// </summary>
    /// <param name="rootPath">资源文件系统的根路径。</param>
    public FileAssetServer(string rootPath)
        : this(rootPath, enableSpaFallback: false, defaultDocument: "index.html")
    {
    }

    /// <summary>
    /// 使用指定根路径和 SPA 回退配置构造 <see cref="FileAssetServer" /> 实例。
    /// </summary>
    /// <param name="rootPath">资源文件系统的根路径。</param>
    /// <param name="enableSpaFallback">是否启用 SPA 路由回退。</param>
    /// <param name="defaultDocument">SPA 回退使用的默认文档名称。</param>
    public FileAssetServer(string rootPath, bool enableSpaFallback, string defaultDocument)
        : base(CreateOptions(rootPath, enableSpaFallback, defaultDocument), CreateProvider(rootPath))
    {
    }

    /// <summary>
    /// 从文件系统读取指定路径的文件内容。
    /// 路径会相对于根路径进行解析，并防止路径穿越攻击。
    /// </summary>
    /// <param name="path">资源相对路径。</param>
    /// <returns>文件内容字节组；若文件不存在或路径非法则返回 null。</returns>
    public byte[]? ReadFile(string path)
    {
        return ProviderImpl.ReadAsync(path).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 获取指定路径文件的最后修改时间（UTC），用于设置 Last-Modified 头和 If-Modified-Since 协商缓存。
    /// </summary>
    /// <param name="path">资源相对路径。</param>
    /// <returns>最后修改时间；若文件不存在则返回 null。</returns>
    public override DateTime? GetLastModified(string path)
    {
        return ProviderImpl.GetLastModified(path);
    }

    /// <summary>
    /// 核心资源读取方法，委托给 <see cref="FileAssetProvider.ReadAsync" /> 从文件系统读取。
    /// </summary>
    /// <param name="path">请求的资源路径。</param>
    /// <returns>文件内容字节组；若文件不存在则返回 null。</returns>
    protected override byte[]? ReadAssetCore(string path)
    {
        return ProviderImpl.ReadAsync(path).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 根据根路径创建资源提供者实例。
    /// </summary>
    /// <param name="rootPath">资源根路径。</param>
    /// <returns>配置好的 <see cref="FileAssetProvider" /> 实例。</returns>
    private static FileAssetProvider CreateProvider(string rootPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        return new FileAssetProvider(rootPath);
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
            Handler = "file",
            RootPath = rootPath,
            EnableSpaFallback = enableSpaFallback,
            DefaultDocument = string.IsNullOrEmpty(defaultDocument) ? "index.html" : defaultDocument
        };
    }
}
