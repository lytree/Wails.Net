using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// 文件系统范围持久化插件。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-persisted-scope</c>。
/// 允许在运行时动态添加/移除允许访问的文件系统路径，并将范围变更持久化到 JSON 文件，
/// 以便应用重启后恢复之前的 scope 配置。
/// </summary>
public class PersistedScopePlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "persisted-scope";

    /// <summary>
    /// 默认持久化文件路径。
    /// </summary>
    private const string DefaultScopePath = "persisted-scope.json";

    /// <summary>
    /// 文件系统范围实例（按路径隔离）。
    /// </summary>
    private static readonly ConcurrentDictionary<string, FileSystemScope> _scopes = new();

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务
    }

    /// <summary>
    /// 配置插件，注册文件系统范围管理命令。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 添加允许的路径
        context.Commands.MapCommand("scope.addPath", (Func<string, string?, bool>)((path, scopePath) =>
        {
            var file = string.IsNullOrEmpty(scopePath) ? DefaultScopePath : scopePath;
            var scope = _scopes.GetOrAdd(file, _ => new FileSystemScope(file));
            return scope.AddPath(path);
        }));

        // 移除允许的路径
        context.Commands.MapCommand("scope.removePath", (Func<string, string?, bool>)((path, scopePath) =>
        {
            var file = string.IsNullOrEmpty(scopePath) ? DefaultScopePath : scopePath;
            if (!_scopes.TryGetValue(file, out var scope))
            {
                return false;
            }
            return scope.RemovePath(path);
        }));

        // 列出所有允许的路径
        context.Commands.MapCommand("scope.listPaths", (Func<string?, string[]?>)((scopePath) =>
        {
            var file = string.IsNullOrEmpty(scopePath) ? DefaultScopePath : scopePath;
            if (!_scopes.TryGetValue(file, out var scope))
            {
                return Array.Empty<string>();
            }
            return scope.ListPaths();
        }));

        // 清除所有允许的路径
        context.Commands.MapCommand("scope.clear", (Action<string?>)((scopePath) =>
        {
            var file = string.IsNullOrEmpty(scopePath) ? DefaultScopePath : scopePath;
            if (_scopes.TryGetValue(file, out var scope))
            {
                scope.Clear();
            }
        }));

        // 检查路径是否在允许范围内
        context.Commands.MapCommand("scope.isAllowed", (Func<string, string?, bool>)((path, scopePath) =>
        {
            var file = string.IsNullOrEmpty(scopePath) ? DefaultScopePath : scopePath;
            if (!_scopes.TryGetValue(file, out var scope))
            {
                return false;
            }
            return scope.IsAllowed(path);
        }));

        // 手动保存范围到磁盘
        context.Commands.MapCommand("scope.save", (Action<string?>)((scopePath) =>
        {
            var file = string.IsNullOrEmpty(scopePath) ? DefaultScopePath : scopePath;
            if (_scopes.TryGetValue(file, out var scope))
            {
                scope.Save();
            }
        }));

        // 从磁盘加载范围
        context.Commands.MapCommand("scope.load", (Func<string?, bool>)((scopePath) =>
        {
            var file = string.IsNullOrEmpty(scopePath) ? DefaultScopePath : scopePath;
            var scope = _scopes.GetOrAdd(file, _ => new FileSystemScope(file));
            return scope.Load();
        }));
    }

    /// <summary>
    /// 文件系统范围管理器。
    /// 维护允许访问的路径集合，支持路径规范化、通配符匹配和 JSON 持久化。
    /// </summary>
    private sealed class FileSystemScope
    {
        /// <summary>持久化文件路径。</summary>
        private readonly string _filePath;

        /// <summary>允许的路径集合（规范化后的绝对路径，支持通配符）。</summary>
        private readonly HashSet<string> _allowedPaths = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>对象锁。</summary>
        private readonly object _lock = new();

        /// <summary>
        /// 构造文件系统范围管理器。
        /// </summary>
        /// <param name="filePath">持久化文件路径。</param>
        public FileSystemScope(string filePath)
        {
            _filePath = filePath;
            // 构造时自动加载已有范围
            Load();
        }

        /// <summary>
        /// 添加允许的路径。
        /// 路径会被规范化为绝对路径，支持通配符（* 匹配路径段，** 匹配多层）。
        /// </summary>
        /// <param name="path">要添加的路径。</param>
        /// <returns>添加成功返回 true，路径无效或已存在返回 false。</returns>
        public bool AddPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            lock (_lock)
            {
                var normalized = NormalizePath(path);
                var added = _allowedPaths.Add(normalized);
                if (added)
                {
                    Save();
                }
                return added;
            }
        }

        /// <summary>
        /// 移除允许的路径。
        /// </summary>
        /// <param name="path">要移除的路径。</param>
        /// <returns>移除成功返回 true，路径不存在返回 false。</returns>
        public bool RemovePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            lock (_lock)
            {
                var normalized = NormalizePath(path);
                var removed = _allowedPaths.Remove(normalized);
                if (removed)
                {
                    Save();
                }
                return removed;
            }
        }

        /// <summary>
        /// 列出所有允许的路径。
        /// </summary>
        /// <returns>允许路径数组。</returns>
        public string[] ListPaths()
        {
            lock (_lock)
            {
                return _allowedPaths.ToArray();
            }
        }

        /// <summary>
        /// 清除所有允许的路径。
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _allowedPaths.Clear();
                Save();
            }
        }

        /// <summary>
        /// 检查指定路径是否在允许范围内。
        /// 支持通配符匹配：路径与允许列表中的模式逐一比较。
        /// </summary>
        /// <param name="path">要检查的路径。</param>
        /// <returns>允许返回 true，拒绝返回 false。</returns>
        public bool IsAllowed(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            lock (_lock)
            {
                if (_allowedPaths.Count == 0)
                {
                    return false;
                }

                var normalized = NormalizePath(path);

                // 精确匹配优先
                if (_allowedPaths.Contains(normalized))
                {
                    return true;
                }

                // 通配符匹配
                foreach (var pattern in _allowedPaths)
                {
                    if (MatchPattern(normalized, pattern))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 保存范围到 JSON 文件。
        /// </summary>
        public void Save()
        {
            lock (_lock)
            {
                var json = JsonSerializer.Serialize(new ScopeData { Paths = _allowedPaths.ToList() },
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
        }

        /// <summary>
        /// 从 JSON 文件加载范围。
        /// </summary>
        /// <returns>加载成功返回 true，文件不存在或解析失败返回 false。</returns>
        public bool Load()
        {
            lock (_lock)
            {
                if (!File.Exists(_filePath))
                {
                    return false;
                }

                try
                {
                    var json = File.ReadAllText(_filePath);
                    var data = JsonSerializer.Deserialize<ScopeData>(json);
                    if (data?.Paths is not null)
                    {
                        _allowedPaths.Clear();
                        foreach (var p in data.Paths)
                        {
                            _allowedPaths.Add(NormalizePath(p));
                        }
                        return true;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// 规范化路径：转为绝对路径并统一分隔符。
        /// </summary>
        /// <param name="path">原始路径。</param>
        /// <returns>规范化后的路径。</returns>
        private static string NormalizePath(string path)
        {
            if (path.Contains('*'))
            {
                // 通配符路径不转为绝对路径，仅统一分隔符
                return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        /// <summary>
        /// 通配符模式匹配。
        /// 支持 *（匹配单层路径段）和 **（匹配多层路径段）。
        /// </summary>
        /// <param name="path">要检查的路径。</param>
        /// <param name="pattern">通配符模式。</param>
        /// <returns>匹配返回 true。</returns>
        private static bool MatchPattern(string path, string pattern)
        {
            if (!pattern.Contains('*'))
            {
                return string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase);
            }

            // 将 ** 转为正则 .*，* 转为 [^/\\]*，其他字符转义
            var regexPattern = "^" +
                System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*\\*", ".*")
                    .Replace("\\*", "[^/\\\\]*") +
                "$";
            return System.Text.RegularExpressions.Regex.IsMatch(
                path, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }

    /// <summary>
    /// 范围持久化数据（JSON 序列化用）。
    /// </summary>
    private sealed class ScopeData
    {
        /// <summary>允许的路径列表。</summary>
        public List<string> Paths { get; set; } = new();
    }
}
