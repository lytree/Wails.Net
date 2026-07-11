using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 文件系统插件，提供文件读写、目录操作、文件元数据查询等命令。
/// 对应 Tauri v2 的 <c>@tauri-apps/api/fs</c> 和 Wails v3 的文件系统绑定。
/// 内置路径穿越防护，可配置沙箱根目录限制可访问范围。
/// </summary>
public class FileSystemPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "filesystem";

    private readonly string? _sandboxRoot;

    /// <summary>
    /// 初始化无沙箱限制的文件系统插件。
    /// 警告：无沙箱时前端可访问系统任意路径，仅适用于受信任的应用。
    /// </summary>
    public FileSystemPlugin()
    {
        _sandboxRoot = null;
    }

    /// <summary>
    /// 初始化带沙箱限制的文件系统插件，前端只能访问指定根目录下的文件。
    /// </summary>
    /// <param name="sandboxRoot">沙箱根目录绝对路径。</param>
    public FileSystemPlugin(string sandboxRoot)
    {
        _sandboxRoot = Path.GetFullPath(sandboxRoot);
    }

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册文件系统相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 文件操作
        context.Commands.MapCommand("fs.read", (Func<string, string>)(path => File.ReadAllText(GetSafePath(path))));
        context.Commands.MapCommand("fs.write", (Action<string, string>)((path, content) => File.WriteAllText(GetSafePath(path), content)));
        context.Commands.MapCommand("fs.exists", (Func<string, bool>)(path => File.Exists(GetSafePath(path))));
        context.Commands.MapCommand("fs.delete", (Action<string>)(path => File.Delete(GetSafePath(path))));
        context.Commands.MapCommand("fs.readBinary", (Func<string, byte[]>)(path => File.ReadAllBytes(GetSafePath(path))));
        context.Commands.MapCommand("fs.writeBinary", (Action<string, byte[]>)((path, data) => File.WriteAllBytes(GetSafePath(path), data)));
        context.Commands.MapCommand("fs.copy", (Action<string, string>)((src, dest) => File.Copy(GetSafePath(src), GetSafePath(dest), true)));
        context.Commands.MapCommand("fs.rename", (Action<string, string>)((src, dest) => File.Move(GetSafePath(src), GetSafePath(dest))));
        context.Commands.MapCommand("fs.stat", (Func<string, FileStat>)(path => GetFileStat(GetSafePath(path))));

        // 异步命令
        context.Commands.MapCommand("fs.readAsync", (Func<string, Task<string>>)(async path => await File.ReadAllTextAsync(GetSafePath(path))));
        context.Commands.MapCommand("fs.writeAsync", (Func<string, string, Task>)(async (path, content) => await File.WriteAllTextAsync(GetSafePath(path), content)));

        // 目录操作
        context.Commands.MapCommand("fs.mkdir", (Action<string>)(path => Directory.CreateDirectory(GetSafePath(path))));
        context.Commands.MapCommand("fs.rmdir", (Action<string, bool>)((path, recursive) => Directory.Delete(GetSafePath(path), recursive)));
        context.Commands.MapCommand("fs.existsDir", (Func<string, bool>)(path => Directory.Exists(GetSafePath(path))));
        context.Commands.MapCommand("fs.readDir", (Func<string, string[]>)(path => Directory.GetFileSystemEntries(GetSafePath(path))));
        context.Commands.MapCommand("fs.readDirRecursive", (Func<string, string[]>)(path => Directory.GetFileSystemEntries(GetSafePath(path), "*", SearchOption.AllDirectories)));
    }

    /// <summary>
    /// 将相对路径解析为安全的绝对路径，防止路径穿越攻击。
    /// 当配置了沙箱根目录时，路径必须在沙箱范围内。
    /// </summary>
    /// <param name="path">前端传入的路径。</param>
    /// <returns>安全的绝对路径。</returns>
    /// <exception cref="UnauthorizedAccessException">路径超出沙箱范围。</exception>
    /// <exception cref="ArgumentException">路径无效。</exception>
    private string GetSafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("路径不能为空。", nameof(path));
        }

        var fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : _sandboxRoot is not null
                ? Path.GetFullPath(path, _sandboxRoot)
                : Path.GetFullPath(path);

        if (_sandboxRoot is not null)
        {
            var rootWithSep = _sandboxRoot.EndsWith(Path.DirectorySeparatorChar)
                ? _sandboxRoot
                : _sandboxRoot + Path.DirectorySeparatorChar;

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!fullPath.StartsWith(rootWithSep, comparison) && fullPath != _sandboxRoot)
            {
                throw new UnauthorizedAccessException($"路径超出沙箱范围: {path}");
            }
        }

        return fullPath;
    }

    /// <summary>
    /// 获取文件或目录的元数据信息。
    /// </summary>
    private static FileStat GetFileStat(string fullPath)
    {
        var info = new FileInfo(fullPath);
        return new FileStat
        {
            Path = fullPath,
            Size = info.Exists ? info.Length : 0,
            IsDirectory = (File.GetAttributes(fullPath) & FileAttributes.Directory) == FileAttributes.Directory,
            LastModified = info.LastWriteTimeUtc,
            Created = info.CreationTimeUtc,
            IsReadOnly = info.IsReadOnly
        };
    }
}

/// <summary>
/// 文件元数据信息。
/// 对应 Tauri v2 的 <c>FileInfo</c> 结构。
/// </summary>
public sealed class FileStat
{
    /// <summary>文件路径</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>文件大小（字节）</summary>
    public long Size { get; set; }

    /// <summary>是否为目录</summary>
    public bool IsDirectory { get; set; }

    /// <summary>最后修改时间（UTC）</summary>
    public DateTime LastModified { get; set; }

    /// <summary>创建时间（UTC）</summary>
    public DateTime Created { get; set; }

    /// <summary>是否只读</summary>
    public bool IsReadOnly { get; set; }
}
