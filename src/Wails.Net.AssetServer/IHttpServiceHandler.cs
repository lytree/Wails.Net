using System.Net;

namespace Wails.Net.AssetServer;

/// <summary>
/// HTTP 服务处理器接口，对应 Wails v3 Go 版本中的 <c>http.Handler</c> 接口。
/// <para>
/// 服务实例实现此接口并通过 <see cref="Application.Services.ServiceOptions.Route"/> 指定挂载前缀后，
/// 将被 <see cref="AssetServer"/> 在该前缀下挂载并转发匹配的 HTTP 请求。
/// 对应 Wails v3 Go 版本 <c>services.go</c> 中的注释：
/// <c>"If the service instance implements [http.Handler], it will be mounted
/// on the internal asset server at the prefix specified by Route."</c>
/// </para>
/// <para>
/// 典型用法：
/// <code>
/// public class MyApiController : IHttpServiceHandler
/// {
///     public async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
///     {
///         var path = ctx.Request.Url?.AbsolutePath ?? "/";
///         ctx.Response.ContentType = "application/json";
///         var body = Encoding.UTF8.GetBytes($@"{{""path"":""{path}""}}");
///         ctx.Response.ContentLength64 = body.Length;
///         await ctx.Response.OutputStream.WriteAsync(body, ct);
///         ctx.Response.Close();
///     }
/// }
///
/// // 注册时指定 Route 前缀（Application 层）：
/// app.RegisterService(new MyApiController(), new ServiceOptions { Route = "/api" });
/// </code>
/// </para>
/// </summary>
public interface IHttpServiceHandler
{
    /// <summary>
    /// 异步处理 HTTP 请求。
    /// <para>
    /// 实现者负责写入响应并调用 <see cref="HttpListenerResponse.Close"/>（或等效的关闭方法）。
    /// 上下文中包含完整的请求 URL，实现者需自行解析相对于挂载前缀的路径。
    /// </para>
    /// </summary>
    /// <param name="context">HTTP 请求上下文，包含请求和响应对象。</param>
    /// <param name="cancellationToken">取消令牌，在应用关闭时触发。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken);
}
