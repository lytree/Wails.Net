namespace Wails.Net.Application.Services;

/// <summary>
/// 服务路由暂存区（P1-6）。
/// <para>
/// 服务可能在 <see cref="Wails.Net.AssetServer.AssetServer"/> 注入之前就用
/// <see cref="ServiceOptions.Route"/> 注册；此时路由无法立即挂载，先暂存，
/// 待 <see cref="Wails.Net.Application.Application.AssetServer"/> 赋值时一次性应用。
/// </para>
/// <para>
/// 从 <see cref="Wails.Net.Application.Application"/> 抽出（RH-HOST-02）：Application 只保留
/// <c>AssetServer</c> setter 中的一行 <see cref="FlushTo"/> 调用。
/// </para>
/// </summary>
public sealed class ServiceRouteRegistry
{
    private readonly List<(string Route, Wails.Net.AssetServer.IHttpServiceHandler Handler)> _pending = [];

    /// <summary>
    /// 暂存一条待挂载的服务路由。
    /// </summary>
    /// <param name="route">路由前缀（非空）。</param>
    /// <param name="handler">处理该路由前缀请求的处理器。</param>
    public void Add(string route, Wails.Net.AssetServer.IHttpServiceHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(handler);

        _pending.Add((route, handler));
    }

    /// <summary>
    /// 把暂存的路由全部挂载到指定资源服务器，并清空暂存区。
    /// </summary>
    /// <param name="assetServer">目标资源服务器；为 null 时直接返回且不清空。</param>
    /// <returns>本次实际挂载的路由数量。</returns>
    public int FlushTo(Wails.Net.AssetServer.AssetServer? assetServer)
    {
        if (assetServer is null || _pending.Count == 0)
        {
            return 0;
        }

        foreach (var (route, handler) in _pending)
        {
            assetServer.MountServiceRoute(route, handler);
        }

        var count = _pending.Count;
        _pending.Clear();
        return count;
    }

    /// <summary>
    /// 当前暂存但尚未挂载的路由数量。
    /// </summary>
    public int PendingCount => _pending.Count;
}
