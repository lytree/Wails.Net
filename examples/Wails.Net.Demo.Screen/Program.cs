// Demo: Wails.Net.Demo.Screen
// 目的：演示屏幕信息查询能力。
// 通过 ScreenPlugin 注册 screen.getPrimary / screen.getAll 命令，
// 前端调用命令获取主屏与所有屏幕的尺寸、坐标、缩放比例、是否主屏等信息。
// ScreenLogService 记录每次查询的日志，便于审计。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.Screen.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Screen";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务
builder.Services.AddSingleton<ScreenLogService>();

// 启用内置 ScreenPlugin（提供 screen.getPrimary / screen.getAll 命令）
builder.UsePlugin<ScreenPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定
app.RegisterBindings<ScreenLogService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - Screen",
        Width = 1000,
        Height = 700,
    });
};

await desktopApp.RunAsync();
