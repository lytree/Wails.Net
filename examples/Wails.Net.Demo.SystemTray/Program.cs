// Demo: Wails.Net.Demo.SystemTray
// 目的：演示系统托盘（SystemTray）能力。
// 通过 ISystemTrayManager.CreateSystemTray 创建托盘实例，
// 使用 Menu 构建右键菜单（显示窗口、隐藏窗口、发送通知、退出），
// 订阅 ISystemTrayImpl.OnTrayClick 处理托盘左键点击事件，
// 通过框架 NotificationService 发送托盘通知，
// 同时通过 TrayPlugin 暴露 tray.* 命令到前端，
// TrayLogService 维护事件历史并广播 tray:clicked 事件到前端。

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Application.Services;
using Wails.Net.Demo.SystemTray.Services;

// 创建桌面应用构建器
var builder = DesktopApplicationBuilder.CreateBuilder(args);

// 配置应用选项
builder.Configure(options =>
{
    options.ApplicationName = "Wails.Net Demo - SystemTray";
    options.Assets.RootPath = "frontend";
    options.Assets.DefaultDocument = "index.html";
    options.Assets.EnableSpaFallback = true;
});

// 注册绑定服务
builder.Services.AddSingleton<TrayLogService>();

// 启用内置 TrayPlugin（提供 tray.setIcon / tray.setLabel / tray.show / tray.hide 等命令）
builder.UsePlugin<TrayPlugin>();
// 启用内置 NotificationPlugin（用于发送托盘通知）
builder.UsePlugin<NotificationPlugin>();

// 配置日志级别
builder.Logging.SetMinimumLevel(LogLevel.Information);

// 使用平台工厂自动检测并注册平台实现
builder.UseAutoPlatform();

// 构建应用实例
var desktopApp = builder.Build();
var app = desktopApp.Application;

// 注册绑定
var trayLogService = app.RegisterBindings<TrayLogService>();

// 应用启动后创建主窗口和系统托盘
app.Options.OnAfterStart = () =>
{
    // 创建主窗口
    app.CreateWebviewWindow(new WebviewWindowOptions
    {
        Name = "main",
        Title = "Wails.Net Demo - SystemTray",
        Width = 1000,
        Height = 700,
    });

    // 创建系统托盘（使用内置示例图标，1x1 蓝色 PNG，跨平台无需 System.Drawing.Common）
    var iconBytes = CreateSampleIconBytes();
    var manager = app.SystemTrayManager;
    if (manager is null)
    {
        trayLogService.RecordEvent("error", "SystemTrayManager 未注入，无法创建托盘");
        return;
    }

    var tray = manager.CreateSystemTray(iconBytes);
    manager.SetTooltip(tray, "Wails.Net SystemTray Demo");
    manager.SetLabel(tray, "Wails.Net");

    // 订阅托盘左键点击事件：显示主窗口并记录
    tray.OnTrayClick += () =>
    {
        trayLogService.RecordEvent("click", "托盘左键点击");
        var window = app.GetWindowByName("main");
        window?.Show();
        window?.Focus();
        app.Events.Emit("tray:clicked", new { button = "left", time = DateTime.Now.ToString("HH:mm:ss") });
    };

    // 构建托盘右键菜单：显示窗口 / 隐藏窗口 / 发送通知 / 退出
    // 使用完全限定名消除 System.Windows.Forms.Menu 与 Wails.Net.Application.Menus.Menu 的歧义（CS0104）
    var menu = new Wails.Net.Application.Menus.Menu();
    menu.AddMenuItem("显示窗口", () =>
    {
        trayLogService.RecordEvent("menu", "显示窗口");
        var window = app.GetWindowByName("main");
        window?.Show();
        window?.Focus();
    });
    menu.AddMenuItem("隐藏窗口", () =>
    {
        trayLogService.RecordEvent("menu", "隐藏窗口");
        app.GetWindowByName("main")?.Hide();
    });
    menu.AddSeparator();
    menu.AddMenuItem("发送通知", () =>
    {
        trayLogService.RecordEvent("menu", "发送通知");
        // 使用框架 NotificationService 发送系统通知
        var notifier = app.Services.Services.OfType<NotificationService>().FirstOrDefault();
        notifier?.SendNotification("Wails.Net 托盘通知", "您点击了托盘菜单中的「发送通知」项");
    });
    menu.AddSeparator();
    menu.AddMenuItem("退出", () =>
    {
        trayLogService.RecordEvent("menu", "退出");
        app.Quit();
    });
    manager.SetMenu(tray, menu);

    // 显示托盘
    manager.Show(tray);

    // 将托盘实例写入 TrayHolder，供 TrayPlugin 的 tray.* 命令操作
    var holder = app.Services.Services.OfType<TrayHolder>().FirstOrDefault();
    if (holder is not null)
    {
        holder.ActiveTray = tray;
    }

    trayLogService.RecordEvent("info", "系统托盘已创建并显示");
};

// 生成 1x1 蓝色像素 PNG 图标字节（跨平台，无需 System.Drawing.Common）
// 包含 PNG 签名 + IHDR（1x1, 8-bit RGBA）+ IDAT（zlib 压缩的透明像素）+ IEND
static byte[] CreateSampleIconBytes()
{
    return new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk header (length=13)
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // width=1, height=1
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, // bitdepth=8, colortype=6(RGBA), CRC
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, // IDAT chunk header (length=13)
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, // zlib stream + deflate data
        0x00, 0x00, 0x03, 0x00, 0x01, 0x5C, 0xCD, 0xFF, // CRC
        0x69, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, // IEND chunk header (length=0)
        0x44, 0xAE, 0x42, 0x60, 0x82                     // IEND CRC
    };
}

await desktopApp.RunAsync();
