using Wails.Net.Plugins.Clipboard;
// Demo: Wails.Net.Demo.Clipboard
// 目的：演示内置 ClipboardPlugin 与自定义绑定方法的配合使用。
// ClipboardPlugin 提供 clipboard.setText / clipboard.getText 命令，
// ClipboardStatsService 提供复制次数统计（绑定方法）。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.Clipboard.Services;

// 创建桌面应用构建器
// 通过 DebugMode 统一判定当前模式（优先级：WAILS_DEBUG > --debug > .NET 环境变量）
var isDebugMode = DebugMode.IsEnabled(args);

var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Clipboard";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务
builder.Services.AddSingleton<ClipboardStatsService>();

// 启用内置 ClipboardPlugin（提供 clipboard.setText / clipboard.getText 等命令）
builder.UsePlugin<ClipboardPlugin>();

// 配置日志级别（Debug 模式输出更详细日志，Release 模式仅 Information 以上）
builder.Logging.SetMinimumLevel(isDebugMode ? LogLevel.Debug : LogLevel.Information);
builder.Logging.AddFilter("Microsoft", isDebugMode ? LogLevel.Information : LogLevel.Warning);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定
app.RegisterBindings<ClipboardStatsService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    var mainWindow = app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = isDebugMode ? "Wails.Net Demo - Clipboard (Debug)" : "Wails.Net Demo - Clipboard",
        Width = 1000,
        Height = 700,
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
                Console.WriteLine($"[Demo] DevTools 打开失败：{ex.Message}");
            }
        });
    }
};

await desktopApp.RunAsync();
