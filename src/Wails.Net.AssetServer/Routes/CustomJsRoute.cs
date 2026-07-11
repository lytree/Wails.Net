using System.Text;
using Wails.Net.AssetServer.Middleware;

namespace Wails.Net.AssetServer.Routes;

/// <summary>
/// 处理 <c>/wails/custom.js</c> 路由的中间件。
/// 对应 Wails v3 Go 版本中为前端提供用户自定义 JS 代码的路由处理逻辑。
/// </summary>
public class CustomJsRoute : IMiddleware
{
    /// <summary>
    /// 路由匹配的路径常量。
    /// </summary>
    public const string RoutePath = "/wails/custom.js";

    /// <summary>
    /// 异步处理请求：当路径匹配 <c>/wails/custom.js</c> 时返回自定义 JS 代码，
    /// 否则将请求转发给链中下一个处理器。
    /// </summary>
    /// <param name="path">请求的资源路径。</param>
    /// <param name="next">链中下一个处理器的委托。</param>
    /// <returns>自定义 JS 代码字节组；若路径不匹配则返回下一处理器的结果。</returns>
    public Task<byte[]?> ProcessAsync(string path, Func<string, Task<byte[]?>> next)
    {
        if (string.Equals(path, RoutePath, StringComparison.OrdinalIgnoreCase))
        {
            // 占位实现：实际自定义代码将在后续阶段注入。
            var content = "// Wails.NET custom.js placeholder\n";
            return Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(content));
        }

        return next(path);
    }
}
