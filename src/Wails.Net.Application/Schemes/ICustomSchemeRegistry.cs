namespace Wails.Net.Application.Schemes;

/// <summary>
/// 自定义 URI scheme 注册表（对应 Tauri v2 的 asset protocol）。
/// <para>
/// 将 scheme（如 <c>myapp</c>）映射到具体的 <see cref="Wails.Net.AssetServer.AssetServer"/>，
/// 供平台 Webview 窗口在创建时查询并设置请求拦截，把 <c>myapp://...</c> 交给对应的资源管线处理。
/// </para>
/// <para>
/// 从 <see cref="Wails.Net.Application.Application"/> 抽出（RH-HOST-02）：<see cref="Wails.Net.Application.Application"/> 仅保留同名
/// 公开 facade 方法并委托到本接口，scheme 表的存储与并发由实现独占。
/// </para>
/// </summary>
public interface ICustomSchemeRegistry
{
    /// <summary>
    /// 注册（或覆盖）scheme 到资源服务器的映射。scheme 比较不区分大小写。
    /// </summary>
    /// <param name="scheme">URI scheme 名称（不含 <c>://</c>），非空。</param>
    /// <param name="assetServer">处理该 scheme 请求的资源服务器，非 null。</param>
    /// <exception cref="System.ArgumentException"><paramref name="scheme"/> 为 null、空串或仅空白。</exception>
    /// <exception cref="System.ArgumentNullException"><paramref name="assetServer"/> 为 null。</exception>
    void Register(string scheme, Wails.Net.AssetServer.AssetServer assetServer);

    /// <summary>
    /// 取消注册指定 scheme。
    /// </summary>
    /// <param name="scheme">要取消注册的 scheme 名称。</param>
    /// <returns>是否成功移除；scheme 为空或未注册时返回 false。</returns>
    bool Unregister(string scheme);

    /// <summary>
    /// 查询指定 scheme 是否已注册。
    /// </summary>
    /// <param name="scheme">scheme 名称。</param>
    /// <returns>已注册返回 true；scheme 为空或未注册返回 false。</returns>
    bool IsRegistered(string scheme);

    /// <summary>
    /// 尝试获取指定 scheme 关联的资源服务器。
    /// </summary>
    /// <param name="scheme">scheme 名称。</param>
    /// <param name="assetServer">查找到的资源服务器；未注册时为 null。</param>
    /// <returns>是否找到已注册的 scheme。</returns>
    bool TryGetAssetServer(string scheme, out Wails.Net.AssetServer.AssetServer? assetServer);

    /// <summary>
    /// 获取所有已注册 scheme 名称的快照（顺序未定义）。
    /// </summary>
    IReadOnlyList<string> RegisteredSchemes { get; }
}
