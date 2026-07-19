// Demo: Wails.Net.Demo.Menu
// 目的：演示应用菜单（Application Menu）的构建与点击处理。
// 通过 MenuPlugin 注册菜单命令，通过 Application.SetApplicationMenu 设置应用菜单，
// 每个菜单项的 Callback 触发后通过 app.Events.Emit 广播 menu:clicked 事件到前端，
// 同时调用 MenuLogService 记录点击历史。
// 演示特性：子菜单、分隔符、复选菜单项、禁用菜单项、角色菜单项。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.Menu.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Menu";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务
builder.Services.AddSingleton<MenuLogService>();

// 启用内置 MenuPlugin（提供 menu.setApplicationMenu / menu.updateMenuItem 等命令）
builder.UsePlugin<MenuPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定
var menuLogService = new MenuLogService();
app.RegisterService(menuLogService);

// 应用启动后构建并设置应用菜单
app.Options.OnAfterStart = () =>
{
    var menu = new Wails.Net.Application.Menus.Menu();

    // 文件菜单
    var fileMenu = menu.AddSubmenu("文件");
    fileMenu.AddMenuItem("新建", () => OnMenuClick(app, menuLogService, "file.new"));
    fileMenu.AddMenuItem("打开", () => OnMenuClick(app, menuLogService, "file.open"));
    fileMenu.AddSeparator();
    // 禁用菜单项示例
    var disabledItem = fileMenu.AddMenuItem("保存（已禁用）", () => OnMenuClick(app, menuLogService, "file.save"));
    disabledItem.IsDisabled = true;
    fileMenu.AddSeparator();
    fileMenu.AddMenuItem("退出", () => OnMenuClick(app, menuLogService, "file.quit"));

    // 编辑菜单（使用标准 Edit 角色）
    var editMenu = menu.AddSubmenu("编辑");
    editMenu.AddRoleItem(MenuRole.Undo);
    editMenu.AddRoleItem(MenuRole.Redo);
    editMenu.AddSeparator();
    editMenu.AddRoleItem(MenuRole.Cut);
    editMenu.AddRoleItem(MenuRole.Copy);
    editMenu.AddRoleItem(MenuRole.Paste);
    editMenu.AddRoleItem(MenuRole.SelectAll);

    // 视图菜单：包含子菜单（主题）与复选菜单项（侧边栏）
    var viewMenu = menu.AddSubmenu("视图");
    var themeMenu = viewMenu.AddSubmenu("主题");
    themeMenu.AddCheckboxMenuItem("浅色", true, () => OnMenuClick(app, menuLogService, "view.theme.light"));
    themeMenu.AddCheckboxMenuItem("深色", false, () => OnMenuClick(app, menuLogService, "view.theme.dark"));
    viewMenu.AddSeparator();
    viewMenu.AddCheckboxMenuItem("显示侧边栏", true, () => OnMenuClick(app, menuLogService, "view.sidebar"));

    // 帮助菜单
    var helpMenu = menu.AddSubmenu("帮助");
    helpMenu.AddMenuItem("关于", () => OnMenuClick(app, menuLogService, "help.about"));
    helpMenu.AddMenuItem("文档", () => OnMenuClick(app, menuLogService, "help.docs"));

    app.SetApplicationMenu(menu);

    // 创建主窗口
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - Menu",
        Width = 1000,
        Height = 700,
    });
};

// 菜单点击处理：记录历史并广播事件到前端
static void OnMenuClick(Wails.Net.Application.Application app, MenuLogService logService, string menuId)
{
    logService.RecordClick(menuId);
    app.Events.Emit("menu:clicked", new { id = menuId, time = DateTime.Now.ToString("HH:mm:ss") });
}

await desktopApp.RunAsync();
