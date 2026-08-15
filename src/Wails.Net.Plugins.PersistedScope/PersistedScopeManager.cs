using System.Collections.Concurrent;
using System.Text.Json;
using Wails.Net.Application.Security;

namespace Wails.Net.Plugins.PersistedScope;

/// <summary>
/// 持久化作用域服务接口（P2-2 审计项落地）。
/// <para>
/// 允许在运行时动态添加/移除允许访问的路径，并将变更持久化到 JSON 文件，
/// 以便应用重启后恢复之前的授权配置。对应 Tauri v2 的
/// <c>@tauri-apps/plugin-persisted-scope</c> 能力。
/// </para>
/// <para>
/// 该服务同时实现 <see cref="IScope"/>，可经
/// <see cref="Wails.Net.Application.Plugins.IPermissionRegistrar.BindScope"/> 绑定到
/// <see cref="PermissionManager"/>，使持久化的路径授权参与命令调度时的参数级
/// Scope 校验（如 fs 插件的 <c>[ScopeParameter("fs:allow-read")]</c> 路径参数）。
/// </para>
/// </summary>
public interface IPersistedScope
{
    /// <summary>
    /// 添加允许的路径并持久化。
    /// </summary>
    /// <param name="path">要添加的路径（支持通配符）。</param>
    /// <param name="scopeName">作用域名称（持久化文件名）；null 时使用默认作用域。</param>
    /// <returns>添加成功返回 true；路径无效或已存在返回 false。</returns>
    bool AddPath(string path, string? scopeName = null);

    /// <summary>
    /// 移除允许的路径并持久化。
    /// </summary>
    /// <param name="path">要移除的路径。</param>
    /// <param name="scopeName">作用域名称；null 时使用默认作用域。</param>
    /// <returns>移除成功返回 true；路径不存在返回 false。</returns>
    bool RemovePath(string path, string? scopeName = null);

    /// <summary>
    /// 列出指定作用域允许的所有路径。
    /// </summary>
    /// <param name="scopeName">作用域名称；null 时使用默认作用域。</param>
    /// <returns>允许路径数组；作用域不存在时返回空数组。</returns>
    string[] ListPaths(string? scopeName = null);

    /// <summary>
    /// 清除指定作用域的所有路径并持久化。
    /// </summary>
    /// <param name="scopeName">作用域名称；null 时使用默认作用域。</param>
    void Clear(string? scopeName = null);

    /// <summary>
    /// 校验指定路径是否在任意作用域的允许范围内（合并所有命名作用域）。
    /// 供 <see cref="IScope.Allows"/> 与前端 <c>scope.isAllowed</c> 共用。
    /// </summary>
    /// <param name="path">要校验的路径。</param>
    /// <returns>允许返回 true。</returns>
    bool IsAllowed(string path);

    /// <summary>
    /// 从磁盘加载指定作用域的持久化配置。
    /// </summary>
    /// <param name="scopeName">作用域名称；null 时使用默认作用域。</param>
    /// <returns>加载成功返回 true；文件不存在或解析失败返回 false。</returns>
    bool Load(string? scopeName = null);

    /// <summary>
    /// 将指定作用域立即保存到磁盘。
    /// </summary>
    /// <param name="scopeName">作用域名称；null 时使用默认作用域。</param>
    void Save(string? scopeName = null);
}

/// <summary>
/// 持久化作用域管理器（P2-2 审计项落地）。
/// <para>
/// 维护多个命名作用域（默认作用域使用 <c>persisted-scope.json</c>），
/// 每个作用域包含允许访问的路径集合，支持路径规范化、通配符匹配与 JSON 持久化。
/// 实现 <see cref="IScope"/>：<see cref="Allows"/> 合并所有命名作用域校验，
/// 使绑定到 <see cref="PermissionManager"/> 后，运行时动态授权的路径
/// 能立即参与命令参数级 Scope 校验（对应 Tauri v2 的 fs scope 体验）。
/// </para>
/// </summary>
public sealed class PersistedScopeManager : IPersistedScope, IScope
{
    /// <summary>默认作用域持久化文件路径。</summary>
    public const string DefaultScopePath = "persisted-scope.json";

