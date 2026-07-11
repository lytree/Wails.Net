using System.Net;

namespace Wails.Net.AssetServer.Middleware;

/// <summary>
/// 资源服务器中间件接口。
/// 对应 Wails v3 Go 版本 assetserver 中间件机制，
/// 用于在请求到达最终资源处理器之前对路径或内容进行拦截与转换。
/// </summary>
public interface IMiddleware
{
    /// <summary>
    /// 异步处理指定路径的请求。
    /// 中间件可选择自行处理请求，或调用 <paramref name="next" /> 将请求传递给链中下一节点。
    /// </summary>
    /// <param name="path">请求的资源路径。</param>
    /// <param name="next">链中下一个处理器的委托，返回字节数组或 null。</param>
    /// <returns>处理后的资源内容；若未处理则返回 null。</returns>
    Task<byte[]?> ProcessAsync(string path, Func<string, Task<byte[]?>> next);
}

/// <summary>
/// 基于 HTTP 上下文的资源服务器中间件接口。
/// 提供完整的 HTTP 请求/响应上下文访问能力，支持读写响应头、状态码及 Range 请求等。
/// 对应 Wails v3 Go 版本中 <c>http.Handler</c> 风格的中间件。
/// </summary>
public interface IHttpMiddleware
{
    /// <summary>
    /// 异步处理 HTTP 请求。
    /// 中间件可选择自行写入响应并返回，或调用 <paramref name="next" /> 将请求传递给链中下一节点。
    /// 返回 true 表示已完全处理请求（后续中间件和最终处理器不再执行）；
    /// 返回 false 表示交由链继续处理。
    /// </summary>
    /// <param name="context">HTTP 请求上下文，包含请求和响应对象。</param>
    /// <param name="next">链中下一个处理器的委托。</param>
    /// <returns>是否已完全处理请求。</returns>
    Task<bool> ProcessAsync(HttpListenerContext context, Func<Task> next);
}
