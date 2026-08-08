using Wails.Net.Plugins.Application;
using Wails.Net.Plugins.Window;
using Wails.Net.Plugins.Log;
using Wails.Net.Plugins.Path;
using Wails.Net.Plugins.AppInfo;
using Wails.Net.Plugins.OsInfo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Demo.DevRelease.Services;

// =====================================================================
//  Wails.Net DevRelease 演示 Demo
// ---------------------------------------------------------------------
//  本 Demo 专门演示 Wails.Net 项目的"前后端项目分布"与
//  "Debug / Release 模式"，对应 Tauri v2 / Wails v3 的运行模式。
//
//  ┌──────────────────┬─────────────────────────────────────────────┐
//  │ 模式              │ 行为                                         │
//  ├──────────────────┼─────────────────────────────────────────────┤
//  │ Debug（开发）     │ • WAILS_DEBUG=true 触发                      │
//  │                  │ • 自动打开 WebView2 DevTools                  │
//  │                  │ • 控制台输出 [DEBUG] 标记                    │
//  │                  │ • 日志级别 = Debug                           │
//  │                  │ • 窗口标题追加 "(Debug)"                    │
//  ├──────────────────┼─────────────────────────────────────────────┤
//  │ Release（发布）   │ • WAILS_DEBUG=false 或未设置                 │
//  │                  │ • 不打开 DevTools                            │
//  │                  │ • 日志级别 = Information                     │
//  │                  │ • 窗口标题无 Debug 标记                     │
//  └──────────────────┴─────────────────────────────────────────────┘
//
//  推荐命令（参照 Tauri v2 / Wails v3）：
//      wails dev     ← Debug 模式（开发与热重载）
//      wails build   ← Release 模式（生产构建）
//      F5            ← 通过 launchSettings.json 启动（VS / Rider）
// =====================================================================

// ---- 1. 模式检测 ----
// 优先级：WAILS_DEBUG 环境变量 > --debug 命令行参数 > DOTNET_ENVIRONMENT（统一由框架 DebugMode 提供）
var isDebugMode = DebugMode.IsEnabled(args);

Console.WriteLine("=========================================================");
Console.WriteLine(isDebugMode
    ? "[Wails.Net] ✓ Debug 模式（开发与热重载）"
    : "[Wails.Net] ✓ Release 模式（生产构建）");
Console.WriteLine($"[Wails.Net] 进程 PID：{Environment.ProcessId}");
Console.WriteLine($"[Wails.Net] .NET 运行时：{Environment.Version}");
Console.WriteLine($"[Wails.Net] 工作目录：{Environment.CurrentDirectory}");
Console.WriteLine("=========================================================");

// ---- 2. 创建桌面应用构建器 ----
var builder = DesktopApplicationBuilder.CreateBuilder(args);

builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net DevRelease Demo";
    options.SingleInstance = true;
    options.Window.Frameless = false;

    // 静态资源根路径。
    //  - Debug 时由 vite dev server 增量构建（dist/）
    //  - Release 时由 dotnet build 嵌入为资源
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// ---- 3. 日志级别（按模式区分） ----
builder.Logging.SetMinimumLevel(isDebugMode ? LogLevel.Debug : LogLevel.Information);
builder.Logging.AddFilter("Microsoft", isDebugMode ? LogLevel.Information : LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

// ---- 4. 注册绑定服务 ----
builder.Services.AddSingleton<DevReleaseService>();

// ---- 5. 注册内置插件 ----
builder.UsePlugin<WindowPlugin>();        // 窗口操作（min/max/close + openDevTools）
builder.UsePlugin<ApplicationPlugin>();   // 应用级操作
builder.UsePlugin<LogPlugin>();           // 日志记录
builder.UsePlugin<OsInfoPlugin>();        // 操作系统信息
builder.UsePlugin<AppInfoPlugin>();       // 应用信息
builder.UsePlugin<PathPlugin>();          // 路径操作

// ---- 6. 平台实现（自动检测） ----
builder.UseAutoPlatform();

// ---- 7. 构建应用 ----
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 从 DI 容器获取绑定服务并注册到 BindingManager
app.RegisterBindings<DevReleaseService>();

// ---- 8. 创建主窗口（按模式定制窗口标题与行为） ----
app.Options.OnAfterStart = () =>
{
    var mainWindow = app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = isDebugMode
            ? "Wails.Net DevRelease Demo (Debug) - Wails.Net"
            : "Wails.Net DevRelease Demo - Wails.Net",
        Width = 1100,
        Height = 750,
        MinWidth = 800,
        MinHeight = 600,
        Resizable = true,
        Maximisable = true,
        Minimisable = true,
        Fullscreen = false,
    });

    // Debug 模式：窗口创建后自动打开 DevTools
    if (isDebugMode)
    {
        Console.WriteLine("[Wails.Net] DevTools 即将打开...");
        // 延迟 500ms 等待 WebView2 初始化完成
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            try
            {
                mainWindow.OpenDevTools();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Wails.Net] DevTools 打开失败：{ex.Message}");
            }
        });
    }
};

// ---- 9. 注册退出钩子 ----
app.Options.OnShutdown = () =>
{
    Console.WriteLine("[Wails.Net] 应用退出");
};

// ---- 10. 运行应用 ----
await desktopApp.RunAsync();
