using Wails.Net.Application.Plugins;
using WailsApplication = Wails.Net.Application.Application;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Security;

namespace Wails.Net.Plugins.FileSystem;

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
        // 能力声明与参数级 Scope 映射通过 CommandSecurity 显式传入，避免声明被运行时丢弃（SEC-H-002）
        var readScope = CommandSecurity.Require("fs:allow-read").WithScope("path", "fs:allow-read");
        var writeScope = CommandSecurity.Require("fs:allow-write").WithScope("path", "fs:allow-write");
        var copyScope = CommandSecurity.Require("fs:allow-read", "fs:allow-write")
            .WithScope("src", "fs:allow-read")
            .WithScope("dest", "fs:allow-write");

        context.Commands.MapCommand("fs.read", (Func<string, string>)ReadText, readScope);
        context.Commands.MapCommand("fs.write", (Action<string, string>)WriteText, writeScope);
        context.Commands.MapCommand("fs.exists", (Func<string, bool>)ExistsFile, readScope);
        context.Commands.MapCommand("fs.delete", (Action<string>)DeleteFile, writeScope);
        context.Commands.MapCommand("fs.readBinary", (Func<string, byte[]>)ReadBinary, readScope);
        context.Commands.MapCommand("fs.writeBinary", (Action<string, byte[]>)WriteBinary, writeScope);

        // 文件操作（前端 wails.fs.* API 名，与 @wailsnet/runtime/src/api/fs.ts 一致）
        context.Commands.MapCommand("fs.readTextFile", (Func<string, string>)ReadText, readScope);
        context.Commands.MapCommand("fs.writeTextFile", (Action<string, string>)WriteText, writeScope);
        context.Commands.MapCommand("fs.readBinaryFile", (Func<string, byte[]>)ReadBinary, readScope);
        context.Commands.MapCommand("fs.writeBinaryFile", (Action<string, byte[]>)WriteBinary, writeScope);
        context.Commands.MapCommand("fs.remove", (Action<string>)DeleteFile, writeScope);

        // 其他文件操作
        context.Commands.MapCommand("fs.copy", (Action<string, string>)CopyFile, copyScope);
        context.Commands.MapCommand("fs.rename", (Action<string, string>)RenameFile, copyScope);
        context.Commands.MapCommand("fs.stat", (Func<string, FileStat>)StatFile, readScope);

        // 异步命令
        context.Commands.MapCommandAsync("fs.readAsync", (Func<string, Task<string>>)ReadTextAsync, readScope);
        context.Commands.MapCommandAsync("fs.writeAsync", (Func<string, string, Task>)WriteTextAsync, writeScope);

        // 目录操作
        context.Commands.MapCommand("fs.mkdir", (Action<string>)MakeDirectory, writeScope);
        context.Commands.MapCommand("fs.rmdir", (Action<string, bool>)RemoveDirectory, writeScope);
        context.Commands.MapCommand("fs.existsDir", (Func<string, bool>)ExistsDirectory, readScope);
        context.Commands.MapCommand("fs.readDir", (Func<string, string[]>)ReadDirectory, readScope);
        context.Commands.MapCommand("fs.readDirRecursive", (Func<string, string[]>)ReadDirectoryRecursive, readScope);
    }

    // ===== 带 [ScopeParameter] 特性的命令方法组 =====
    // 读类命令绑定 fs:allow-read，写类命令绑定 fs:allow-write
    // CommandDispatcher 调度时自动从参数提取路径并进行 Scope 校验

    /// <summary>
    /// 读取文本文件内容（命令 <c>fs.readText</c>，需 <c>fs:allow-read</c>）。
    /// </summary>
    /// <param name="path">文件路径（相对沙箱根目录或绝对路径）。</param>
    /// <returns>文件文本内容。</returns>
    public string ReadText([ScopeParameter("fs:allow-read")] string path)
        => File.ReadAllText(GetSafePath(path));

    /// <summary>
    /// 写入文本文件（命令 <c>fs.writeText</c>，需 <c>fs:allow-write</c>）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="content">要写入的文本内容。</param>
    public void WriteText([ScopeParameter("fs:allow-write")] string path, string content)
        => File.WriteAllText(GetSafePath(path), content);

    /// <summary>
    /// 判断文件是否存在（命令 <c>fs.exists</c>，需 <c>fs:allow-read</c>）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>存在返回 true。</returns>
    public bool ExistsFile([ScopeParameter("fs:allow-read")] string path)
        => File.Exists(GetSafePath(path));

    /// <summary>
    /// 删除文件（命令 <c>fs.remove</c>，需 <c>fs:allow-write</c>）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    public void DeleteFile([ScopeParameter("fs:allow-write")] string path)
        => File.Delete(GetSafePath(path));

    /// <summary>
    /// 读取二进制文件内容（命令 <c>fs.readBinary</c>，需 <c>fs:allow-read</c>）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>文件字节数组。</returns>
    public byte[] ReadBinary([ScopeParameter("fs:allow-read")] string path)
        => File.ReadAllBytes(GetSafePath(path));

    /// <summary>
    /// 写入二进制文件（命令 <c>fs.writeBinary</c>，需 <c>fs:allow-write</c>）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="data">要写入的字节数组。</param>
    public void WriteBinary([ScopeParameter("fs:allow-write")] string path, byte[] data)
        => File.WriteAllBytes(GetSafePath(path), data);

    /// <summary>
    /// 复制文件（命令 <c>fs.copy</c>；源需 <c>fs:allow-read</c>，目标需 <c>fs:allow-write</c>）。
    /// </summary>
    /// <param name="src">源文件路径。</param>
    /// <param name="dest">目标文件路径；已存在时覆盖。</param>
    public void CopyFile(
        [ScopeParameter("fs:allow-read")] string src,
        [ScopeParameter("fs:allow-write")] string dest)
        => File.Copy(GetSafePath(src), GetSafePath(dest), true);

    /// <summary>
    /// 重命名/移动文件（命令 <c>fs.rename</c>；源需 <c>fs:allow-read</c>，目标需 <c>fs:allow-write</c>）。
    /// </summary>
    /// <param name="src">源文件路径。</param>
    /// <param name="dest">目标文件路径。</param>
    public void RenameFile(
        [ScopeParameter("fs:allow-read")] string src,
        [ScopeParameter("fs:allow-write")] string dest)
        => File.Move(GetSafePath(src), GetSafePath(dest));

    /// <summary>
    /// 获取文件元信息（命令 <c>fs.stat</c>，需 <c>fs:allow-read</c>）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>文件大小、时间等元信息。</returns>
    public FileStat StatFile([ScopeParameter("fs:allow-read")] string path)
        => GetFileStat(GetSafePath(path));

    /// <summary>
    /// 异步读取文本文件内容（命令 <c>fs.readTextAsync</c>，需 <c>fs:allow-read</c>）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>文件文本内容。</returns>
    public async Task<string> ReadTextAsync([ScopeParameter("fs:allow-read")] string path)
        => await File.ReadAllTextAsync(GetSafePath(path));

    /// <summary>
    /// 异步写入文本文件（命令 <c>fs.writeAsync</c>，需 <c>fs:allow-write</c>）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="content">要写入的文本内容。</param>
    public async Task WriteTextAsync([ScopeParameter("fs:allow-write")] string path, string content)
        => await File.WriteAllTextAsync(GetSafePath(path), content);

    /// <summary>
    /// 创建目录（命令 <c>fs.mkdir</c>，需 <c>fs:allow-write</c>）。
    /// </summary>
    /// <param name="path">目录路径。</param>
    public void MakeDirectory([ScopeParameter("fs:allow-write")] string path)
        => Directory.CreateDirectory(GetSafePath(path));

    /// <summary>
    /// 删除目录（命令 <c>fs.rmdir</c>，需 <c>fs:allow-write</c>）。
    /// </summary>
    /// <param name="path">目录路径。</param>
    /// <param name="recursive">是否递归删除非空目录。</param>
    public void RemoveDirectory([ScopeParameter("fs:allow-write")] string path, bool recursive)
        => Directory.Delete(GetSafePath(path), recursive);

    /// <summary>
    /// 判断目录是否存在（命令 <c>fs.existsDir</c>，需 <c>fs:allow-read</c>）。
    /// </summary>
    /// <param name="path">目录路径。</param>
    /// <returns>存在返回 true。</returns>
    public bool ExistsDirectory([ScopeParameter("fs:allow-read")] string path)
        => Directory.Exists(GetSafePath(path));

    /// <summary>
    /// 列出目录下的直接子项（命令 <c>fs.readDir</c>，需 <c>fs:allow-read</c>）。
    /// </summary>
    /// <param name="path">目录路径。</param>
    /// <returns>子项路径数组。</returns>
    public string[] ReadDirectory([ScopeParameter("fs:allow-read")] string path)
        => Directory.GetFileSystemEntries(GetSafePath(path));

    /// <summary>
    /// 递归列出目录下的所有子项（命令 <c>fs.readDirRecursive</c>，需 <c>fs:allow-read</c>）。
    /// </summary>
    /// <param name="path">目录路径。</param>
    /// <returns>递归子项路径数组。</returns>
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
