using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Security;

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
    /// 同时注册短名（fs.read）和前端 API 名（fs.readTextFile）两套命令，保持向后兼容。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 声明权限集
        context.Permissions.RegisterPermissionSet("fs:default", "文件系统默认权限集",
            "fs:allow-read", "fs:allow-write", "fs:allow-exists", "fs:allow-mkdir", "fs:allow-remove");
        context.Permissions.DeclarePermission("fs:allow-read", "允许读取文件");
        context.Permissions.DeclarePermission("fs:allow-write", "允许写入文件");
        context.Permissions.DeclarePermission("fs:allow-exists", "允许检查文件是否存在");
        context.Permissions.DeclarePermission("fs:allow-mkdir", "允许创建目录");
        context.Permissions.DeclarePermission("fs:allow-remove", "允许删除文件或目录");

        // 文件操作（短名，向后兼容）— 全部使用带 [ScopeParameter] 特性的方法组注册
        // 对应 Tauri v2 的 fs scope：限定可访问的路径范围
        // 读类命令绑定 fs:allow-read 权限，写类命令绑定 fs:allow-write 权限
        context.Commands.MapCommand("fs.read", (Func<string, string>)ReadText);
        context.Commands.MapCommand("fs.write", (Action<string, string>)WriteText);
        context.Commands.MapCommand("fs.exists", (Func<string, bool>)ExistsFile);
        context.Commands.MapCommand("fs.delete", (Action<string>)DeleteFile);
        context.Commands.MapCommand("fs.readBinary", (Func<string, byte[]>)ReadBinary);
        context.Commands.MapCommand("fs.writeBinary", (Action<string, byte[]>)WriteBinary);

        // 文件操作（前端 wails.fs.* API 名，与 RuntimeGenerator 一致）
        context.Commands.MapCommand("fs.readTextFile", (Func<string, string>)ReadText);
        context.Commands.MapCommand("fs.writeTextFile", (Action<string, string>)WriteText);
        context.Commands.MapCommand("fs.readBinaryFile", (Func<string, byte[]>)ReadBinary);
        context.Commands.MapCommand("fs.writeBinaryFile", (Action<string, byte[]>)WriteBinary);
        context.Commands.MapCommand("fs.remove", (Action<string>)DeleteFile);

        // 其他文件操作
        context.Commands.MapCommand("fs.copy", (Action<string, string>)CopyFile);
        context.Commands.MapCommand("fs.rename", (Action<string, string>)RenameFile);
        context.Commands.MapCommand("fs.stat", (Func<string, FileStat>)StatFile);

        // 异步命令
        context.Commands.MapCommandAsync("fs.readAsync", (Func<string, Task<string>>)ReadTextAsync);
        context.Commands.MapCommandAsync("fs.writeAsync", (Func<string, string, Task>)WriteTextAsync);

        // 目录操作
        context.Commands.MapCommand("fs.mkdir", (Action<string>)MakeDirectory);
        context.Commands.MapCommand("fs.rmdir", (Action<string, bool>)RemoveDirectory);
        context.Commands.MapCommand("fs.existsDir", (Func<string, bool>)ExistsDirectory);
        context.Commands.MapCommand("fs.readDir", (Func<string, string[]>)ReadDirectory);
        context.Commands.MapCommand("fs.readDirRecursive", (Func<string, string[]>)ReadDirectoryRecursive);
    }

    // ===== 带 [ScopeParameter] 特性的命令方法组 =====
    // 读类命令绑定 fs:allow-read，写类命令绑定 fs:allow-write
    // CommandDispatcher 调度时自动从参数提取路径并进行 Scope 校验

    public string ReadText([ScopeParameter("fs:allow-read")] string path)
        => File.ReadAllText(GetSafePath(path));

    public void WriteText([ScopeParameter("fs:allow-write")] string path, string content)
        => File.WriteAllText(GetSafePath(path), content);

    public bool ExistsFile([ScopeParameter("fs:allow-read")] string path)
        => File.Exists(GetSafePath(path));

    public void DeleteFile([ScopeParameter("fs:allow-write")] string path)
        => File.Delete(GetSafePath(path));

    public byte[] ReadBinary([ScopeParameter("fs:allow-read")] string path)
        => File.ReadAllBytes(GetSafePath(path));

    public void WriteBinary([ScopeParameter("fs:allow-write")] string path, byte[] data)
        => File.WriteAllBytes(GetSafePath(path), data);

    public void CopyFile(
        [ScopeParameter("fs:allow-read")] string src,
        [ScopeParameter("fs:allow-write")] string dest)
        => File.Copy(GetSafePath(src), GetSafePath(dest), true);

    public void RenameFile(
        [ScopeParameter("fs:allow-read")] string src,
        [ScopeParameter("fs:allow-write")] string dest)
        => File.Move(GetSafePath(src), GetSafePath(dest));

    public FileStat StatFile([ScopeParameter("fs:allow-read")] string path)
        => GetFileStat(GetSafePath(path));

    public async Task<string> ReadTextAsync([ScopeParameter("fs:allow-read")] string path)
        => await File.ReadAllTextAsync(GetSafePath(path));

    public async Task WriteTextAsync([ScopeParameter("fs:allow-write")] string path, string content)
        => await File.WriteAllTextAsync(GetSafePath(path), content);

    public void MakeDirectory([ScopeParameter("fs:allow-write")] string path)
        => Directory.CreateDirectory(GetSafePath(path));

    public void RemoveDirectory([ScopeParameter("fs:allow-write")] string path, bool recursive)
        => Directory.Delete(GetSafePath(path), recursive);

    public bool ExistsDirectory([ScopeParameter("fs:allow-read")] string path)
        => Directory.Exists(GetSafePath(path));

    public string[] ReadDirectory([ScopeParameter("fs:allow-read")] string path)
        => Directory.GetFileSystemEntries(GetSafePath(path));

    public string[] ReadDirectoryRecursive([ScopeParameter("fs:allow-read")] string path)
        => Directory.GetFileSystemEntries(GetSafePath(path), "*", SearchOption.AllDirectories);

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
