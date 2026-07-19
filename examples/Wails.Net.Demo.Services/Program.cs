// Demo: Wails.Net.Demo.Services
// 目的：演示 Service Route 挂载多个 HTTP 路由（P1-6 IHttpServiceHandler）。
// 注册 3 个 IHttpServiceHandler，分别挂载到 /api/users、/api/products、/api/orders，
// 支持 GET（列出）与 POST（创建）请求，返回 JSON 响应。
// 同时通过 [Binding] 方法暴露相同的数据操作给前端，便于对比两种调用方式。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Services;
using Wails.Net.Demo.Services.Handlers;
using Wails.Net.Demo.Services.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Services";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册服务到 DI 容器
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<ProductService>();
builder.Services.AddSingleton<OrderService>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 从 DI 解析服务实例并注册到 BindingManager
// 同一实例将共享给 HTTP 处理器，确保两条调用路径操作同一份数据
var userService = app.RegisterBindings<UserService>();
var productService = app.RegisterBindings<ProductService>();
var orderService = app.RegisterBindings<OrderService>();

// P1-6：Service Route 挂载 — 3 个 HTTP API 路由
app.RegisterService(new UserApiHandler(userService), new ServiceOptions { Route = "/api/users" });
app.RegisterService(new ProductApiHandler(productService), new ServiceOptions { Route = "/api/products" });
app.RegisterService(new OrderApiHandler(orderService), new ServiceOptions { Route = "/api/orders" });

// 应用启动后创建主窗口
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - Services",
        Width = 1000,
        Height = 700,
    });
};

await desktopApp.RunAsync();
