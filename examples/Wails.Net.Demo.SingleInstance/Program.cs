// Demo: Wails.Net.Demo.SingleInstance
// 目的：演示单实例锁（Single Instance）能力。
// 通过 ApplicationOptions.SingleInstance 启用单实例模式，
// 平台层在启动时调用 AcquireSingleInstanceLock(uniqueId)：
// - 首实例获取锁成功，正常运行；
// - 后续实例获取锁失败，调用 NotifySingleInstance(args) 通知首实例后退出。
// 首实例通过 Application.OnSecondInstanceLaunch 回调接收新实例的命令行参数，
// 同时框架会广播 KnownEvents.SecondInstanceLaunched 事件到前端。
// SingleInstanceLogService 通过 [Binding] 方法暴露启动历史查询、参数详情、清空等能力。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.SingleInstance.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - SingleInstance";
    options.SingleInstance = true; // 启用单实例模式
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务
builder.Services.AddSingleton<SingleInstanceLogService>();

// 启用内置 WindowPlugin（用于在收到二次启动时聚焦主窗口）
builder.UsePlugin<WindowPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定
var logService = app.RegisterBindings<SingleInstanceLogService>();

// 注册二次启动回调：
// 当有新进程尝试启动同一应用时，首实例会通过此回调收到新实例的命令行参数。
// 框架同时会广播 KnownEvents.SecondInstanceLaunched 事件到前端。
app.OnSecondInstanceLaunch(args =>
{
    var argList = args?.Length > 0 ? string.Join(" ", args) : "（无参数）";
    logService.RecordLaunch(args ?? Array.Empty<string>());
    // 聚焦主窗口提示用户
    var window = app.GetWindowByName("main");
    window?.Show();
    window?.Focus();
    Console.WriteLine($"[SingleInstance] 收到二次启动，参数：{argList}");
});

// 应用启动后创建主窗口，并记录首实例启动
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - SingleInstance",
        Width = 1000,
        Height = 700,
    });

    logService.RecordLaunch(Environment.GetCommandLineArgs());
};

await desktopApp.RunAsync();
