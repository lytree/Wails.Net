using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.Plugins;
using Wails.Net.Demo.Services;

// 创建桌面应用构建器（使用 Generic Host 模式）
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项（DesktopHostOptions 仅暴露部分基础选项，
// 其余选项在 Build 后通过 Application.Options 设置）
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo";
    options.SingleInstance = true;
    options.Window.Frameless = false;

    // 配置静态资源根路径。设置后应用将自动创建 FileAssetServer，
    // 并通过 http://wails.localhost/ (Windows) / wails://localhost/ (Linux)
    // 提供静态资源服务，无需使用 file:// 协议，避免权限问题。
    // frontend 目录已通过 csproj 配置复制到输出目录。
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务到 DI 容器
// 这些服务的公共方法将自动暴露给前端 JavaScript
builder.Services.AddSingleton<GreetingService>();
builder.Services.AddSingleton<TodoService>();

// 使用内置插件（每个插件提供一组前端可调用的命令）
builder.UsePlugin<LogPlugin>();             // 日志记录
builder.UsePlugin<ClipboardPlugin>();        // 剪贴板操作
builder.UsePlugin<DialogPlugin>();           // 原生对话框
builder.UsePlugin<NotificationPlugin>();     // 系统通知
builder.UsePlugin<OsInfoPlugin>();           // 操作系统信息
builder.UsePlugin<StorePlugin>();            // 键值存储
builder.UsePlugin<PathPlugin>();             // 路径操作
builder.UsePlugin<AppInfoPlugin>();           // 应用信息

// 使用自定义插件
builder.UsePlugin<MyCustomPlugin>();          // 自定义计数器插件

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 注册 Windows 平台实现
builder.UsePlatform<WindowsPlatformApp>();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 设置 ApplicationOptions 中 DesktopHostOptions 未覆盖的选项
app.Options.EnableDefaultContextMenu = true;
app.Options.DragAndDrop = true;

// 注册绑定服务到 Application（公共方法通过反射暴露给前端）
app.RegisterService(new GreetingService());
app.RegisterService(new TodoService());

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    // 未设置 URL 或 HTML 时，窗口将自动导航到 http://wails.localhost/ (Windows)
    // 或 wails://localhost/ (Linux)，由 AssetServer 提供静态资源服务。
    // 无需构建 file:// URL，避免权限问题。
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - 桌面应用示例",
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
