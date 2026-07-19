// Demo: Wails.Net.Demo.Environment
// 目的：演示内置 OsInfoPlugin / PathPlugin / AppInfoPlugin 的命令调用。
// OsInfoPlugin 命令（参见 OsInfoPlugin.cs，同时注册 os.* 与 system.* 两套名）：
//   os.platform / os.hostname / os.arch / os.locale / os.version / os.type
//   system.platform / system.hostname / system.arch / system.locale / system.version / system.type / system.timezone
// PathPlugin 命令（参见 PathPlugin.cs）：
//   path.appDataDir / path.appConfigDir / path.appLogDir / path.appCacheDir
//   path.downloadDir / path.documentDir / path.homeDir / path.tempDir
//   path.configDir / path.dataDir / path.runtimeDir
// AppInfoPlugin 命令（参见 AppInfoPlugin.cs）：
//   app.getName / app.getVersion / app.getDescription / app.getTauriVersion
// 同时使用 EnvironmentLogService 绑定方法记录查询历史。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.Environment.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Environment";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册日志服务到 DI 容器
builder.Services.AddSingleton<EnvironmentLogService>();

// 启用内置插件
builder.UsePlugin<OsInfoPlugin>();       // 操作系统信息（os.* + system.* 命令）
builder.UsePlugin<PathPlugin>();         // 路径操作（path.* 命令）
builder.UsePlugin<AppInfoPlugin>();      // 应用信息（app.* 命令）

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册日志服务的绑定方法
app.RegisterBindings<EnvironmentLogService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - Environment",
        Width = 1000,
        Height = 700,
    });
};

await desktopApp.RunAsync();
