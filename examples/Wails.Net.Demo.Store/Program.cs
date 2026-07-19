// Demo: Wails.Net.Demo.Store
// 目的：演示内置 StorePlugin（键值存储）的命令调用。
// StorePlugin 通过 MapCommand 注册以下命令（参见 StorePlugin.cs）：
//   store.set(key, value)    — 设置键值
//   store.get(key)           — 获取值
//   store.delete(key)        — 删除键
//   store.has(key)           — 检查键是否存在
//   store.keys()             — 列出所有键
//   store.clear()            — 清空所有键
//   store.watch(key)         — 监听键变更
// 同时使用 StoreLogService 绑定方法记录操作日志（计数 + 最近 5 条）。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.Store.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Store";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册日志服务到 DI 容器
builder.Services.AddSingleton<StoreLogService>();

// 启用内置 StorePlugin（提供 store.set / store.get / store.delete 等命令）
builder.UsePlugin<StorePlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册日志服务的绑定方法
app.RegisterBindings<StoreLogService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - Store",
        Width = 1000,
        Height = 700,
    });
};

await desktopApp.RunAsync();
