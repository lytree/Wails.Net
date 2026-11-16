using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Coordinators;
using Wails.Net.Application.Events;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Invocation;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Services;
using Wails.Net.Application.Streams;
using Wails.Net.Application.Transport;
using Wails.Net.Application.Windows;
using Wails.Net.Events;

namespace Wails.Net.Application;

/// <summary>
/// Application 的管理器分部（RH-HOST-02 文件级拆分）。<para>承载各管理器字段与属性、日志 / 宿主生命周期 / 插件管理器字段、DI 服务槽与 Plugins。public API 与拆分前完全一致，仅按关注点拆分文件。</para>
/// </summary>
public partial class Application
{
    /// <summary>
    /// 窗口管理器实例。在 <see cref="SetPlatformApp"/> 时创建。
    /// 对应 Wails v3 Go 版本 application.go 中的 windowManager 字段。
    /// </summary>
    private WindowManager? _windowManager;

    /// <summary>
    /// 对话框管理器实例。在 <see cref="SetPlatformApp"/> 时创建。
    /// 对应 Wails v3 Go 版本 application.go 中的 dialogManager 字段。
    /// </summary>
    private DialogManager? _dialogManager;

    /// <summary>
    /// 屏幕管理器实例。在 <see cref="SetPlatformApp"/> 时创建。
    /// 对应 Wails v3 Go 版本 application.go 中的 screenManager 字段。
    /// </summary>
    private ScreenManager? _screenManager;

    /// <summary>
    /// 剪贴板管理器，由平台特定代码注入。
    /// 对应 Wails v3 Go 版本 application.go 中的 clipboard 字段。
    /// </summary>
    private IClipboardManager? _clipboardManager;

    /// <summary>
    /// 菜单管理器，由平台特定代码注入。
    /// 对应 Wails v3 Go 版本 application.go 中的 menu 字段。
    /// </summary>
    private IMenuManager? _menuManager;

    /// <summary>
    /// 系统托盘管理器，由平台特定代码注入。
    /// 对应 Wails v3 Go 版本 application.go 中的 tray 字段。
    /// </summary>
    private ISystemTrayManager? _systemTrayManager;

    /// <summary>
    /// 快捷键绑定管理器，由平台特定代码注入。
    /// 对应 Wails v3 Go 版本 application.go 中的 keybindings 字段。
    /// </summary>
    private IKeyBindingManager? _keyBindingManager;

    /// <summary>
    /// 浏览器管理器，由平台特定代码注入。
    /// 对应 Wails v3 Go 版本 application.go 中的 browser 字段。
    /// </summary>
    private IBrowserManager? _browserManager;

    /// <summary>
    /// 自启动管理器，由平台特定代码注入。
    /// 对应 Wails v3 Go 版本 application.go 中的 autostart 字段。
    /// </summary>
    private IAutostartManager? _autostartManager;

    /// <summary>
    /// 环境信息管理器，由平台特定代码注入。
    /// 对应 Wails v3 Go 版本 application.go 中的 environment 字段。
    /// </summary>
    private IEnvironmentManager? _environmentManager;

    /// <summary>
    /// 日志记录器字段，由 <see cref="InitializeFromServiceProvider"/> 或显式 <see cref="Logger"/> 属性注入。
    /// 对应 AGENTS.md §1.1.1 技术选型要求：日志统一使用 <c>Microsoft.Extensions.Logging.ILogger&lt;T&gt;</c> 抽象。
    /// 对应 Wails v3 Go 版本 application.go 中的 logger 字段。
    /// </summary>
    private ILogger<Application>? _logger;

    /// <summary>
    /// Host 应用生命周期实例，由 <see cref="InitializeFromServiceProvider"/> 注入。
    /// 用于在 <see cref="Run"/> 和 <see cref="Shutdown"/> 中触发 Started/Stopping/Stopped 事件，
    /// 让用户代码可以通过 <c>Lifetime.ApplicationStarted.Register(...)</c> 接入标准生命周期钩子。
    /// 对应 ASP.NET Core 的 <c>IHostApplicationLifetime</c> 集成。
    /// </summary>
    private IHostApplicationLifetime? _lifetime;

    /// <summary>
    /// 插件管理器实例，由 <see cref="InitializeFromServiceProvider"/> 注入。
    /// 用于在 <see cref="Run"/> 中调用 <see cref="PluginManager.StartupPluginsAsync"/>
    /// 和在 <see cref="Shutdown"/> 中调用 <see cref="PluginManager.ShutdownPluginsAsync"/>。
    /// 对应 Wails v3 Go 版本的 Plugin 生命周期管理和 Tauri v2 的 setup/on_drop 钩子。
    /// </summary>
    private PluginManager? _pluginManager;

