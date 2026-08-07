using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.React.Plugins;
using Wails.Net.Demo.React.Services;

// 通过 DebugMode 统一判定当前模式（优先级：WAILS_DEBUG 环境变量 > --debug 参数 > .NET 环境变量）。
var isDebugMode = DebugMode.IsEnabled(args);

// 创建桌面应用构建器（使用 Generic Host 模式）
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net React Demo";
    options.SingleInstance = true;
    options.Window.Frameless = false;

    // 前端构建产物目录（pnpm build 后生成，由 Wails.Net.Sdk 的 WailsNetBuildFrontend 自动构建）
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

// 配置日志级别（Debug 模式输出更详细日志，Release 模式仅 Information 以上）
builder.Logging.SetMinimumLevel(isDebugMode ? LogLevel.Debug : LogLevel.Information);
builder.Logging.AddFilter("Microsoft", isDebugMode ? LogLevel.Information : LogLevel.Warning);

// 使用平台工厂自动检测并注册平台实现（Windows/Linux/Android）
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 设置 ApplicationOptions
app.Options.EnableDefaultContextMenu = true;
app.Options.DragAndDrop = true;

// 从 DI 容器获取绑定服务并注册到 BindingManager（对应 ASP.NET Core 风格：DI 是单一注册点）
app.RegisterBindings<GreetingService>();
app.RegisterBindings<TodoService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    var mainWindow = app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = isDebugMode
            ? "Wails.Net React Demo (Debug) - TSX 示例"
            : "Wails.Net React Demo - TSX 示例",
        Width = 1200,
        Height = 800,
        MinWidth = 800,
        MinHeight = 600,
        Resizable = true,
        Maximisable = true,
        Minimisable = true,
        Fullscreen = false,
    });

    // Debug 模式：窗口创建后自动打开 DevTools（延迟等待 WebView2 初始化完成）
    if (isDebugMode)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            try
            {
                mainWindow.OpenDevTools();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[React Demo] DevTools 打开失败：{ex.Message}");
            }
        });
    }
};

// 构建并运行应用
await desktopApp.RunAsync();
