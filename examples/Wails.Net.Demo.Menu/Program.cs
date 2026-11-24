using Wails.Net.Plugins.Menu;
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

using Wails.Net.Demo.Menu.Services;

// 创建桌面应用构建器
// 通过 DebugMode 统一判定当前模式（优先级：WAILS_DEBUG > --debug > .NET 环境变量）
var isDebugMode = DebugMode.IsEnabled(args);

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

// 配置日志级别（Debug 模式输出更详细日志，Release 模式仅 Information 以上）
builder.Logging.SetMinimumLevel(isDebugMode ? LogLevel.Debug : LogLevel.Information);
builder.Logging.AddFilter("Microsoft", isDebugMode ? LogLevel.Information : LogLevel.Warning);

// 使用平台工厂自动检测并注册平台实现
builder.UseWailsPlatform();

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
    // 主题三态与原生标题栏上的主题按钮共用 Application.ThemeMode，两者互相同步。
    var viewMenu = menu.AddSubmenu("视图");
    var themeMenu = viewMenu.AddSubmenu("主题");
    themeMenu.AddRadioMenuItem("按系统", true, () => SetTheme(app, menuLogService, ThemeMode.System));
    themeMenu.AddRadioMenuItem("暗黑", false, () => SetTheme(app, menuLogService, ThemeMode.Dark));
    themeMenu.AddRadioMenuItem("明亮", false, () => SetTheme(app, menuLogService, ThemeMode.Light));
    viewMenu.AddSeparator();
    viewMenu.AddCheckboxMenuItem("显示侧边栏", true, () => OnMenuClick(app, menuLogService, "view.sidebar"));

    // 帮助菜单
    var helpMenu = menu.AddSubmenu("帮助");
    helpMenu.AddMenuItem("关于", () => OnMenuClick(app, menuLogService, "help.about"));
    helpMenu.AddMenuItem("文档", () => OnMenuClick(app, menuLogService, "help.docs"));

    app.SetApplicationMenu(menu);

    // 创建主窗口
    var mainWindow = app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = isDebugMode ? "Wails.Net Demo - Menu (Debug)" : "Wails.Net Demo - Menu",
        Width = 1000,
        Height = 700,

        // 在原生标题栏「最小化」旁显示主题按钮，点击循环 按系统 → 暗黑 → 明亮，
        // 与 视图 → 主题 菜单项驱动同一份应用级主题状态
        //（Windows 自绘 / macOS 标题栏附件视图 / Linux HeaderBar 按钮）。
        TitleBarThemeButton = true,
    });

    // Debug 模式：窗口创建后自动打开 DevTools（延迟等待 WebView2 初始化完成）
    if (isDebugMode)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            try
            {
                mainWindow.OpenDevTools();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Demo] DevTools 打开失败：{ex.Message}");
            }
        });
    }
};

// 菜单点击处理：记录历史并广播事件到前端
static void OnMenuClick(Wails.Net.Application.Application app, MenuLogService logService, string menuId)
{
    logService.RecordClick(menuId);
    app.Events.Emit("menu:clicked", new { id = menuId, time = DateTime.Now.ToString("HH:mm:ss") });
}

// 主题切换：直接驱动应用级主题（原生标题栏 / 原生菜单 / 标题栏主题按钮共用同一状态），
// 同时按菜单点击记录历史，前端据此能看到来源。
static void SetTheme(Wails.Net.Application.Application app, MenuLogService logService, ThemeMode mode)
{
    app.SetThemeMode(mode);
    OnMenuClick(app, logService, mode switch
    {
        ThemeMode.Dark => "view.theme.dark",
        ThemeMode.Light => "view.theme.light",
        _ => "view.theme.system",
    });
}

await desktopApp.RunAsync();
