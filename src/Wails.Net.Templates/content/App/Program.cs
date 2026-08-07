using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;

using Company.AppName.Services;

// =====================================================================
//  Wails.Net 模板项目入口
// ---------------------------------------------------------------------
//  调试 / 发布模式（参照 Tauri v2 / Wails v3 的项目分布与运行模式）：
//
//  ┌──────────────────┬────────────────────────────────────────────┐
//  │ 模式              │ 行为                                       │
//  ├──────────────────┼────────────────────────────────────────────┤
//  │ Debug（开发）     │ • 启动前端 dev server（vite dev）          │
//  │                  │ • dotnet watch 自动重启                     │
//  │                  │ • WebView2 自动打开 DevTools                 │
//  │                  │ • 前端资源由 FileAssetServer 提供（dist/） │
//  ├──────────────────┼────────────────────────────────────────────┤
//  │ Release（发布）   │ • 前端构建（pnpm build → dist/）          │
//  │                  │ • dotnet build -c Release                  │
//  │                  │ • 前端资源嵌入为 .NET 资源                 │
//  │                  │ • 性能优化 / 禁用 DevTools                 │
//  └──────────────────┴────────────────────────────────────────────┘
//
//  推荐命令：
//      wails dev      ← Debug 模式（参见 wails.json 中的钩子）
//      wails build    ← Release 模式
//      dotnet run     ← 直接启动（按当前 Configuration 决定）
// =====================================================================

// 通过 DebugMode 统一判定当前模式（优先级：WAILS_DEBUG 环境变量 > --debug 参数 > .NET 环境变量）：
//   - wails dev 会自动设置 WAILS_DEBUG=true
//   - 普通 dotnet run 默认为 false
var isDebugMode = DebugMode.IsEnabled(args);

// 创建桌面应用构建器（使用 Generic Host 模式）
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Company.AppName";
    options.SingleInstance = true;
    options.Window.Frameless = false;

    // ---------- 静态资源配置 ----------
    // 设置后应用将自动创建 AssetServer 并通过
    //   http://wails.localhost/  (Windows)
    //   wails://localhost/      (Linux)
    // 提供静态资源服务，避免 file:// 协议权限问题。
    //
    // 约定：
    //   - Debug 模式：RootPath 指向 frontend/dist（已被 vite 增量构建）
    //   - Release 模式：同样指向 frontend/dist（构建后随 .NET 一起分发）
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;

    // 可选：开发服务器 URL（wails dev 使用，未来可启用代理 AssetServer）
    // 当前实现：Wails.Net 仍由本地 AssetServer 提供资源，vite dev 进程并行运行以提供 HMR
    // 完整 Tauri 风格的 dev server 代理将在后续版本提供。
    if (isDebugMode &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAILS_DEV_SERVER_URL")))
    {
        options.DevServerUrl = Environment.GetEnvironmentVariable("WAILS_DEV_SERVER_URL");
    }
});

// ---------- 日志配置 ----------
// Debug 模式输出更详细日志；Release 模式仅 Warning 以上
builder.Logging.SetMinimumLevel(isDebugMode ? LogLevel.Debug : LogLevel.Information);
builder.Logging.AddFilter("Microsoft", isDebugMode ? LogLevel.Information : LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

// ---------- 绑定服务 ----------
// 这些服务的公共方法将自动通过源代码生成器暴露给前端 JavaScript
builder.Services.AddSingleton<GreetingService>();

// ---------- 内置插件 ----------
// 每个插件提供一组前端可调用的命令（wails.window.* / wails.app.* 等）
builder.UsePlugin<WindowPlugin>();        // 窗口操作（min/max/close + openDevTools）
builder.UsePlugin<ApplicationPlugin>();   // 应用级操作（quit / getInfo）
builder.UsePlugin<LogPlugin>();           // 日志记录
builder.UsePlugin<OsInfoPlugin>();        // 操作系统信息

// ---------- 平台实现 ----------
// 显式注册平台实现，确保 [ModuleInitializer] 触发委托注册。
// 生产代码严禁使用反射（参见 AGENTS.md §3.4）。
if (OperatingSystem.IsWindows())
{
    builder.UsePlatform<Wails.Net.Application.Platform.WindowsPlatformApp>();
}
else if (OperatingSystem.IsLinux())
{
    builder.UsePlatform<Wails.Net.Application.Platform.LinuxPlatformApp>();
}
else if (OperatingSystem.IsAndroid())
{
    builder.UsePlatform<Wails.Net.Application.Platform.AndroidPlatformApp>();
}
else
{
    // 未知平台：降级到 Server 模式（无 GUI，可用于 CI / 容器）
    builder.UseAutoPlatform();
}

// ---------- 构建应用 ----------
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 从 DI 容器获取绑定服务并注册到 BindingManager。
// 对应 ASP.NET Core 风格：DI 是单一注册点，避免双重实例。
app.RegisterBindings<GreetingService>();

// ---------- Debug 模式额外行为 ----------
// 在 Debug 模式下：
//   1. 注册 OnAfterStart 钩子：自动打开 WebView2 DevTools
//   2. 控制台输出当前模式提示
if (isDebugMode)
{
    Console.WriteLine("[Wails.Net] Debug 模式已启用");
    Console.WriteLine("[Wails.Net] DevTools 将在窗口创建后自动打开");
    Console.WriteLine("[Wails.Net] 提示：HMR 由并行运行的 vite dev server 提供");

    app.Options.OnAfterStart = () =>
    {
        var mainWindow = app.GetWindowByName("main");
        if (mainWindow is not null)
        {
            // 自动打开 WebView2 DevTools（开发体验对齐 Wails v3 / Tauri）
            mainWindow.OpenDevTools();
        }

        app.CreateWebviewWindow(new WebviewWindowOptions
        {
            Name = "main",
            Title = "Company.AppName (Debug) - Wails.Net 桌面应用",
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
}
else
{
    // ---------- Release 模式 ----------
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
}

// 构建并运行应用
await desktopApp.RunAsync();
