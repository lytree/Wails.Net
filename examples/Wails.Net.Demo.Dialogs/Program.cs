// Demo: Wails.Net.Demo.Dialogs
// 目的：演示内置 DialogPlugin 提供的各类原生对话框（信息/警告/错误/询问/打开文件/保存文件/多文件选择），
// 并通过 DialogHistoryService 绑定方法记录用户操作历史。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.Dialogs.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Dialogs";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务
builder.Services.AddSingleton<DialogHistoryService>();

// 启用内置 DialogPlugin（提供 dialog.message / dialog.warning / dialog.error / dialog.question /
// dialog.openFile / dialog.saveFile / dialog.openMultipleFiles 命令）
builder.UsePlugin<DialogPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定
app.RegisterBindings<DialogHistoryService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - Dialogs",
        Width = 1000,
        Height = 700,
    });
};

await desktopApp.RunAsync();
