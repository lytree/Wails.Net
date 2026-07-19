// Demo: Wails.Net.Demo.Events
// 目的：演示 Wails.Net 事件系统的双向通信能力。
// 后端通过 app.Events.Emit 广播事件到前端，前端通过 wails.events.emit 发送事件到后端。
// 后端通过 app.Events.On 订阅前端事件，前端通过 wails.events.on 订阅后端事件。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Demo.Events.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Events";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册事件服务到 DI 容器
builder.Services.AddSingleton<EventService>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定
app.RegisterBindings<EventService>();

// 后端订阅前端事件：当前端 emit "frontend:event" 时，后端接收并广播一个 demo:echo 事件回前端
app.Events.On("frontend:event", evt =>
{
    // 将前端发送的事件数据原样回显，附带后端处理时间戳
    app.Events.Emit("demo:echo", new
    {
        original = evt.Data,
        receivedAt = DateTime.Now.ToString("HH:mm:ss.fff"),
    });
});

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - Events",
        Width = 1000,
        Height = 700,
    });
};

await desktopApp.RunAsync();