    /// <summary>命名作用域集合（键为作用域名称 = 持久化文件名）。</summary>
    private readonly ConcurrentDictionary<string, NamedScope> _scopes = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool AddPath(string path, string? scopeName = null)
        => GetOrCreate(scopeName).AddPath(path);

    /// <inheritdoc/>
    public bool RemovePath(string path, string? scopeName = null)
        => _scopes.TryGetValue(NormalizeScopeName(scopeName), out var scope) && scope.RemovePath(path);

    /// <inheritdoc/>
    public string[] ListPaths(string? scopeName = null)
        => _scopes.TryGetValue(NormalizeScopeName(scopeName), out var scope) ? scope.ListPaths() : [];

    /// <inheritdoc/>
    public void Clear(string? scopeName = null)
    {
        if (_scopes.TryGetValue(NormalizeScopeName(scopeName), out var scope))
        {
            scope.Clear();
        }
    }

    /// <inheritdoc/>
    public bool IsAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // 合并所有命名作用域：任一作用域允许即放行
        foreach (var scope in _scopes.Values)
        {
            if (scope.IsAllowed(path))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public bool Load(string? scopeName = null)
        => GetOrCreate(scopeName).Load();

    /// <inheritdoc/>
    public void Save(string? scopeName = null)
    {
        if (_scopes.TryGetValue(NormalizeScopeName(scopeName), out var scope))
        {
            scope.Save();
        }
    }

    /// <summary>
    /// 实现 <see cref="IScope.Allows"/>：校验路径是否在持久化授权的允许范围内。
    /// 与 <see cref="IsAllowed"/> 语义一致，供 <see cref="PermissionManager.ValidateScopes"/> 调用。
    /// </summary>
    /// <param name="value">要校验的路径。</param>
    /// <returns>允许返回 true。</returns>
    public bool Allows(string value) => IsAllowed(value);

    /// <summary>
    /// 规范化作用域名称：null 使用默认文件名。
    /// </summary>
    private static string NormalizeScopeName(string? scopeName)
        => string.IsNullOrWhiteSpace(scopeName) ? DefaultScopePath : scopeName!;

    /// <summary>
    /// 获取或创建命名作用域（构造时自动加载已有持久化配置）。
    /// </summary>
    private NamedScope GetOrCreate(string? scopeName)
        => _scopes.GetOrAdd(NormalizeScopeName(scopeName), static name => new NamedScope(name));

    /// <summary>
    /// 命名作用域：维护允许路径集合，支持规范化、通配符匹配与 JSON 持久化。
    /// </summary>
    private sealed class NamedScope
    {
        /// <summary>持久化文件路径（同时作为作用域名称）。</summary>
        private readonly string _filePath;

        /// <summary>允许的路径集合（规范化后的绝对路径，支持通配符）。</summary>
        private readonly HashSet<string> _allowedPaths = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>对象锁。</summary>
        private readonly object _lock = new();

        /// <summary>
        /// 构造命名作用域，构造时自动加载已有持久化配置。
        /// </summary>
        /// <param name="filePath">持久化文件路径。</param>
        public NamedScope(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        /// <summary>
        /// 添加允许的路径并持久化。
        /// </summary>
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
        /// 移除允许的路径并持久化。
        /// </summary>
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
        public string[] ListPaths()
        {
            lock (_lock)
            {
                return _allowedPaths.ToArray();
            }
        }

        /// <summary>
        /// 清除所有允许的路径并持久化。
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
        /// 校验路径是否在允许范围内（精确匹配 + 通配符匹配）。
        /// </summary>
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

                if (_allowedPaths.Contains(normalized))
                {
                    return true;
                }

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
        /// <returns>加载成功返回 true；文件不存在或解析失败返回 false。</returns>
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
        /// 通配符模式匹配：* 匹配单层路径段，** 匹配多层。
        /// </summary>
        private static bool MatchPattern(string path, string pattern)
        {
            if (!pattern.Contains('*'))
            {
                return string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase);
            }

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
