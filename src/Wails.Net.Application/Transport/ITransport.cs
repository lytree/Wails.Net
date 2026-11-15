namespace Wails.Net.Application.Transport;

/// <summary>
/// 传输层接口，对应 Wails v3 中的 Transport 接口。
/// 传输层负责从前端接收运行时调用请求、通过 MessageProcessor 处理、并将响应返回前端。
/// </summary>
public interface ITransport
{
    /// <summary>
    /// 返回前端 JS 客户端代码。
    /// 对应 Wails v3 Go 版本 Transport 接口的 JSClient() 方法。
    /// </summary>
    /// <returns>JS 客户端代码字符串。</returns>
    string JSClient();

    /// <summary>
    /// 启动传输。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动操作的异步任务。</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 停止传输。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示停止操作的异步任务。</returns>
    Task StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 事件监听器接口，用于监听事件的传输层。
/// 实现此接口的传输层会被 EventProcessor 调用以将事件广播到前端。
/// 对应 Wails v3 Go 版本中 EventProcessor 的 wailsEventListener 字段类型。
/// </summary>
public interface IWailsEventListener
{
    /// <summary>
    /// 通知事件，将事件广播到连接的前端客户端。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据，可为 null。</param>
    /// <param name="senderWindowId">事件来源窗口 ID，可为 null（应用级事件）。
    /// 对应 Wails v3 Go 版本 CustomEvent.Sender 字段语义：标识事件来源窗口，
    /// 传输层应将其包含在前端载荷中使前端可识别事件发起方。
    /// 默认 null 保持与既有调用方的兼容。</param>
    /// <param name="targetWindowId">定向目标窗口 ID（OPT-EVENT-002）。
    /// 非 null 时事件仅派发到该窗口；null 表示广播到所有窗口。
    /// 默认 null 保持与既有调用方的兼容。</param>
    void NotifyEvent(string eventName, object? data, uint? senderWindowId = null, uint? targetWindowId = null);
}

/// <summary>
/// 资源服务器传输接口，用于提供资源服务的传输层。
/// 对应 Wails v3 Go 版本中的 AssetServerTransport 接口。
/// 实现此接口的传输层可服务于浏览器场景，将 HTML、CSS、JS 等静态资源
/// 与 IPC 传输端点共同暴露在 HTTP 服务器上。
/// </summary>
public interface IAssetServerTransport
{
    /// <summary>
    /// 将资源服务器绑定到当前传输层。
    /// 传输层在收到资源请求时应将请求转发给 AssetServer 处理。
    /// 此方法在 StartAsync 完成后调用。
    /// </summary>
    /// <param name="assetServer">Wails 内部资源服务器实例。</param>
    void ServeAssets(Wails.Net.AssetServer.AssetServer assetServer);
}
