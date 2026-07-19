// Demo: Wails.Net.Demo.Server
// 目的：演示 Wails.Net 的 Server 模式（无 GUI 后台服务）。
// 使用 ServerPlatformApp 替代 WindowsPlatformApp/LinuxPlatformApp，
// 不创建任何 GUI 窗口，仅运行后台任务并通过 AssetServer 接收 HTTP 请求。
// 典型场景：容器化部署、Headless 服务、CI 测试环境。
//
// 运行方式：
//   dotnet run --project examples\Wails.Net.Demo.Server\Wails.Net.Demo.Server.csproj
//
// 测试 HTTP 端点（启动后另开终端）：
//   curl http://localhost:0/api/status        # 通过 Service Route 查询状态
//   curl http://localhost:0/index.html        # 静态页面

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Services;
using Wails.Net.AssetServer;
using Wails.Net.Demo.Server.Services;

// 创建桌面应用构建器（Server 模式仍使用 DesktopApplicationBuilder，
// 仅在平台层用 ServerPlatformApp 替代 GUI 平台实现）
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Server";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册后台任务服务到 DI 容器
builder.Services.AddSingleton<BackgroundTaskService>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 显式使用 ServerPlatformApp（无 GUI 桩实现），覆盖 UseAutoPlatform 的自动检测。
// 对应 AGENTS.md §6.1 Server 模式降级：用于容器化部署和测试。
builder.UsePlatform<ServerPlatformApp>();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册后台任务服务的绑定方法，并取得从 DI 容器解析的实例引用，
// 供后续 OnAfterStart 回调与 HTTP 处理器共享同一单例。
var bgService = app.RegisterBindings<BackgroundTaskService>();

// P1-6：Service Route 挂载 — 提供 HTTP API 端点
// /api/status 返回当前运行状态 JSON
app.RegisterService(new ServerStatusHandler(bgService), new ServiceOptions { Route = "/api/status" });

// 应用启动后：不创建 GUI 窗口，仅启动后台任务
app.Options.OnAfterStart = () =>
{
    bgService.StartProcessing();
    Console.WriteLine("[Server Demo] 后台任务已启动，按 Ctrl+C 退出。");
    Console.WriteLine("[Server Demo] HTTP 端点：GET /api/status  GET /index.html");
};

// 运行（ServerPlatformApp.Run 将阻塞直到 SignalShutdown）
await desktopApp.RunAsync();

/// <summary>
/// 服务状态 HTTP 处理器，挂载到 /api/status 路由。
/// 返回 JSON 格式的运行状态与已处理任务数。
/// </summary>
internal sealed class ServerStatusHandler : IHttpServiceHandler
{
    /// <summary>后台任务服务引用。</summary>
    private readonly BackgroundTaskService _bg;

    /// <summary>
    /// 构造处理器实例。
    /// </summary>
    /// <param name="bg">后台任务服务。</param>
    public ServerStatusHandler(BackgroundTaskService bg)
    {
        _bg = bg;
    }

    /// <summary>
    /// 处理 HTTP 请求，返回 JSON 状态信息。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var payload = new
        {
            status = _bg.GetStatus(),
            processedCount = _bg.GetProcessedCount(),
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.StatusCode = 200;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }
}