    /// <summary>窗口管理器槽。</summary>
    internal WindowManager? WindowManagerSlot { get => _windowManager; set => _windowManager = value; }

    /// <summary>对话框管理器槽。</summary>
    internal DialogManager? DialogManagerSlot { get => _dialogManager; set => _dialogManager = value; }

    /// <summary>屏幕管理器槽。</summary>
    internal ScreenManager? ScreenManagerSlot { get => _screenManager; set => _screenManager = value; }

    /// <summary>插件管理器槽。</summary>
    internal PluginManager? PluginManagerSlot { get => _pluginManager; set => _pluginManager = value; }


    // Bindings/Events 已在上方声明为 DI 注入的 get-only 自动属性（R1-1）

    /// <summary>
    /// 获取插件管理器实例（当通过 <see cref="DesktopApplicationBuilder.Build"/> 初始化时可用）。
    /// 用于访问已注册的插件列表和插件生命周期管理。
    /// 对应主题 B：插件生命周期对齐。
    /// </summary>
    public PluginManager? Plugins => _pluginManager;

    /// <summary>
    /// 获取所有窗口的只读列表。
    /// </summary>
    public IReadOnlyList<WebviewWindow> Windows => _windowManager?.AllWindows ?? Array.Empty<WebviewWindow>();

    /// <summary>
    /// 获取窗口管理器实例，若平台应用未设置则返回 null。
    /// </summary>
    public IWindowManager? WindowManager => _windowManager;

    /// <summary>
    /// 获取对话框管理器实例，若平台应用未设置则返回 null。
    /// </summary>
    public IDialogManager? DialogManager => _dialogManager;

    /// <summary>
    /// 获取屏幕管理器实例，若平台应用未设置则返回 null。
    /// </summary>
    public IScreenManager? ScreenManager => _screenManager;

    /// <summary>
    /// 获取或设置剪贴板管理器。由平台特定代码注入实现。
    /// </summary>
    public IClipboardManager? ClipboardManager
    {
        get => _clipboardManager;
        set => _clipboardManager = value;
    }

    /// <summary>
    /// 获取或设置菜单管理器。由平台特定代码注入实现。
    /// </summary>
    public IMenuManager? MenuManager
    {
        get => _menuManager;
        set => _menuManager = value;
    }

    /// <summary>
    /// 获取或设置系统托盘管理器。由平台特定代码注入实现。
    /// </summary>
    public ISystemTrayManager? SystemTrayManager
    {
        get => _systemTrayManager;
        set => _systemTrayManager = value;
    }

    /// <summary>
    /// 获取或设置快捷键绑定管理器。由平台特定代码注入实现。
    /// </summary>
    public IKeyBindingManager? KeyBindingManager
    {
        get => _keyBindingManager;
        set => _keyBindingManager = value;
    }

    /// <summary>
    /// 获取或设置浏览器管理器。由平台特定代码注入实现。
    /// </summary>
    public IBrowserManager? BrowserManager
    {
        get => _browserManager;
        set => _browserManager = value;
    }

    /// <summary>
    /// 获取或设置自启动管理器。由平台特定代码注入实现。
    /// </summary>
    public IAutostartManager? AutostartManager
    {
        get => _autostartManager;
        set => _autostartManager = value;
    }

    /// <summary>
    /// 获取或设置环境信息管理器。由平台特定代码注入实现。
    /// </summary>
    public IEnvironmentManager? EnvironmentManager
    {
        get => _environmentManager;
        set => _environmentManager = value;
    }

    /// <summary>
    /// 获取环境信息管理器的简短别名，对应 Wails v3 Go 版本 <c>app.Env</c>。
    /// </summary>
    public IEnvironmentManager? Env => _environmentManager;

    /// <summary>
    /// 获取或设置日志记录器。
    /// 对应 AGENTS.md §1.1.1 技术选型要求：日志统一使用 <c>Microsoft.Extensions.Logging.ILogger&lt;T&gt;</c> 抽象。
    /// 对应 Wails v3 Go 版本 application.go 中的 logger 字段。
    /// </summary>
    public ILogger<Application>? Logger
    {
        get => _logger;
        set => _logger = value;
    }

}
