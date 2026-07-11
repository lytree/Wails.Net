using System.Net;

namespace Wails.Net.AssetServer;

/// <summary>
/// 文件系统资源服务器，从文件系统读取资源（开发模式）。
/// 对应 Wails v3 Go 版本 <c>internal/assetserver/fileassets</c> 中的 FileAssetServer。
/// 支持 Range 请求、ETag 缓存及 Last-Modified 头（由基类 AssetServer 统一处理）。
/// </summary>
public class FileAssetServer : AssetServer
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
    /// 使用指定根路径构造 <see cref="FileAssetServer" /> 实例。
    /// </summary>
    /// <param name="rootPath">资源文件系统的根路径。</param>
    public FileAssetServer(string rootPath)
        : base(CreateOptions(rootPath))
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        _rootPath = rootPath;
    }

    /// <summary>
    /// 从文件系统读取指定路径的文件内容。
    /// 路径会相对于根路径进行解析，并防止路径穿越攻击。
    /// </summary>
    /// <param name="path">资源相对路径。</param>
    /// <returns>文件内容字节组；若文件不存在或路径非法则返回 null。</returns>
    public byte[]? ReadFile(string path)
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

        return File.ReadAllBytes(fullPath);
    }

    /// <summary>
    /// 获取指定路径文件的最后修改时间（UTC），用于设置 Last-Modified 头。
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
    /// 核心资源读取方法，委托给 <see cref="ReadFile" /> 从文件系统读取。
    /// </summary>
    /// <param name="path">请求的资源路径。</param>
    /// <returns>文件内容字节组；若文件不存在则返回 null。</returns>
    protected override byte[]? ReadAssetCore(string path)
    {
        return ReadFile(path);
    }

    /// <summary>
    /// 处理 HTTP 请求时额外写入 Last-Modified 头。
    /// 在基类处理之后补充文件相关的元数据头。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public override async Task ServeHttpAsync(HttpListenerContext context, CancellationToken cancellationToken = default)
    {
        var path = context.Request.Url?.AbsolutePath.Split('?')[0] ?? "/";
        var lastModified = GetLastModified(path);

        if (lastModified is not null)
        {
            context.Response.Headers[Headers.LastModified] =
                lastModified.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        await base.ServeHttpAsync(context, cancellationToken);
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

    /// <summary>
    /// 根据根路径创建资源服务器选项。
    /// </summary>
    /// <param name="rootPath">资源根路径。</param>
    /// <returns>配置好的 <see cref="AssetOptions" /> 实例。</returns>
    private static AssetOptions CreateOptions(string rootPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        return new AssetOptions
        {
            Handler = "file",
            RootPath = rootPath
        };
    }
}
