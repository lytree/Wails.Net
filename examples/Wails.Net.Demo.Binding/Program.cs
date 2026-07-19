// Demo: Wails.Net.Demo.Binding
// 目的：演示 Wails.Net 绑定系统的各类方法签名（同步、异步、重载、复杂对象、集合、异常、CancellationToken）。
// 所有公共方法用 [Binding] 特性标记，由源代码生成器（非反射）暴露给前端 JavaScript。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.Binding.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Binding";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务到 DI 容器
builder.Services.AddSingleton<BindingService>();

// 仅启用 OsInfoPlugin 用于演示上下文（提供 wails.os.* 命令）
builder.UsePlugin<OsInfoPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定（[Binding] 标记的方法会被源生成器收集）
app.RegisterBindings<BindingService>();

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - Binding",
        Width = 1000,
        Height = 700,
    });
};

await desktopApp.RunAsync();
