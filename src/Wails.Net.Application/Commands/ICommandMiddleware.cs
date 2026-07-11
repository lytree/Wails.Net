namespace Wails.Net.Application.Commands;

/// <summary>
/// 命令中间件接口，用于在命令执行前后插入横切逻辑。
/// 对应 ASP.NET Core 的中间件管道模式。
/// 中间件可用于日志记录、审计、限流、指标收集等场景。
/// </summary>
public interface ICommandMiddleware
{
    /// <summary>
    /// 处理命令调用请求。
    /// 中间件可在调用 <paramref name="next"/> 前后插入自定义逻辑，
    /// 也可短路管道（不调用 next）直接返回响应。
    /// </summary>
    /// <param name="context">命令上下文。</param>
    /// <param name="request">调用请求。</param>
    /// <param name="next">管道中下一个委托，调用以继续执行命令。</param>
    /// <returns>调用响应。</returns>
    Task<InvokeResponse> InvokeAsync(ICommandContext context, InvokeRequest request, Func<Task<InvokeResponse>> next);
}
