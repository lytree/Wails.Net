// Demo: Wails.Net.Demo.Frameless
// 目的：演示无边框窗口（Frameless）与自定义标题栏的实现。
// 通过 options.Window.Frameless = true 启用无边框模式，
// 前端使用 CSS 变量 --wails-draggable: drag 标记可拖拽区域，
// 自定义最小化/最大化/关闭按钮通过 window.minimize / window.maximize / window.close 命令调用。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.Frameless.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项：启用无边框窗口
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Frameless";
    options.Window.Frameless = true;
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务
builder.Services.AddSingleton<WindowStateService>();

// 启用内置 WindowPlugin（提供 window.minimize / window.maximize / window.close 等命令）
builder.UsePlugin<WindowPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定
app.RegisterBindings<WindowStateService>();

// 应用启动后创建主窗口（800x600 无边框）
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - Frameless",
        Width = 800,
        Height = 600,
        Frameless = true,
    });
};

await desktopApp.RunAsync();
