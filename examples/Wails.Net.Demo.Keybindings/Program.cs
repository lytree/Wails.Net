// Demo: Wails.Net.Demo.Keybindings
// 目的：演示全局快捷键（Global Shortcut / KeyBinding）能力。
// 通过 Application.KeyBindingManager.RegisterKeyBinding 注册后端全局热键
// （Windows 使用 RegisterHotKey，Linux 使用 GTK Accelerator），
// 触发时通过 Application.Events 广播 keybinding:pressed 事件到前端。
// 同时启用 GlobalShortcutPlugin，前端可通过 globalshortcut.register / unregister 命令
// 动态注册快捷键，触发时通过 globalshortcut:pressed 事件接收。
// KeybindingLogService 维护触发历史并暴露查询/清空绑定方法。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Demo.Keybindings.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - Keybindings";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务
builder.Services.AddSingleton<KeybindingLogService>();

// 启用内置 GlobalShortcutPlugin（提供 globalshortcut.register / unregister / isRegistered 命令）
builder.UsePlugin<GlobalShortcutPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定
var logService = app.RegisterBindings<KeybindingLogService>();

// 应用启动后创建主窗口并注册后端全局热键
app.Options.OnAfterStart = () =>
{
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - Keybindings",
        Width = 1000,
        Height = 700,
    });

    // 通过 KeyBindingManager 注册后端全局热键（不依赖插件命令）
    var manager = app.KeyBindingManager;
    if (manager is null)
    {
        logService.Record("error", "KeyBindingManager 未注入，后端快捷键不可用");
        return;
    }

    // Ctrl+Alt+T：触发测试事件
    RegisterBackendHotkey(manager, app, logService, "Ctrl+Alt+T", "后端热键 Ctrl+Alt+T 触发");
    // Ctrl+Alt+H：隐藏主窗口
    RegisterBackendHotkey(manager, app, logService, "Ctrl+Alt+H", "后端热键 Ctrl+Alt+H 触发（隐藏窗口）",
        () => app.GetWindowByName("main")?.Hide());
    // Ctrl+Alt+S：显示主窗口
    RegisterBackendHotkey(manager, app, logService, "Ctrl+Alt+S", "后端热键 Ctrl+Alt+S 触发（显示窗口）",
        () =>
        {
            var w = app.GetWindowByName("main");
            w?.Show();
            w?.Focus();
        });
    // F9：纯功能键热键
    RegisterBackendHotkey(manager, app, logService, "F9", "后端热键 F9 触发");

    logService.Record("info", "已注册 4 个后端全局热键：Ctrl+Alt+T / Ctrl+Alt+H / Ctrl+Alt+S / F9");
};

// 注册后端全局热键的本地辅助方法
// 使用完全限定名 Wails.Net.Application.Application 消除与 System.Windows.Forms.Application 的歧义（CS0104/CS1503）
static void RegisterBackendHotkey(
    Wails.Net.Application.Managers.IKeyBindingManager manager,
    Wails.Net.Application.Application app,
    KeybindingLogService logService,
    string accelerator,
    string description,
    Action? extra = null)
{
    try
    {
        manager.RegisterKeyBinding(accelerator, () =>
        {
            logService.Record("backend", description);
            app.Events.Emit("keybinding:pressed", new { source = "backend", accelerator, time = DateTime.Now.ToString("HH:mm:ss") });
            extra?.Invoke();
        });
    }
    catch (Exception ex)
    {
        logService.Record("error", $"注册 {accelerator} 失败：{ex.Message}");
    }
}

await desktopApp.RunAsync();
