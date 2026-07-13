using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.Vue.Plugins;
using Wails.Net.Demo.Vue.Services;

// 创建桌面应用构建器（使用 Generic Host 模式）
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Vue Demo";
    options.SingleInstance = true;
    options.Window.Frameless = false;

    // 前端构建产物目录（npm run build 后生成）
    options.Assets.RootPath = "frontend/dist";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务到 DI 容器
builder.Services.AddSingleton<GreetingService>();
builder.Services.AddSingleton<TodoService>();

// 使用内置插件
builder.UsePlugin<LogPlugin>();
builder.UsePlugin<ClipboardPlugin>();
builder.UsePlugin<DialogPlugin>();
builder.UsePlugin<NotificationPlugin>();
builder.UsePlugin<OsInfoPlugin>();
builder.UsePlugin<StorePlugin>();
builder.UsePlugin<PathPlugin>();
builder.UsePlugin<AppInfoPlugin>();

// 使用自定义插件
builder.UsePlugin<MyCustomPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 注册 Windows 平台实现
builder.UsePlatform<WindowsPlatformApp>();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 设置 ApplicationOptions
app.Options.EnableDefaultContextMenu = true;
app.Options.DragAndDrop = true;

// 注册绑定服务到 Application（公共方法通过反射暴露给前端）
app.RegisterService(new GreetingService());
app.RegisterService(new TodoService());

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Vue Demo - vue-jsx-vapor 示例",
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
