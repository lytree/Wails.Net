using LyboxApp.Plugins;
using LyboxApp.Services.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Generated;

// 显式调用源生成器生成的注册方法（[ModuleInitializer] 安全网），
// 确保 GeneratedBindingRegistry 在应用启动前填充（[Binding]/[Command] 强类型调用器）。
GeneratedBindingRegistryRegistration.Register();

// 创建桌面应用构建器（Generic Host 模式）
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "LYBox (Wails.Net)";
    options.SingleInstance = true;
    options.Window.Frameless = false;

    // 前端构建产物目录
    options.Assets.RootPath = "frontend/dist";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 核心插件（注册设置存储、事件总线、本地化、任务注册表、导航/插件清单注册表）
builder.UsePlugin<CorePlugin>();

// AvaloniaTemplate (LYBox) 功能项插件（移植为 Wails.Net 插件）
builder.UsePlugin<TemplatePlugin>();
builder.UsePlugin<ButtonsInputsPlugin>();
builder.UsePlugin<DateTimePlugin>();
builder.UsePlugin<DialogFeedbacksPlugin>();
builder.UsePlugin<DownloaderPlugin>();
builder.UsePlugin<NavigationMenusPlugin>();

builder.Logging.SetMinimumLevel(LogLevel.Information);

// 平台工厂自动检测（Windows/Linux/Android）
builder.UseAutoPlatform();

var desktopApp = builder.Build();
var app = desktopApp.Application;

// 将事件总线挂载到应用，使后端 [Binding] 服务可向前端广播事件
var eventBus = desktopApp.Services.GetRequiredService<LyboxEventBus>();
eventBus.Attach((name, data) => app.Events.Emit(name, data));

// 注册 [Binding] 服务（源生成器生成强类型调用器，前端通过 window.wails.call 调用）
app.RegisterService(desktopApp.Services.GetRequiredService<LyboxCoreService>());
app.RegisterService(desktopApp.Services.GetRequiredService<SettingsService>());
app.RegisterService(desktopApp.Services.GetRequiredService<LocalizationService>());
app.RegisterService(desktopApp.Services.GetRequiredService<TemplateService>());
app.RegisterService(desktopApp.Services.GetRequiredService<ButtonsInputsService>());
app.RegisterService(desktopApp.Services.GetRequiredService<DateTimeService>());
app.RegisterService(desktopApp.Services.GetRequiredService<DialogFeedbacksService>());
app.RegisterService(desktopApp.Services.GetRequiredService<DownloaderService>());
app.RegisterService(desktopApp.Services.GetRequiredService<NavigationMenusService>());

// 订阅前端发来的事件（对话框结果、任务取消等）
app.Events.On("lybox:dialog-result", evt =>
{
    if (evt.Data is System.Text.Json.JsonElement je && je.TryGetProperty("id", out var idProp))
    {
        var id = idProp.GetString();
        eventBus.Emit("lybox:dialog-ack", new { id });
    }
});

app.Events.On("lybox:cancel-task", evt =>
{
    if (evt.Data is System.Text.Json.JsonElement je && je.TryGetProperty("taskId", out var tProp))
    {
        var taskId = tProp.GetString();
        if (taskId is not null)
        {
            var registry = desktopApp.Services.GetRequiredService<TaskRegistry>();
            registry.Cancel(taskId);
        }
    }
});

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "LYBox — Wails.Net 功能演示",
        Width = 1280,
        Height = 820,
        MinWidth = 900,
        MinHeight = 600,
        Resizable = true,
        Maximisable = true,
        Minimisable = true,
        Fullscreen = false,
    });
};

await desktopApp.RunAsync();
