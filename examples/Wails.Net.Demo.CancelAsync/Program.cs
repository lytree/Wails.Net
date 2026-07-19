// Demo: Wails.Net.Demo.CancelAsync
// 目的：演示异步任务绑定与 CancellationToken 取消机制。
// 前端发起 LongRunningService.StartLongTask(durationSeconds) 调用后，
// 可通过 _wailsCancelCall 或服务端 CancelFromServer() 中断任务。
// 任务执行期间前端可轮询 GetProgress() 查询进度。
// 参考实现：tests/Wails.Net.Application.Tests/Transport/CancellablePromiseEndToEndTests.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Demo.CancelAsync.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - CancelAsync";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册长任务服务到 DI 容器
builder.Services.AddSingleton<LongRunningService>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定
app.RegisterBindings<LongRunningService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - CancelAsync",
        Width = 1000,
        Height = 700,
    });
};

await desktopApp.RunAsync();
