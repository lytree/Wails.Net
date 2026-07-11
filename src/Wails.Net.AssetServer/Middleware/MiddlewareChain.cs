using System.Net;

namespace Wails.Net.AssetServer.Middleware;

/// <summary>
/// 中间件链，按注册顺序执行中间件。
/// 对应 Wails v3 Go 版本 assetserver 中间件链的编排逻辑，
/// 在请求到达最终资源处理器之前依次调用每个中间件。
/// 同时支持基于路径（<see cref="IMiddleware" />）和基于 HTTP 上下文（<see cref="IHttpMiddleware" />）的中间件。
/// </summary>
public class MiddlewareChain
{
    /// <summary>
    /// 已注册的基于路径的中间件列表，按注册顺序保存。
    /// </summary>
    private readonly List<IMiddleware> _middlewares = new();

    /// <summary>
    /// 已注册的基于 HTTP 上下文的中间件列表，按注册顺序保存。
    /// </summary>
    private readonly List<IHttpMiddleware> _httpMiddlewares = new();

    /// <summary>
    /// 添加基于路径的中间件到链末尾。
    /// </summary>
    /// <param name="middleware">要添加的中间件实例。</param>
    public void Use(IMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _middlewares.Add(middleware);
    }

    /// <summary>
    /// 添加基于 HTTP 上下文的中间件到链末尾。
    /// </summary>
    /// <param name="middleware">要添加的 HTTP 中间件实例。</param>
    public void Use(IHttpMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        _httpMiddlewares.Add(middleware);
    }

    /// <summary>
    /// 按顺序执行基于路径的中间件链，最终调用 <paramref name="finalHandler" /> 处理请求。
    /// </summary>
    /// <param name="path">请求的资源路径。</param>
    /// <param name="finalHandler">链末端的最终处理器，返回字节数组或 null。</param>
    /// <returns>最终由链中某一节点产生的资源内容；若均未处理则返回 null。</returns>
    public Task<byte[]?> ExecuteAsync(string path, Func<string, Task<byte[]?>> finalHandler)
    {
        ArgumentNullException.ThrowIfNull(finalHandler);

        Func<string, Task<byte[]?>> pipeline = finalHandler;

        // 倒序构建委托链，使最先注册的中间件最先执行。
        for (int i = _middlewares.Count - 1; i >= 0; i--)
        {
            var current = _middlewares[i];
            var next = pipeline;
            pipeline = p => current.ProcessAsync(p, next);
        }

        return pipeline(path);
    }

    /// <summary>
    /// 按顺序执行基于 HTTP 上下文的中间件链，最终调用 <paramref name="finalHandler" /> 处理请求。
    /// 若任一中间件返回 true 表示已完全处理请求，则后续中间件和最终处理器不再执行。
    /// </summary>
    /// <param name="context">HTTP 请求上下文。</param>
    /// <param name="finalHandler">链末端的最终处理器。</param>
    /// <returns>表示执行操作的异步任务；若中间件已处理请求则返回 true。</returns>
    public async Task<bool> ExecuteHttpAsync(HttpListenerContext context, Func<Task> finalHandler)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(finalHandler);

        Func<Task> pipeline = finalHandler;

        var handled = false;

        // 倒序构建委托链，使最先注册的中间件最先执行。
        for (int i = _httpMiddlewares.Count - 1; i >= 0; i--)
        {
            var current = _httpMiddlewares[i];
            var next = pipeline;
            pipeline = async () =>
            {
                var result = await current.ProcessAsync(context, next);
                if (result)
                {
                    handled = true;
                }
            };
        }

        await pipeline();
        return handled;
    }

    /// <summary>
    /// 获取已注册的基于路径的中间件数量。
    /// </summary>
    public int Count => _middlewares.Count;

    /// <summary>
    /// 获取已注册的基于 HTTP 上下文的中间件数量。
    /// </summary>
    public int HttpMiddlewareCount => _httpMiddlewares.Count;
}
