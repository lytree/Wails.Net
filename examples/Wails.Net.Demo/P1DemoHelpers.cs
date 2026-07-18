using System.Net;
using System.Text;
using Wails.Net.Application.Services.Updater;
using Wails.Net.AssetServer;

namespace Wails.Net.Demo;

/// <summary>
/// 模拟更新提供者，用于 Demo 演示（P1-8 多 Provider Updater）。
/// 实际应用中应替换为 HttpUpdateProvider / GitHubUpdateProvider / GitLabUpdateProvider。
/// </summary>
public sealed class MockUpdateProvider : IUpdateProvider
{
    /// <summary>Provider 名称。</summary>
    public string Name => "mock";

    /// <summary>
    /// 返回一个模拟的更新清单（当前版本 1.0.0，无新版本）。
    /// </summary>
    public Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var manifest = new UpdateManifest
        {
            Version = "1.0.0",  // 与 CurrentVersion 相同，触发 NoUpdate 事件
            ReleaseNotes = "（Mock Provider）当前已是最新版本",
            DownloadURL = string.Empty,
        };
        return Task.FromResult<UpdateManifest?>(manifest);
    }
}

/// <summary>
/// 健康检查 HTTP 处理器（P1-6 Service Route 挂载）。
/// 挂载到 /api/health，返回应用健康状态 JSON。
/// </summary>
public sealed class DemoHealthHandler : IHttpServiceHandler
{
    /// <summary>
    /// 处理健康检查请求，返回 JSON 格式的状态信息。
    /// </summary>
    public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var status = """
            {
                "status": "healthy",
                "service": "Wails.Net.Demo",
                "timestamp": "DEMO_BUILD_TIME"
            }
            """;
        var bytes = Encoding.UTF8.GetBytes(status);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.StatusCode = 200;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }
}

/// <summary>
/// 版本信息 HTTP 处理器（P1-6 Service Route 挂载）。
/// 挂载到 /api/version，返回应用版本信息 JSON。
/// </summary>
public sealed class DemoVersionHandler : IHttpServiceHandler
{
    /// <summary>
    /// 处理版本查询请求，返回 JSON 格式的版本信息。
    /// </summary>
    public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var version = """
            {
                "name": "Wails.Net Demo",
                "version": "1.0.0",
                "framework": "Wails.Net",
                "runtime": ".NET 10"
            }
            """;
        var bytes = Encoding.UTF8.GetBytes(version);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.StatusCode = 200;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }
}
