using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Wails.Net.AssetServer;

namespace Wails.Net.Application.Services;

/// <summary>
/// 已注册服务与其关联的 <see cref="ServiceOptions"/> 的元组。
/// </summary>
internal sealed class ServiceEntry
{
    /// <summary>
    /// 服务实例。
    /// </summary>
    public required object Instance { get; init; }

    /// <summary>
    /// 注册时附带的选项。永不为 null（默认为 <see cref="ServiceOptions.Default"/>）。
    /// </summary>
    public required ServiceOptions Options { get; init; }
}

/// <summary>
/// 服务注册表，管理已注册的服务及其选项。
/// <para>
/// 对应 Wails v3 Go 版本 <c>services.go</c> 中 Application 持有的 services 列表。
/// 支持通过 <see cref="ServiceOptions.Route"/> 挂载实现 <see cref="IHttpServiceHandler"/> 的服务到 AssetServer。
/// </para>
/// </summary>
public class ServiceRegistry
{
    /// <summary>
    /// 已注册服务条目列表（含实例与选项）。
    /// </summary>
    private readonly List<ServiceEntry> _entries = new();

    /// <summary>
    /// 同步锁，保护 _entries 和 _routeTable 的并发访问。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 路由前缀到服务处理器的映射（P1-6 新增）。
    /// 仅包含 <see cref="ServiceOptions.Route"/> 非空且服务实现 <see cref="IHttpServiceHandler"/> 的条目。
    /// 键为已规范化的路由前缀（以 <c>/</c> 开头，不以 <c>/</c> 结尾）。
    /// </summary>
    private readonly ConcurrentDictionary<string, IHttpServiceHandler> _routeTable = new(StringComparer.Ordinal);

    /// <summary>
    /// 获取已注册服务的只读列表。
    /// </summary>
    public IReadOnlyList<object> Services
    {
        get
        {
            lock (_lock)
            {
                return _entries.Select(e => e.Instance).ToList().AsReadOnly();
            }
        }
    }

