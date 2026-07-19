// Demo: Wails.Net.Demo.Updater
// 目的：演示应用更新（Updater）能力，重点是 P1-8 多 Provider 链式检查机制。
// 注册三个自定义 IUpdateProvider（mock-stable / mock-beta / mock-rc），
// 它们分别返回不同版本号的 UpdateManifest。UpdaterService 按注册顺序依次尝试，
// 首个返回非 null 清单的提供者胜出。
// 通过 UpdaterPlugin 暴露 updater.check / updater.download / updater.install 命令，
// 同时 UpdaterService 自身的 CheckForUpdatesAsync 在发现新版本时广播
// wails:updater:update-available 事件，前端订阅实时显示状态。
// UpdaterDemoService 通过 [Binding] 方法提供：
// - 切换 Provider 链（清空 + 重新注册）
// - 设置当前版本号
// - 触发检查 / 下载 / 安装
// - 查询 Provider 列表与历史

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Application.Services;
using Wails.Net.Application.Services.Updater;
using Wails.Net.Demo.Updater.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Updater";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册 UpdaterDemoService（绑定服务）
builder.Services.AddSingleton<UpdaterDemoService>();

// 覆盖默认 UpdaterService 注册：注入多个自定义 Provider 用于演示
builder.Services.AddSingleton<UpdaterService>(sp =>
{
    var service = new UpdaterService
    {
        CurrentVersion = "1.0.0",
    };
    // 默认仅注册 stable provider，前端可通过绑定方法切换链
    service.AddProvider(new MockStableUpdateProvider());
    return service;
});

// 启用内置 UpdaterPlugin（提供 updater.check / updater.download / updater.install 命令）
builder.UsePlugin<UpdaterPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定
app.RegisterBindings<UpdaterDemoService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - Updater",
        Width = 1000,
        Height = 700,
    });
};

await desktopApp.RunAsync();
