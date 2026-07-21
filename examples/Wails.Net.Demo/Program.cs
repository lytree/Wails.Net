using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Application.Services;
using Wails.Net.Application.Services.Updater;
using Wails.Net.Demo;
using Wails.Net.Demo.Plugins;
using Wails.Net.Demo.Services;

// 注意：必须显式调用 UseWindows()/UseLinux() 而非 UseAutoPlatform()，
// 因为 [ModuleInitializer] 仅在程序集被加载时触发，
// 而 .NET 程序集按需加载——只有显式引用平台程序集中的类型，
// 才会触发 WindowsPlatformRegistrar / LinuxPlatformRegistrar 的模块初始化器完成委托注册。

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

// P1-3：Logger ↔ 前端 console 双向桥接
// UseBrowserConsoleLogReceiver：前端 console.log → 后端 ILogger
// UseBrowserConsoleLogForwarder：后端 ILogger → 前端 DevTools console
builder.UseBrowserConsoleLogReceiver();
builder.UseBrowserConsoleLogForwarder();

// 注册绑定服务到 DI 容器
// 这些服务的公共方法将自动暴露给前端 JavaScript
builder.Services.AddSingleton<GreetingService>();
builder.Services.AddSingleton<TodoService>();
builder.Services.AddSingleton<P1FeaturesService>();

// P1-8：注册 UpdaterService（多 Provider Updater）
// 实际应用中应配置真实的 UpdateURL 或 GitHub/GitLab Provider
builder.Services.AddSingleton<UpdaterService>(sp =>
{
    var service = new UpdaterService
    {
        CurrentVersion = "1.0.0",
    };
    // 演示：注册一个自定义的 Mock Provider，始终返回"无更新"
    // 实际应用中替换为 HttpUpdateProvider / GitHubUpdateProvider / GitLabUpdateProvider
    service.AddProvider(new MockUpdateProvider());
    return service;
});

// 使用内置插件（每个插件提供一组前端可调用的命令）
builder.UsePlugin<WindowPlugin>();           // 窗口操作（将 wails.window.* 转为插件命令）
builder.UsePlugin<WindowsPlugin>();          // 窗口查询（将 wails.windows.* 转为插件命令）
builder.UsePlugin<ApplicationPlugin>();      // 应用级操作（将 wails.application.* 转为插件命令）
builder.UsePlugin<TrayPlugin>();            // 系统托盘（将 wails.tray.* 转为插件命令）
builder.UsePlugin<MenuPlugin>();            // 菜单（将 wails.menu.* 转为插件命令）
builder.UsePlugin<ScreenPlugin>();          // 屏幕查询（将 wails.screen.* 转为插件命令）
builder.UsePlugin<LogPlugin>();             // 日志记录
builder.UsePlugin<ClipboardPlugin>();        // 剪贴板操作
builder.UsePlugin<DialogPlugin>();           // 原生对话框
builder.UsePlugin<NotificationPlugin>();     // 系统通知
builder.UsePlugin<OsInfoPlugin>();           // 操作系统信息
builder.UsePlugin<StorePlugin>();            // 键值存储
builder.UsePlugin<PathPlugin>();             // 路径操作
builder.UsePlugin<AppInfoPlugin>();           // 应用信息
builder.UsePlugin<UpdaterPlugin>();          // 更新插件（P1-8）

// 使用自定义插件
builder.UsePlugin<MyCustomPlugin>();          // 自定义计数器插件

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现。
// Windows 上注册 WindowsPlatformApp，Linux 上注册 LinuxPlatformApp。
// PlatformFactory.TryLoadPlatformAssembly 会通过 Assembly.Load + RuntimeHelpers.RunModuleConstructor
// 显式触发对应平台程序集的 [ModuleInitializer] 注册委托，无需显式调用 UseWindows()/UseLinux()。
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 设置 ApplicationOptions 中 DesktopHostOptions 未覆盖的选项
app.Options.EnableDefaultContextMenu = true;  // P1-4：ContextMenu 行为对齐
app.Options.DragAndDrop = true;

// 启用退出确认对话框：用户点击窗口关闭按钮时弹出 GTK 原生确认框。
// 仅在最后一个窗口关闭时触发，用户选择"取消"则阻止退出。
app.Options.ShowExitConfirmationDialog = true;
app.Options.ExitDialogTitle = "确认退出 Wails.Net Demo";
app.Options.ExitDialogMessage = "确定要退出应用吗？未保存的数据将丢失。";

// P1-7：Event Hooks 补齐（PostShutdown / ShouldQuit）
// 注意：这两个回调定义在 ApplicationOptions 上，不是 DesktopHostOptions。
app.Options.PostShutdown = () =>
{
    // 此回调在 Application.Shutdown 末尾、所有清理完成后调用。
    // 实际应用中可用于：释放外部资源、写入退出日志、通知守护进程等。
    Console.WriteLine("[Demo] PostShutdown 回调已触发 — 所有清理完成");
};

app.Options.ShouldQuit = () =>
{
    // 此回调由平台信号处理器在收到系统级退出信号时调用。
    // 返回 false 可阻止退出（例如：有未保存的数据）。
    // 此处始终返回 true 以允许退出。
    return true;
};

// P1-6：Service Route 挂载能力（IHttpServiceHandler）
// 将 /api/health 和 /api/version 路由挂载到 AssetServer，无需独立启动 ASP.NET Core 管道。
// 实际应用中可用于：健康检查、版本接口、轻量 API 等。
app.RegisterService(new DemoHealthHandler(), new ServiceOptions { Route = "/api/health" });
app.RegisterService(new DemoVersionHandler(), new ServiceOptions { Route = "/api/version" });

// 从 DI 容器获取绑定服务并注册到 BindingManager。
// 对应 ASP.NET Core 风格：DI 是单一注册点，避免双重实例。
app.RegisterBindings<GreetingService>();
app.RegisterBindings<TodoService>();
app.RegisterBindings<P1FeaturesService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    // 未设置 URL 或 HTML 时，窗口将自动导航到 http://wails.localhost/ (Windows)
    // 或 wails://localhost/ (Linux)，由 AssetServer 提供静态资源服务。
    // 无需构建 file:// URL，避免权限问题。
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - 桌面应用示例（含 P1 新能力）",
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
