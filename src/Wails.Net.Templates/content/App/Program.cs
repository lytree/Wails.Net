using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;

using Company.AppName.Services;

// 创建桌面应用构建器（使用 Generic Host 模式）
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Company.AppName";
    options.SingleInstance = true;
    options.Window.Frameless = false;

    // 配置静态资源根路径。
    // 设置后应用通过 http://wails.localhost/ (Windows) / wails://localhost/ (Linux)
    // 提供静态资源服务，无需使用 file:// 协议。
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务到 DI 容器
// 这些服务的公共方法将自动暴露给前端 JavaScript
builder.Services.AddSingleton<GreetingService>();

// 使用内置插件（每个插件提供一组前端可调用的命令）
builder.UsePlugin<WindowPlugin>();
builder.UsePlugin<ApplicationPlugin>();
builder.UsePlugin<LogPlugin>();
builder.UsePlugin<OsInfoPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 注册 Windows 平台实现
builder.UsePlatform<WindowsPlatformApp>();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 从 DI 容器获取绑定服务并注册到 BindingManager。
// 对应 ASP.NET Core 风格：DI 是单一注册点，避免双重实例。
app.RegisterBindings<GreetingService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Company.AppName - Wails.Net 桌面应用",
        Width = 1200,
        Height = 800,
        MinWidth = 800,
        MinHeight = 600,
        Resizable = true,
        Maximisable = true,
        Minimisable = true,
        Fullscreen = false,
    });
};

// 构建并运行应用
await desktopApp.RunAsync();
