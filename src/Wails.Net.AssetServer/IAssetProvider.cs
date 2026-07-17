namespace Wails.Net.AssetServer;

/// <summary>
/// 资源提供者接口，抽象资源读取来源。
/// 对应 Wails v3 Go 版本 assetserver 中资源处理器的抽象。
/// <para>
/// 实现者负责从特定来源（文件系统、嵌入资源、内存等）读取资源内容。
/// AssetServer 通过组合模式持有 <see cref="IAssetProvider"/> 实例，
/// 优先委托资源读取给 Provider，而非依赖类继承重写 <c>ReadAssetCore</c>。
/// </para>
/// </summary>
public interface IAssetProvider
{
    /// <summary>
    /// 异步读取指定路径的资源内容。
    /// </summary>
    /// <param name="path">请求的资源路径（相对路径，使用 / 作为分隔符）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>资源内容字节组；若资源不存在则返回 null。</returns>
    Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定路径资源的最后修改时间（UTC），用于设置 Last-Modified 头和 If-Modified-Since 协商缓存。
    /// </summary>
    /// <param name="path">资源路径。</param>
    /// <returns>最后修改时间（UTC）；若不可用则返回 null。</returns>
    DateTime? GetLastModified(string path);
}
