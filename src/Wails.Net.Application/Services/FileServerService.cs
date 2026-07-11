using Wails.Net.Application.Options;

namespace Wails.Net.Application.Services;

/// <summary>
/// 文件服务，提供安全的本地文件读写功能。
/// 对应 Wails v3 Go 版本 pkg/services/fileserver。
/// 通过路径穿越防护确保所有文件操作限制在允许的根目录内。
/// </summary>
public class FileServerService : IServiceStartup, IServiceShutdown
{
    /// <summary>
    /// 允许的文件操作根目录。
    /// </summary>
    private string _rootPath;

    /// <summary>
    /// 线程安全锁，用于保护文件写入操作。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 获取或设置允许的文件操作根目录。
    /// 在服务启动前设置以自定义根目录。
    /// </summary>
    public string RootPath
    {
        get => _rootPath;
        set => _rootPath = value;
    }

    /// <summary>
    /// 使用当前工作目录作为根目录构造文件服务实例。
    /// </summary>
    public FileServerService()
    {
        _rootPath = Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// 使用指定根目录构造文件服务实例。
    /// </summary>
    /// <param name="rootPath">允许文件操作的根目录。</param>
    public FileServerService(string rootPath)
    {
        _rootPath = rootPath;
    }

    /// <summary>
    /// 服务启动，初始化根目录配置。
    /// </summary>
    /// <param name="options">应用选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动操作的异步任务。</returns>
    public Task ServiceStartup(ApplicationOptions options, CancellationToken cancellationToken)
    {
        _rootPath = Path.GetFullPath(_rootPath);
        if (!Directory.Exists(_rootPath))
        {
            Directory.CreateDirectory(_rootPath);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 服务关闭，清理资源。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示关闭操作的异步任务。</returns>
    public Task ServiceShutdown(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 读取指定路径的文件内容。
    /// </summary>
    /// <param name="path">相对于根目录的文件路径。</param>
    /// <returns>文件文本内容。</returns>
    /// <exception cref="UnauthorizedAccessException">路径超出根目录范围。</exception>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    public string ReadFile(string path)
    {
        var fullPath = GetSafePath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"文件不存在: {path}", fullPath);
        }
        lock (_lock)
        {
            return File.ReadAllText(fullPath);
        }
    }

    /// <summary>
    /// 将内容写入指定路径的文件。
    /// 若文件已存在则覆盖。
    /// </summary>
    /// <param name="path">相对于根目录的文件路径。</param>
    /// <param name="content">要写入的内容。</param>
    /// <exception cref="UnauthorizedAccessException">路径超出根目录范围。</exception>
    public void WriteFile(string path, string content)
    {
        var fullPath = GetSafePath(path);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        lock (_lock)
        {
            File.WriteAllText(fullPath, content);
        }
    }

    /// <summary>
    /// 检查指定路径的文件是否存在。
    /// </summary>
    /// <param name="path">相对于根目录的文件路径。</param>
    /// <returns>文件存在返回 true，否则返回 false。</returns>
    /// <exception cref="UnauthorizedAccessException">路径超出根目录范围。</exception>
    public bool FileExists(string path)
    {
        var fullPath = GetSafePath(path);
        return File.Exists(fullPath);
    }

    /// <summary>
    /// 删除指定路径的文件。
    /// 若文件不存在则不执行任何操作。
    /// </summary>
    /// <param name="path">相对于根目录的文件路径。</param>
    /// <exception cref="UnauthorizedAccessException">路径超出根目录范围。</exception>
    public void DeleteFile(string path)
    {
        var fullPath = GetSafePath(path);
        lock (_lock)
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
    }

    /// <summary>
    /// 计算安全路径，防止路径穿越攻击。
    /// 确保解析后的完整路径位于根目录内。
    /// </summary>
    /// <param name="relativePath">相对路径。</param>
    /// <returns>解析后的完整路径。</returns>
    /// <exception cref="UnauthorizedAccessException">路径超出根目录范围。</exception>
    private string GetSafePath(string relativePath)
    {
        var root = Path.GetFullPath(_rootPath);
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        var fullPath = Path.IsPathRooted(relativePath)
            ? Path.GetFullPath(relativePath)
            : Path.GetFullPath(relativePath, root);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(rootWithSep, comparison) && fullPath != root)
        {
            throw new UnauthorizedAccessException($"路径穿越攻击被阻止: {relativePath}");
        }

        return fullPath;
    }
}
