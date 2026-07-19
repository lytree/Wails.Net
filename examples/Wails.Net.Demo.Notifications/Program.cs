// Demo: Wails.Net.Demo.Notifications
// 目的：演示内置 NotificationPlugin 与框架 NotificationService 的系统通知能力。
// 通过自定义 NotificationService 绑定方法提供立即发送、延迟发送与历史记录功能。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.Notifications.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Notifications";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册 Demo 自定义通知服务（与框架 NotificationService 区分，命名空间不同）
builder.Services.AddSingleton<NotificationService>();

// 启用内置 NotificationPlugin（提供 notification.show 等命令，便于前端直接调用）
builder.UsePlugin<NotificationPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定（[Binding] 标记的方法会被源生成器收集）
app.RegisterBindings<NotificationService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - Notifications",
        Width = 1000,
        Height = 700,
    });
};

await desktopApp.RunAsync();
