using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 文件系统监听插件，提供文件和目录变化监听功能。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-fs-watch</c>。
/// 使用 <see cref="FileSystemWatcher"/> 监听文件系统变化，
/// 通过应用事件系统将变化事件转发到前端。
/// </summary>
public class FsWatchPlugin : IPlugin, IDisposable
{
    /// <summary>插件名称</summary>
    public string Name => "fs-watch";

    /// <summary>
    /// 监听器状态记录，包含路径、递归标志和文件扩展名过滤。
    /// </summary>
    private sealed class WatchState : IDisposable
    {
        /// <summary>文件系统监听器实例。</summary>
        public FileSystemWatcher Watcher { get; }

        /// <summary>监听路径。</summary>
        public string Path { get; }

        /// <summary>是否递归监听子目录。</summary>
        public bool Recursive { get; }

        /// <summary>文件扩展名过滤列表（为空表示监听所有文件）。</summary>
        public string[] Extensions { get; }

        /// <summary>
        /// 构造监听器状态。
        /// </summary>
        /// <param name="watcher">文件系统监听器实例。</param>
        /// <param name="path">监听路径。</param>
        /// <param name="recursive">是否递归监听。</param>
        /// <param name="extensions">文件扩展名过滤。</param>
        public WatchState(FileSystemWatcher watcher, string path, bool recursive, string[] extensions)
        {
            Watcher = watcher;
            Path = path;
            Recursive = recursive;
            Extensions = extensions;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Watcher.EnableRaisingEvents = false;
            Watcher.Dispose();
        }
    }

    /// <summary>
    /// 监听器字典，按监听 ID 索引。
    /// </summary>
    private static readonly ConcurrentDictionary<int, WatchState> s_watches = new();

    /// <summary>
    /// 下一个监听 ID 生成器，线程安全递增。
    /// </summary>
    private static int s_nextId = 1;

    private bool _disposed;

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册文件系统监听相关命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        context.Commands.MapCommand("fswatch.watch",
            (Func<string, bool, string?, int>)((path, recursive, extensionsJson) =>
                StartWatch(path, recursive, extensionsJson)));

        context.Commands.MapCommand("fswatch.unwatch",
            (Action<int>)(id => StopWatch(id)));

        context.Commands.MapCommand("fswatch.unwatchAll",
            (Action)(() => StopAll()));

        context.Commands.MapCommand("fswatch.listWatches",
            (Func<int[]>)(() => s_watches.Keys.ToArray()));

        context.Commands.MapCommand("fswatch.isWatching",
            (Func<int, bool>)(id => s_watches.ContainsKey(id)));
    }

    /// <summary>
    /// 开始监听指定路径的文件系统变化。
    /// </summary>
    /// <param name="path">要监听的目录路径。</param>
    /// <param name="recursive">是否递归监听子目录。</param>
    /// <param name="extensionsJson">文件扩展名过滤的 JSON 数组（如 <c>[".txt",".json"]</c>），为 null 或空表示监听所有文件。</param>
    /// <returns>监听器 ID。若路径无效返回 0。</returns>
    private static int StartWatch(string path, bool recursive, string? extensionsJson)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return 0;
        }

        // 解析扩展名过滤
        string[] extensions = [];
        if (!string.IsNullOrWhiteSpace(extensionsJson))
        {
            try
            {
                extensions = JsonSerializer.Deserialize<string[]>(extensionsJson) ?? [];
            }
            catch
            {
                extensions = [];
            }
        }

        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size |
                           NotifyFilters.CreationTime | NotifyFilters.Attributes
        };

        var id = Interlocked.Increment(ref s_nextId);
        var state = new WatchState(watcher, path, recursive, extensions);

        // 注册事件处理器
        watcher.Changed += (_, e) => OnFileChanged(id, e, "changed");
        watcher.Created += (_, e) => OnFileChanged(id, e, "created");
        watcher.Deleted += (_, e) => OnFileChanged(id, e, "deleted");
        watcher.Renamed += (_, e) => OnFileRenamed(id, e);
        watcher.Error += (_, e) => OnWatcherError(id, e);

        watcher.EnableRaisingEvents = true;
        s_watches[id] = state;

        return id;
    }

    /// <summary>
    /// 停止指定 ID 的监听器。
    /// </summary>
    /// <param name="id">监听器 ID。</param>
    private static void StopWatch(int id)
    {
        if (s_watches.TryRemove(id, out var state))
        {
            state.Dispose();
        }
    }

    /// <summary>
    /// 停止所有监听器。
    /// </summary>
    private static void StopAll()
    {
        foreach (var pair in s_watches)
        {
            pair.Value.Dispose();
        }

        s_watches.Clear();
    }

    /// <summary>
    /// 文件变化事件处理器，通过应用事件系统转发到前端。
    /// </summary>
    /// <param name="id">监听器 ID。</param>
    /// <param name="e">文件系统事件参数。</param>
    /// <param name="changeType">变化类型（changed/created/deleted）。</param>
    private static void OnFileChanged(int id, FileSystemEventArgs e, string changeType)
    {
        // 检查扩展名过滤
        if (!ShouldNotify(e.FullPath, id))
        {
            return;
        }

        var data = new
        {
            id,
            type = changeType,
            path = e.FullPath,
            name = e.Name
        };

        Application.Get()?.Events.Emit($"fswatch:changed:{id}", data, null);
    }

    /// <summary>
    /// 文件重命名事件处理器。
    /// </summary>
    /// <param name="id">监听器 ID。</param>
    /// <param name="e">重命名事件参数。</param>
    private static void OnFileRenamed(int id, RenamedEventArgs e)
    {
        if (!ShouldNotify(e.FullPath, id))
        {
            return;
        }

        var data = new
        {
            id,
            type = "renamed",
            path = e.FullPath,
            name = e.Name,
            oldPath = e.OldFullPath,
            oldName = e.OldName
        };

        Application.Get()?.Events.Emit($"fswatch:changed:{id}", data, null);
    }

    /// <summary>
    /// 监听器错误事件处理器。
    /// </summary>
    /// <param name="id">监听器 ID。</param>
    /// <param name="e">错误事件参数。</param>
    private static void OnWatcherError(int id, ErrorEventArgs e)
    {
        var data = new
        {
            id,
            type = "error",
            error = e.GetException()?.Message ?? "Unknown error"
        };

        Application.Get()?.Events.Emit($"fswatch:changed:{id}", data, null);
    }

    /// <summary>
    /// 检查文件路径是否符合扩展名过滤条件。
    /// </summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="id">监听器 ID。</param>
    /// <returns>是否应通知此次变化。</returns>
    private static bool ShouldNotify(string filePath, int id)
    {
        if (!s_watches.TryGetValue(id, out var state))
        {
            return false;
        }

        // 无扩展名过滤时通知所有变化
        if (state.Extensions.Length == 0)
        {
            return true;
        }

        // 目录总是通知
        if (Directory.Exists(filePath))
        {
            return true;
        }

        var ext = System.IO.Path.GetExtension(filePath);
        return Array.Exists(state.Extensions, e =>
            string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAll();
        _disposed = true;
    }
}
