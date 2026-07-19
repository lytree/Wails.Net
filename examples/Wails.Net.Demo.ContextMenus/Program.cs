// Demo: Wails.Net.Demo.ContextMenus
// 目的：演示上下文菜单（Context Menu）的注册与触发。
// 通过 menu.setContextMenu 命令注册多个命名上下文菜单，
// 前端通过 CSS 变量 --custom-contextmenu 引用对应菜单 ID，
// 右键元素时由 MessageProcessor 自动弹出已注册的菜单。
// 菜单项点击后通过 app.Events.Emit 广播 contextmenu:clicked 事件到前端。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.ContextMenus.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项：启用默认右键菜单
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - ContextMenus";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务
builder.Services.AddSingleton<ContextMenuService>();

// 启用内置 MenuPlugin（提供 menu.setContextMenu / menu.popup 等命令）
builder.UsePlugin<MenuPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定
var contextMenuService = new ContextMenuService();
app.RegisterService(contextMenuService);

// 应用启动后：构建并注册 3 个命名上下文菜单，然后创建主窗口
app.Options.OnAfterStart = () =>
{
    // 输入框上下文菜单：剪切/复制/粘贴/清除
    var inputMenu = new Wails.Net.Application.Menus.ContextMenu();
    inputMenu.AddMenuItem("剪切", () => OnContextClick(app, contextMenuService, "input", "cut"));
    inputMenu.AddMenuItem("复制", () => OnContextClick(app, contextMenuService, "input", "copy"));
    inputMenu.AddMenuItem("粘贴", () => OnContextClick(app, contextMenuService, "input", "paste"));
    inputMenu.AddSeparator();
    inputMenu.AddMenuItem("清空输入框", () => OnContextClick(app, contextMenuService, "input", "clear"));
    app.MenuManager?.RegisterContextMenu("input-context-menu", inputMenu);

    // 按钮上下文菜单：禁用/启用/重置颜色
    var buttonMenu = new Wails.Net.Application.Menus.ContextMenu();
    buttonMenu.AddMenuItem("禁用按钮", () => OnContextClick(app, contextMenuService, "button", "disable"));
    buttonMenu.AddMenuItem("启用按钮", () => OnContextClick(app, contextMenuService, "button", "enable"));
    buttonMenu.AddSeparator();
    buttonMenu.AddMenuItem("重置颜色", () => OnContextClick(app, contextMenuService, "button", "reset-color"));
    app.MenuManager?.RegisterContextMenu("button-context-menu", buttonMenu);

    // 文本区域上下文菜单：复制/全选/反选
    var textMenu = new Wails.Net.Application.Menus.ContextMenu();
    textMenu.AddMenuItem("复制文本", () => OnContextClick(app, contextMenuService, "text", "copy"));
    textMenu.AddMenuItem("全选", () => OnContextClick(app, contextMenuService, "text", "select-all"));
    textMenu.AddSeparator();
    textMenu.AddCheckboxMenuItem("反选显示", false, () => OnContextClick(app, contextMenuService, "text", "invert"));
    app.MenuManager?.RegisterContextMenu("text-context-menu", textMenu);

    // 创建主窗口
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - ContextMenus",
        Width = 1000,
        Height = 700,
    });
};

// 上下文菜单点击处理：记录历史并广播事件到前端
static void OnContextClick(Wails.Net.Application.Application app, ContextMenuService service, string target, string action)
{
    service.RecordContextAction(target, action);
    app.Events.Emit("contextmenu:clicked", new { target, action, time = DateTime.Now.ToString("HH:mm:ss") });
}

await desktopApp.RunAsync();
