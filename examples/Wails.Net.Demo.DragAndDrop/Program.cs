// Demo: Wails.Net.Demo.DragAndDrop
// 目的：演示文件拖放能力。
// 平台层在窗口收到文件拖放事件后会广播 KnownEvents.WindowFileDropped 事件
// （事件名 "wails:window:file:dropped"，payload 为文件路径字符串数组）。
// 前端订阅此事件实时显示拖入的文件列表；
// 后端 FileDropService 通过 [Binding] 方法记录拖放历史、读取文件元信息，
// 并提供清空历史与查询统计的能力。
// 同时通过 WindowPlugin 的 window.setFileDropEnabled 命令演示运行时启用/禁用拖放。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.DragAndDrop.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - DragAndDrop";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务
builder.Services.AddSingleton<FileDropService>();

// 启用内置 WindowPlugin（提供 window.setFileDropEnabled 等命令）
builder.UsePlugin<WindowPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 启用文件拖放（DragAndDrop 位于 ApplicationOptions，非 DesktopHostOptions）
// 默认即为 true，这里显式设置以示说明
app.Options.DragAndDrop = true;

// 注册绑定
app.RegisterBindings<FileDropService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    var window = app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - DragAndDrop",
        Width = 1000,
        Height = 700,
    });

    // 显式启用文件拖放（默认已启用，此处演示 API 调用）
    window.SetFileDropEnabled(true);
};

await desktopApp.RunAsync();
