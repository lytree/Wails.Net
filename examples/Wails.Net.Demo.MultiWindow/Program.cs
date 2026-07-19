// Demo: Wails.Net.Demo.MultiWindow
// 目的：演示多窗口管理能力。
// 启用 WindowPlugin（单窗口操作）与 WindowsPlugin（窗口列表查询），
// WindowManagerService 通过 Application.CreateWebviewWindow 创建子窗口，
// 维护窗口标题等元数据，并提供聚焦、关闭、列表查询等绑定方法。
// 窗口事件通过 Application.Events 广播到所有窗口，前端通过 wails.events.on 订阅。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.MultiWindow.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - MultiWindow";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 启用内置 WindowPlugin（单窗口操作：window.focus / window.close 等）
builder.UsePlugin<WindowPlugin>();

// 启用内置 WindowsPlugin（窗口列表查询：windows.getAll / windows.getById 等）
builder.UsePlugin<WindowsPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定服务（注入 Application 实例）
var windowManagerService = new WindowManagerService(app);
app.RegisterService(windowManagerService);

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - MultiWindow - 主窗口",
        Width = 1000,
        Height = 700,
    });
};

await desktopApp.RunAsync();
