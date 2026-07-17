namespace Wails.Net.AssetServer;

/// <summary>
/// 文件系统资源提供者，从文件系统读取资源（开发模式）。
/// 对应 Wails v3 Go 版本 <c>internal/assetserver/fileassets</c> 中的 FileAssetServer。
/// <para>
/// 实现 <see cref="IAssetProvider"/> 接口，提供路径穿越防护和 Last-Modified 支持。
/// 与 <see cref="FileAssetServer"/> 相比，此类不继承 <see cref="AssetServer"/>，
/// 纯粹负责资源读取，可被组合到任意 AssetServer 实例中。
/// </para>
/// </summary>
public class FileAssetProvider : IAssetProvider
{
    /// <summary>
    /// 资源文件系统的根路径。
    /// </summary>
    private readonly string _rootPath;

    /// <summary>
    /// 获取资源根路径。
    /// </summary>
    public string RootPath => _rootPath;

    /// <summary>
    /// 使用指定根路径构造 <see cref="FileAssetProvider" /> 实例。
    /// </summary>
    /// <param name="rootPath">资源文件系统的根路径。</param>
    public FileAssetProvider(string rootPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        _rootPath = rootPath;
    }

    /// <summary>
    /// 异步从文件系统读取指定路径的文件内容。
    /// 路径会相对于根路径进行解析，并防止路径穿越攻击。
    /// </summary>
    /// <param name="path">资源相对路径。</param>
    /// <param name="cancellationToken">取消令牌（文件读取为同步操作，此参数保留供未来扩展）。</param>
    /// <returns>文件内容字节组；若文件不存在或路径非法则返回 null。</returns>
    public Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Task.FromResult<byte[]?>(null);
        }

        var fullPath = ResolveFullPath(path);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return Task.FromResult<byte[]?>(null);
        }

        return Task.FromResult<byte[]?>(File.ReadAllBytes(fullPath));
    }

    /// <summary>
    /// 获取指定路径文件的最后修改时间（UTC），用于设置 Last-Modified 头和 If-Modified-Since 协商缓存。
    /// </summary>
    /// <param name="path">资源相对路径。</param>
    /// <returns>最后修改时间；若文件不存在则返回 null。</returns>
    public DateTime? GetLastModified(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var fullPath = ResolveFullPath(path);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return null;
        }

        return File.GetLastWriteTimeUtc(fullPath);
    }

    /// <summary>
    /// 将相对路径解析为绝对路径，并确保解析结果位于根路径之下。
    /// </summary>
    /// <param name="path">资源相对路径。</param>
    /// <returns>安全的绝对路径；若路径穿越根路径则返回 null。</returns>
    private string? ResolveFullPath(string path)
    {
        var normalizedPath = path.TrimStart('/', '\\');
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalizedPath));
        var fullRoot = Path.GetFullPath(_rootPath);

        // 确保解析后的路径仍在根路径之下，防止路径穿越。
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fullPath;
    }
}