    /// <summary>
    /// 获取已注册服务条目（含选项）的只读列表。
    /// </summary>
    internal IReadOnlyList<ServiceEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToList().AsReadOnly();
            }
        }
    }

    /// <summary>
    /// 注册服务，使用默认选项。
    /// </summary>
    /// <param name="service">要注册的服务实例。</param>
    public void Register(object service)
    {
        Register(service, ServiceOptions.Default);
    }

    /// <summary>
    /// 注册服务并附带选项。
    /// <para>
    /// 对应 Wails v3 Go 版本 <c>NewServiceWithOptions</c> 函数。
    /// 若 <see cref="ServiceOptions.Route"/> 非空且服务实现 <see cref="IHttpServiceHandler"/>，
    /// 同时将路由条目加入路由表供 AssetServer 查询。
    /// </para>
    /// </summary>
    /// <param name="service">要注册的服务实例。</param>
    /// <param name="options">服务选项；为 null 时使用 <see cref="ServiceOptions.Default"/>。</param>
    public void Register(object service, ServiceOptions? options)
    {
        ArgumentNullException.ThrowIfNull(service);
        var opts = options ?? ServiceOptions.Default;

        lock (_lock)
        {
            _entries.Add(new ServiceEntry { Instance = service, Options = opts });
        }

        // 若服务实现 IHttpServiceHandler 且指定 Route，注册到路由表
        if (service is IHttpServiceHandler handler && !string.IsNullOrWhiteSpace(opts.Route))
        {
            var normalizedRoute = NormalizeRoute(opts.Route!);
            _routeTable[normalizedRoute] = handler;
        }
    }

    /// <summary>
    /// 注销服务。
    /// 同时从路由表中移除其关联的路由条目（若存在）。
    /// </summary>
    /// <param name="service">要注销的服务实例。</param>
    public void Unregister(object service)
    {
        ArgumentNullException.ThrowIfNull(service);

        ServiceEntry? removed;
        lock (_lock)
        {
            var idx = _entries.FindIndex(e => ReferenceEquals(e.Instance, service));
            if (idx < 0)
            {
                return;
            }

            removed = _entries[idx];
            _entries.RemoveAt(idx);
        }

        // 从路由表中移除（若存在）
        if (removed.Options.Route is { } route && removed.Instance is IHttpServiceHandler)
        {
            var normalizedRoute = NormalizeRoute(route);
            _routeTable.TryRemove(normalizedRoute, out _);
        }
    }

    /// <summary>
    /// 获取指定类型的服务。
    /// </summary>
    /// <typeparam name="T">服务类型。</typeparam>
    /// <returns>匹配的服务实例，若不存在则返回 null。</returns>
    public T? GetService<T>() where T : class
    {
        lock (_lock)
        {
            return _entries.Select(e => e.Instance).OfType<T>().FirstOrDefault();
        }
    }

    /// <summary>
    /// 获取所有匹配类型的服务。
    /// </summary>
    /// <typeparam name="T">服务类型。</typeparam>
    /// <returns>匹配的服务实例集合。</returns>
    public IEnumerable<T> GetServices<T>() where T : class
    {
        lock (_lock)
        {
            return _entries.Select(e => e.Instance).OfType<T>().ToList();
        }
    }

    /// <summary>
    /// 获取指定服务实例关联的选项。若未注册则返回 null。
    /// </summary>
    /// <param name="service">服务实例。</param>
    /// <returns>关联的 <see cref="ServiceOptions"/>；未注册时返回 null。</returns>
    public ServiceOptions? GetOptions(object service)
    {
        ArgumentNullException.ThrowIfNull(service);
        lock (_lock)
        {
            return _entries.FirstOrDefault(e => ReferenceEquals(e.Instance, service))?.Options;
        }
    }

    /// <summary>
    /// 获取所有已注册的路由前缀及其对应的处理器（P1-6 新增）。
    /// </summary>
    /// <returns>路由前缀到处理器的只读字典；若无路由则返回空字典。</returns>
    public IReadOnlyDictionary<string, IHttpServiceHandler> GetServiceRoutes()
    {
        return _routeTable.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// 尝试按路径前缀匹配已注册的路由处理器（P1-6 新增）。
    /// </summary>
    /// <param name="path">请求路径（如 <c>/api/users</c>）。</param>
    /// <param name="route">匹配到的路由前缀；未匹配时为 null。</param>
    /// <param name="handler">匹配到的处理器；未匹配时为 null。</param>
    /// <returns>是否匹配到路由。</returns>
    /// <remarks>
    /// 匹配规则：
    /// <list type="bullet">
    /// <item>精确匹配：<c>path == route</c>（如 <c>/api</c> 匹配 <c>/api</c>）。</item>
    /// <item>前缀匹配：<c>path</c> 以 <c>route + "/"</c> 开头（如 <c>/api/users</c> 匹配 <c>/api</c>）。</item>
    /// <item>不匹配：<c>/apiv2</c> 不匹配 <c>/api</c>（避免前缀歧义）。</item>
    /// </list>
    /// 当多个路由都能匹配时，选择最长匹配（最具体）。
    /// </remarks>
    public bool TryMatchRoute(string path, out string? route, out IHttpServiceHandler? handler)
    {
        if (string.IsNullOrEmpty(path))
        {
            route = null;
            handler = null;
            return false;
        }

        string? bestRoute = null;
        IHttpServiceHandler? bestHandler = null;

        foreach (var kv in _routeTable)
        {
            var candidateRoute = kv.Key;
            if (MatchesRoute(path, candidateRoute))
            {
                if (bestRoute is null || candidateRoute.Length > bestRoute.Length)
                {
                    bestRoute = candidateRoute;
                    bestHandler = kv.Value;
                }
            }
        }

        route = bestRoute;
        handler = bestHandler;
        return bestRoute is not null;
    }

    /// <summary>
    /// 清空所有服务。
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
        _routeTable.Clear();
    }

    /// <summary>
    /// 将已注册的服务实例迁移到 <see cref="IServiceCollection"/>。
    /// 所有服务以单例生命周期注册，使用服务的运行时类型作为服务类型。
    /// </summary>
    /// <param name="services">目标服务集合。</param>
    public void CopyTo(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        lock (_lock)
        {
            foreach (var entry in _entries)
            {
                services.AddSingleton(entry.Instance.GetType(), entry.Instance);
            }
        }
    }

    /// <summary>
    /// 规范化路由前缀：确保以 <c>/</c> 开头，去除尾部 <c>/</c>。
    /// </summary>
    /// <param name="route">原始路由字符串。</param>
    /// <returns>规范化后的路由。</returns>
    private static string NormalizeRoute(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        var trimmed = route.Trim();
        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }
        // 去除尾部斜杠（保留根路由 "/" 特例）
        while (trimmed.Length > 1 && trimmed.EndsWith('/'))
        {
            trimmed = trimmed[..^1];
        }
        return trimmed;
    }

    /// <summary>
    /// 判断指定路径是否匹配给定路由前缀。
    /// </summary>
    private static bool MatchesRoute(string path, string route)
    {
        if (route.Length == 1 && route[0] == '/')
        {
            // 根路由匹配任意路径
            return true;
        }

        // 精确匹配
        if (string.Equals(path, route, StringComparison.Ordinal))
        {
            return true;
        }

        // 前缀匹配：path 以 "route/" 开头
        return path.Length > route.Length &&
               path[route.Length] == '/' &&
               path.StartsWith(route, StringComparison.Ordinal);
    }
}
