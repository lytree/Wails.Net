namespace Wails.Net.Application.Transport;

/// <summary>
/// 传输层接口，对应 Wails v3 中的 Transport 接口。
/// </summary>
public interface ITransport
{
    /// <summary>
    /// 返回前端 JS 客户端代码。
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
/// </summary>
public interface IWailsEventListener
{
    /// <summary>
    /// 通知事件。
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据，可为 null。</param>
    void NotifyEvent(string eventName, object? data);
}

/// <summary>
/// HTTP 处理器接口，用于基于 HTTP 的传输层。
/// </summary>
public interface ITransportHttpHandler
{
    /// <summary>
    /// 处理 HTTP 请求（使用 object 暂代 HttpRequest/HttpResponse 避免依赖 ASP.NET）。
    /// </summary>
    /// <param name="request">请求对象。</param>
    /// <param name="response">响应对象。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    Task HandleRequestAsync(object request, object response);
}

/// <summary>
/// 资源服务器传输接口，用于提供资源服务的传输层。
/// </summary>
public interface IAssetServerTransport
{
    /// <summary>
    /// 获取资源。
    /// </summary>
    /// <param name="path">资源路径。</param>
    /// <returns>资源字节数据，若不存在则返回 null。</returns>
    byte[]? GetAsset(string path);

    /// <summary>
    /// 检查资源是否存在。
    /// </summary>
    /// <param name="path">资源路径。</param>
    /// <returns>如果资源存在则返回 true，否则返回 false。</returns>
    bool HasAsset(string path);
}
