using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Events;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Services;
using Wails.Net.Application.Transport;
using Wails.Net.Application.Windows;
using Wails.Net.Events;
using Wails.Net.Runtime.Js;

namespace Wails.Net.Application;

/// <summary>
/// 核心应用类，对应 Wails v3 Go 版本 application.go 中的 Application 结构。
/// 负责管理应用生命周期、窗口、绑定、事件、传输层、资源服务器及各平台管理器。
/// </summary>
public class Application
{
    private readonly ApplicationOptions _options;
    private IPlatformApp? _platformApp = null;
    private readonly ServiceRegistry _serviceRegistry = new();
    private uint _nextWindowId = 1;
    private readonly Dictionary<uint, WebviewWindow> _windows = new();

    /// <summary>
    /// 配置驱动的窗口列表，由 DesktopApplicationBuilder.Build 传递。
    /// 在 Run() 的 OnAfterStart 之前按需创建，避免主线程创建窗口导致 Win32 线程亲和性问题。
    /// </summary>
    private List<WindowConfig>? _configuredWindows;

    /// <summary>
    /// 默认窗口配置，由 DesktopApplicationBuilder.Build 传递。
    /// 当 _configuredWindows 为空且 OnAfterStart 未设置时使用。
    /// </summary>
    private DesktopHostOptions.WindowOptions? _defaultWindowConfig;

    /// <summary>
    /// 绑定管理器，负责注册和调用绑定方法。
    /// 对应 Wails v3 Go 版本 bindings.go 中的 Bindings 结构。
    /// </summary>
    private readonly BindingManager _bindings = new();

    /// <summary>
    /// 事件处理器，管理事件订阅和发布。
    /// 对应 Wails v3 Go 版本 events.go 中的 EventProcessor。
    /// </summary>
    private readonly EventProcessor _events = new();

    /// <summary>
    /// 传输层实例。
    /// </summary>
    private ITransport? _transport;

    /// <summary>
    /// 消息处理器实例，懒加载。
    /// 用于 <see cref="HandleMessageFromFrontend"/> 解析和处理前端消息。
    /// </summary>
    private MessageProcessor? _messageProcessor;

    /// <summary>
    /// 命令调度器实例，由 <see cref="DesktopApplicationBuilder.Build"/> 注入。
    /// 用于在前端调用方法名未在 <see cref="BindingManager"/> 中找到时回退查找命令。
    /// 对应 Wails v3 中通过 MapCommand 注册的命令路径。
    /// </summary>
    private CommandDispatcher? _commandDispatcher;

    /// <summary>
    /// DI 服务容器，由 <see cref="InitializeFromServiceProvider"/> 注入。
    /// 传递给 <see cref="MessageProcessor"/>，使命令上下文能携带 WindowId 传递到插件命令。
    /// </summary>
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// 资源服务器实例。
    /// </summary>
    private Wails.Net.AssetServer.AssetServer? _assetServer;

    /// <summary>
    /// 待挂载的服务路由列表（P1-6 新增）。
    /// 当服务在 AssetServer 注入前注册且配置了 <see cref="Services.ServiceOptions.Route"/> 时，
    /// 暂存到此处；待 <see cref="AssetServer"/> setter 触发时统一挂载。
    /// </summary>
    private readonly List<(string Route, Wails.Net.AssetServer.IHttpServiceHandler Handler)> _pendingServiceRoutes = new();

    /// <summary>
    /// 自定义 URI scheme 到 AssetServer 的映射。
    /// 通过 <see cref="RegisterScheme"/> 注册，平台 WebviewWindow 在创建时可查询此映射以拦截请求。
    /// </summary>
    private readonly Dictionary<string, Wails.Net.AssetServer.AssetServer> _customSchemes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 用于保护 <see cref="_customSchemes"/> 并发访问的同步对象。
    /// </summary>
    private readonly object _customSchemesLock = new();

    /// <summary>
    /// 是否正在运行。
    /// </summary>
    private volatile bool _isRunning;

    private static Application? _globalApplication;

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

    /// <summary>
    /// 关闭任务列表，包含 <see cref="ApplicationOptions.ShutdownTasks"/> 和通过
    /// <see cref="OnShutdown(Action)"/> 注册的任务。
    /// 对应 Wails v3 Go 版本 application.go 中的 shutdownTasks 字段。
    /// </summary>
    private readonly List<Action> _shutdownTasks = new();

    /// <summary>
    /// 启动前 hook 列表，通过 <see cref="OnBeforeStart(Action{Application})"/> 注册。
    /// 在 <see cref="Run"/> 方法中、实际启动服务之前遍历执行所有 hook。
    /// 对应 Wails v3 Go 版本 application.go 中的 OnBeforeStart hook 机制。
    /// </summary>
    private readonly List<Action<Application>> _onBeforeStartHooks = new();

    /// <summary>
    /// 是否为首实例（单实例模式下）。
    /// 对应 Wails v3 Go 版本 application.go 中的 isFirstInstance 字段。
    /// </summary>
    private bool _isFirstInstance = true;

    /// <summary>
    /// 第二个实例启动时调用的回调列表。
    /// 由 <see cref="OnSecondInstanceLaunch"/> 注册，由 <see cref="RaiseSecondInstanceLaunched"/> 触发。
    /// 对应 Wails v3 Go 版本 application.go 中的 OnSecondInstanceLaunch hook 机制。
    /// </summary>
    private Action<string[]>? _onSecondInstanceLaunch;

    /// <summary>
    /// 应用级取消令牌源，在 <see cref="Shutdown"/> 时取消。
    /// 对应 Wails v3 Go 版本 application.go 中的 context 取消机制。
    /// </summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 获取应用选项。
    /// </summary>
    public ApplicationOptions Options => _options;

    /// <summary>
    /// 获取平台应用实例。
    /// </summary>
    public IPlatformApp? PlatformApp => _platformApp;

    /// <summary>
    /// 设置平台应用实例。由平台特定的扩展方法调用。
    /// 同时创建依赖平台应用的管理器实例。
    /// </summary>
    /// <param name="platformApp">平台应用实例。</param>
    public void SetPlatformApp(IPlatformApp platformApp)
    {
        _platformApp = platformApp;
        _windowManager = new WindowManager(platformApp);
        _dialogManager = new DialogManager(platformApp);
        _screenManager = new ScreenManager(platformApp);
    }

    /// <summary>
    /// 从 DI 容器初始化管理器。
    /// 尝试从 DI 获取已注册的管理器实例，替换 Application 内部的管理器。
    /// 注意：<see cref="_events"/> 和 <see cref="_bindings"/> 为只读字段，无法从 DI 替换，
    /// 保留 Application 创建的实例；但可对 <see cref="_events"/> 设置传输层监听器。
    /// </summary>
    /// <param name="serviceProvider">DI 容器。</param>
    internal void InitializeFromServiceProvider(IServiceProvider serviceProvider)
    {
        // 保存 DI 容器引用，供 MessageProcessor 创建带 WindowId 的命令上下文
        _serviceProvider = serviceProvider;

        // 自动从 DI 容器获取 ILogger<Application>，若用户未显式设置则使用 DI 注册的实例。
        // 对应 AGENTS.md §1.1.1 技术选型：日志统一使用 Microsoft.Extensions.Logging 抽象。
        if (_logger is null)
        {
            _logger = serviceProvider.GetService<ILogger<Application>>();
        }

        // 自动从 DI 容器获取 IHostApplicationLifetime，用于在 Run/Shutdown 中触发标准生命周期事件。
        _lifetime = serviceProvider.GetService<IHostApplicationLifetime>();

        // 平台依赖的管理器（非只读字段，可从 DI 替换）
        var windowManager = serviceProvider.GetService<WindowManager>();
        if (windowManager is not null)
        {
            _windowManager = windowManager;
        }

        var dialogManager = serviceProvider.GetService<DialogManager>();
        if (dialogManager is not null)
        {
            _dialogManager = dialogManager;
        }

        var screenManager = serviceProvider.GetService<ScreenManager>();
        if (screenManager is not null)
        {
            _screenManager = screenManager;
        }

        // 通过公共 Setter 注入的管理器（由平台特定代码注册到 DI）
        var clipboardManager = serviceProvider.GetService<IClipboardManager>();
        if (clipboardManager is not null)
        {
            ClipboardManager = clipboardManager;
        }

        var menuManager = serviceProvider.GetService<IMenuManager>();
        if (menuManager is not null)
        {
            MenuManager = menuManager;
        }

        var systemTrayManager = serviceProvider.GetService<ISystemTrayManager>();
        if (systemTrayManager is not null)
        {
            SystemTrayManager = systemTrayManager;
        }

        var keyBindingManager = serviceProvider.GetService<IKeyBindingManager>();
        if (keyBindingManager is not null)
        {
            KeyBindingManager = keyBindingManager;
        }

        var browserManager = serviceProvider.GetService<IBrowserManager>();
        if (browserManager is not null)
        {
            BrowserManager = browserManager;
        }

        var autostartManager = serviceProvider.GetService<IAutostartManager>();
        if (autostartManager is not null)
        {
            AutostartManager = autostartManager;
        }

        var environmentManager = serviceProvider.GetService<IEnvironmentManager>();
        if (environmentManager is not null)
        {
            EnvironmentManager = environmentManager;
        }

        // 连接传输层事件监听器到事件处理器。
        // 对应 Wails v3 Go 版本 application.go 中的 wailsEventListeners 列表追加机制：
        //   - 若主 Transport 实现了 IWailsEventListener（如 HttpTransport/WebSocketTransport），追加到列表。
        //   - **永远追加 EventIPCTransport 作为兜底**，确保桌面端 webview 通过 ExecJS 直接收到事件，
        //     即使主 Transport 的 WebSocket 通道无客户端连接。
        //   - 这对应 Wails v3 中 EventIPCTransport 的自动追加逻辑：
        //     if _, ok := transport.(WailsEventListener); !ok {
        //         result.wailsEventListeners = append(result.wailsEventListeners, &EventIPCTransport{app: result})
        //     }
        //   - Wails.Net 更激进：永远追加，因为 HttpTransport 实现了 IWailsEventListener
        //     但其内部依赖 WebSocketBroadcaster，无 WS 客户端时事件丢失。EventIPCTransport 通过
        //     ExecJS 注入可弥补此缺陷，与主 Transport 同时工作不冲突。
        var listener = serviceProvider.GetService<IWailsEventListener>();
        if (listener is not null)
        {
            _events.AddWailsEventListener(listener);
        }

        // 始终追加 EventIPCTransport 作为兜底，确保桌面端事件必达。
        // P0-A1：EventIPCTransport 兜底机制对齐 Wails v3。
        // P0-D：注入 ILogger<EventIPCTransport> 以便记录事件派发失败日志，
        // 支持结构化日志诊断。NativeIpcTransport 协作由 NotifyEvent 内部完成。
        var eventIpcLogger = serviceProvider.GetService<ILogger<EventIPCTransport>>();
        _events.AddWailsEventListener(new EventIPCTransport(eventIpcLogger!));

        // 从 DI 容器获取 PluginManager 并收集已注册的插件实例。
        // 对应主题 B：插件生命周期对齐。PluginManager 由 UsePlugin 注册为单例，
        // RegisterFromServices 从 IEnumerable<IPlugin> 收集所有插件实例。
        _pluginManager = serviceProvider.GetService<PluginManager>();
        _pluginManager?.RegisterFromServices();
    }

    /// <summary>
    /// 获取服务注册表。
    /// </summary>
    public ServiceRegistry Services => _serviceRegistry;

    /// <summary>
    /// 获取绑定管理器。
    /// </summary>
    public BindingManager Bindings => _bindings;

    /// <summary>
    /// 获取事件处理器。
    /// </summary>
    public EventProcessor Events => _events;

    /// <summary>
    /// 获取插件管理器实例（当通过 <see cref="DesktopApplicationBuilder.Build"/> 初始化时可用）。
    /// 用于访问已注册的插件列表和插件生命周期管理。
    /// 对应主题 B：插件生命周期对齐。
    /// </summary>
    public PluginManager? Plugins => _pluginManager;

    /// <summary>
    /// 获取或设置传输层实例。
    /// </summary>
    public ITransport? Transport
    {
        get => _transport;
        set => _transport = value;
    }

    /// <summary>
    /// 获取或设置命令调度器实例。
    /// 由 <see cref="Hosting.DesktopApplicationBuilder.Build"/> 在构建完成后注入。
    /// 用于前端调用方法名未在 <see cref="BindingManager"/> 中找到时回退查找命令。
    /// </summary>
    public CommandDispatcher? CommandDispatcher
    {
        get => _commandDispatcher;
        internal set => _commandDispatcher = value;
    }

    /// <summary>
    /// 获取或设置资源服务器实例。
    /// <para>
    /// Setter 触发时会自动将待挂载的服务路由（在 AssetServer 注入前通过
    /// <see cref="RegisterService(object, Services.ServiceOptions)"/> 注册的服务）应用到新设置的 AssetServer。
    /// </para>
    /// </summary>
    public Wails.Net.AssetServer.AssetServer? AssetServer
    {
        get => _assetServer;
        set
        {
            _assetServer = value;
            if (value is not null && _pendingServiceRoutes.Count > 0)
            {
                foreach (var (route, handler) in _pendingServiceRoutes)
                {
                    value.MountServiceRoute(route, handler);
                }
                _pendingServiceRoutes.Clear();
            }
        }
    }

    /// <summary>
    /// 为指定窗口注册 CSP 到 <see cref="AssetServer"/>（P0-4 新增，对应 Tauri v2 per-window CSP）。
    /// 仅当 <paramref name="csp"/> 非空且 <c>Enabled=true</c> 时注册。
    /// 调用方应在创建窗口前调用此方法，确保资源请求拦截时 CSP 已就绪。
    /// </summary>
    /// <param name="windowName">窗口名称；为 null 或空时不注册（回退到全局 CSP）。</param>
    /// <param name="csp">窗口级 CSP 配置；为 null 时不注册。</param>
    internal void RegisterWindowCsp(string? windowName, Wails.Net.Application.Security.CspOptions? csp)
    {
        if (string.IsNullOrEmpty(windowName) || csp is null || !csp.Enabled || _assetServer is null)
        {
            return;
        }

        _assetServer.SetCspHeaderForWindow(windowName, csp.BuildHeader());
    }

    /// <summary>
    /// 注册自定义 URI scheme，将指定 scheme 的请求路由到提供的 <see cref="Wails.Net.AssetServer.AssetServer"/>。
    /// 对应 Tauri v2 的自定义协议（asset protocol）功能。
    /// 注册后，平台 WebviewWindow 在创建时会查询此映射并设置请求拦截。
    /// </summary>
    /// <param name="scheme">URI scheme 名称（不含 "://"，如 "myapp"）。</param>
    /// <param name="assetServer">处理该 scheme 请求的资源服务器。若为 null，则使用 <see cref="AssetServer"/>。</param>
    /// <exception cref="ArgumentNullException">scheme 为 null。</exception>
    /// <exception cref="ArgumentException">scheme 为空字符串或仅包含空白字符。</exception>
    public void RegisterScheme(string scheme, Wails.Net.AssetServer.AssetServer? assetServer = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(scheme);
        lock (_customSchemesLock)
        {
            _customSchemes[scheme] = assetServer ?? _assetServer
                ?? throw new InvalidOperationException(
                    "RegisterScheme 需要提供 assetServer，或在 Application.AssetServer 已设置后调用。");
        }
    }

    /// <summary>
    /// 取消注册指定 URI scheme。
    /// </summary>
    /// <param name="scheme">要取消注册的 scheme 名称。</param>
    /// <returns>是否成功移除；若 scheme 未注册则返回 false。</returns>
    public bool UnregisterScheme(string scheme)
    {
        if (string.IsNullOrEmpty(scheme))
        {
            return false;
        }

        lock (_customSchemesLock)
        {
            return _customSchemes.Remove(scheme);
        }
    }

    /// <summary>
    /// 查询指定 URI scheme 是否已注册。
    /// </summary>
    /// <param name="scheme">scheme 名称。</param>
    /// <returns>若已注册返回 true；否则返回 false。</returns>
    public bool IsSchemeRegistered(string scheme)
    {
        if (string.IsNullOrEmpty(scheme))
        {
            return false;
        }

        lock (_customSchemesLock)
        {
            return _customSchemes.ContainsKey(scheme);
        }
    }

    /// <summary>
    /// 尝试获取指定 URI scheme 关联的 <see cref="Wails.Net.AssetServer.AssetServer"/>。
    /// 供平台 WebviewWindow 实现在创建时查询已注册的 scheme 以设置请求拦截。
    /// </summary>
    /// <param name="scheme">scheme 名称。</param>
    /// <param name="assetServer">查找到的资源服务器；若未注册则为 null。</param>
    /// <returns>是否找到已注册的 scheme。</returns>
    public bool TryGetSchemeAssetServer(string scheme, out Wails.Net.AssetServer.AssetServer? assetServer)
    {
        if (string.IsNullOrEmpty(scheme))
        {
            assetServer = null;
            return false;
        }

        lock (_customSchemesLock)
        {
            return _customSchemes.TryGetValue(scheme, out assetServer);
        }
    }

    /// <summary>
    /// 获取所有已注册的自定义 URI scheme 名称的只读列表。
    /// </summary>
    public IReadOnlyList<string> RegisteredSchemes
    {
        get
        {
            lock (_customSchemesLock)
            {
                return _customSchemes.Keys.ToList().AsReadOnly();
            }
        }
    }

    /// <summary>
    /// 获取所有窗口的只读列表。
    /// </summary>
    public IReadOnlyList<WebviewWindow> Windows => _windows.Values.ToList().AsReadOnly();

    /// <summary>
    /// 获取是否正在运行。
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 获取窗口管理器实例，若平台应用未设置则返回 null。
    /// </summary>
    public IWindowManager? WindowManager => _windowManager;

    /// <summary>
    /// 获取原生 IPC 传输层实例（P0-C2），若未启用则返回 null。
    /// <para>
    /// 默认情况下（<see cref="UseNativeIpc"/> = <c>true</c>）应用启动时构造此实例，
    /// 平台 <c>IPlatformApp.CreateWebviewWindow</c> 在创建窗口后调用
    /// <see cref="INativeIpcTransport.RegisterWindow"/> 注册窗口。
    /// </para>
    /// <para>
    /// 启用后，前端小消息（&lt; 512KB）通过 WebView 原生 postMessage 通道传输，
    /// 替代 HTTP fetch 路径以降低延迟；大消息仍走 HTTP 分块上传。
    /// </para>
    /// </summary>
    public INativeIpcTransport? NativeIpcTransport { get; private set; }

    /// <summary>
    /// 获取或设置是否启用原生 IPC 传输通道（P0-C2）。
    /// <para>
    /// 默认为 <c>true</c>：桌面端默认启用原生 postMessage 副通道，降低小消息延迟。
    /// 设置为 <c>false</c> 可禁用此通道（如纯 Server 模式或无 WebView 的容器化部署）。
    /// </para>
    /// <para>
    /// 启用时：
    /// <list type="bullet">
    /// <item>应用启动时构造 <see cref="INativeIpcTransport"/> 实例。</item>
    /// <item>替换 <see cref="EventIPCTransport"/> 作为事件广播通道。</item>
    /// <item>前端运行时自动探测原生通道并优先使用。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 必须在应用启动前（<see cref="Run"/> 调用前）设置。
    /// </para>
    /// </summary>
    public bool UseNativeIpc { get; set; } = true;

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

    /// <summary>
    /// 获取是否启用单实例模式。
    /// 对应 Wails v3 Go 版本 application.go 中 SingleInstance 选项。
    /// </summary>
    public bool IsSingleInstance => _options.SingleInstance;

    /// <summary>
    /// 获取当前进程是否为首实例。
    /// 仅在 <see cref="IsSingleInstance"/> 为 true 且 <see cref="SetupSingleInstance"/> 已调用时有意义。
    /// </summary>
    public bool IsFirstInstance => _isFirstInstance;

    /// <summary>
    /// 获取应用级取消令牌，在应用关闭时被取消。
    /// 对应 Wails v3 Go 版本 application.go 中的 context.Context。
    /// </summary>
    public CancellationToken ApplicationCancellationToken => _cts.Token;

    /// <summary>
    /// 获取全局应用实例。
    /// </summary>
    /// <returns>全局应用实例，若未创建则返回 null。</returns>
    public static Application? Get() => _globalApplication;

    /// <summary>
    /// 使用指定选项构造应用实例。
    /// 将 <see cref="ApplicationOptions.ShutdownTasks"/> 中的任务合并到内部关闭任务列表。
    /// </summary>
    /// <param name="options">应用选项。</param>
    public Application(ApplicationOptions options)
    {
        _options = options;
        _globalApplication = this;
        _shutdownTasks.AddRange(options.ShutdownTasks);
    }

    /// <summary>
    /// 注册服务。同时将服务的公共方法注册到绑定管理器。
    /// </summary>
    /// <param name="service">要注册的服务实例。</param>
    public virtual void RegisterService(object service)
    {
        RegisterService(service, ServiceOptions.Default);
    }

    /// <summary>
    /// 注册服务并附带选项。同时将服务的公共方法注册到绑定管理器。
    /// <para>
    /// 对应 Wails v3 Go 版本 <c>NewServiceWithOptions</c> 函数。
    /// 当 <paramref name="options"/>.<see cref="ServiceOptions.Route"/> 非空且
    /// <paramref name="service"/> 实现 <see cref="Wails.Net.AssetServer.IHttpServiceHandler"/> 时，
    /// 自动将服务挂载到 <see cref="AssetServer"/> 此前缀下。
    /// </para>
    /// <para>
    /// 若 <see cref="AssetServer"/> 尚未注入，路由信息将暂存到待挂载列表，
    /// 待 AssetServer 注入时（通过 setter）自动应用。
    /// </para>
    /// </summary>
    /// <param name="service">要注册的服务实例。</param>
    /// <param name="options">服务选项；为 null 时使用 <see cref="ServiceOptions.Default"/>。</param>
    public virtual void RegisterService(object service, ServiceOptions? options)
    {
        ArgumentNullException.ThrowIfNull(service);
        var opts = options ?? ServiceOptions.Default;

        _serviceRegistry.Register(service, opts);
        _bindings.RegisterInstance(service);

        // 若服务实现 IHttpServiceHandler 且配置了 Route，挂载到 AssetServer
        if (service is Wails.Net.AssetServer.IHttpServiceHandler handler &&
            !string.IsNullOrWhiteSpace(opts.Route))
        {
            if (_assetServer is not null)
            {
                _assetServer.MountServiceRoute(opts.Route!, handler);
            }
            else
            {
                // AssetServer 尚未注入，暂存待后续 setter 触发时应用
                _pendingServiceRoutes.Add((opts.Route!, handler));
            }
        }
    }

    /// <summary>
    /// 从 DI 容器获取指定类型的服务实例，并注册到绑定管理器。
    /// 对应 ASP.NET Core 风格的 DI 集成，避免双重注册（DI + RegisterService）。
    ///
    /// 使用示例：
    /// <code>
    /// builder.Services.AddSingleton&lt;GreetingService&gt;();
    /// // ...
    /// var app = builder.Build().Application;
    /// app.RegisterBindings&lt;GreetingService&gt;();  // 从 DI 获取实例
    /// </code>
    /// </summary>
    /// <typeparam name="T">服务类型，必须已注册到 DI 容器。</typeparam>
    /// <returns>从 DI 容器获取的服务实例。</returns>
    /// <exception cref="InvalidOperationException">
    /// 当 <see cref="InitializeFromServiceProvider"/> 未被调用或 DI 容器中未注册 <typeparamref name="T"/> 时抛出。
    /// </exception>
    public virtual T RegisterBindings<T>() where T : class
    {
        if (_serviceProvider is null)
        {
            throw new InvalidOperationException(
                "Application 尚未从 DI 容器初始化。请先调用 DesktopApplicationBuilder.Build()。");
        }

        var service = _serviceProvider.GetRequiredService<T>();
        _bindings.RegisterInstance(service);
        return service;
    }

    /// <summary>
    /// 设置配置驱动的窗口配置，由 DesktopApplicationBuilder.Build 调用。
    /// 窗口不在 Build（主线程）创建，而是延迟到 Run()（UI 线程）按需创建，
    /// 避免 Win32 窗口线程亲和性导致的死锁。
    /// </summary>
    /// <param name="configuredWindows">多窗口配置列表，可为 null。</param>
    /// <param name="defaultWindow">默认窗口配置，可为 null。</param>
    internal void SetWindowConfigs(
        List<WindowConfig>? configuredWindows,
        DesktopHostOptions.WindowOptions? defaultWindow)
    {
        _configuredWindows = configuredWindows;
        _defaultWindowConfig = defaultWindow;
    }

    /// <summary>
    /// 根据配置创建窗口。在 UI 线程的 Run() 方法中调用，
    /// 确保窗口由 STA UI 线程创建，可被该线程的 GetMessage 循环正确处理。
    /// 仅当 OnAfterStart 未设置时执行，避免与用户回调重复创建窗口。
    /// </summary>
    private void CreateWindowsFromConfig()
    {
        if (_configuredWindows is { Count: > 0 } windows)
        {
            foreach (var cfg in windows)
            {
                CreateWebviewWindow(new WebviewWindowOptions
                {
                    Name = cfg.Name ?? string.Empty,
                    Title = cfg.Title ?? "Wails.Net",
                    Width = cfg.Width,
                    Height = cfg.Height,
                    Resizable = cfg.Resizable,
                    Centered = cfg.Centered,
                    Fullscreen = cfg.Fullscreen,
                    Frameless = cfg.Frameless,
                    AlwaysOnTop = cfg.AlwaysOnTop,
                    URL = cfg.Url ?? string.Empty,
                });
            }
        }
        else if (_defaultWindowConfig is not null)
        {
            var window = _defaultWindowConfig;
            CreateWebviewWindow(new WebviewWindowOptions
            {
                Title = window.Title,
                Width = window.Width,
                Height = window.Height,
                Frameless = window.Frameless,
            });
        }
    }

    /// <summary>
    /// 创建 Webview 窗口，使用自动递增的 ID。
    /// 若平台应用已设置，则委托给平台应用创建实际窗口。
    /// </summary>
    /// <param name="options">窗口选项。</param>
    /// <returns>新创建的窗口实例。</returns>
    public virtual WebviewWindow CreateWebviewWindow(WebviewWindowOptions options)
    {
        var id = _nextWindowId++;
        var window = new WebviewWindow(id, options.Name, options);
        _windows[id] = window;

        // P0-4：注册窗口级 CSP 到 AssetServer（在平台创建窗口前完成，确保资源请求拦截时 CSP 已就绪）
        RegisterWindowCsp(options.Name, options.Csp);

        _platformApp?.CreateWebviewWindow(id, options);

        return window;
    }

    /// <summary>
    /// 根据 ID 获取窗口。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <returns>匹配的窗口实例，若不存在则返回 null。</returns>
    public virtual WebviewWindow? GetWindow(uint id)
    {
        return _windows.TryGetValue(id, out var window) ? window : null;
    }

    /// <summary>
    /// 根据名称获取窗口。
    /// </summary>
    /// <param name="name">窗口名称。</param>
    /// <returns>匹配的窗口实例，若不存在则返回 null。</returns>
    public virtual WebviewWindow? GetWindowByName(string name)
    {
        return _windows.Values.FirstOrDefault(w => w.Name == name);
    }

    /// <summary>
    /// 销毁窗口，从窗口字典中移除并关闭其平台实现。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    public virtual void DestroyWindow(uint id)
    {
        if (_windows.TryGetValue(id, out var window))
        {
            _windows.Remove(id);
            window.Close();
        }
    }

    /// <summary>
    /// 设置单实例锁。若启用单实例模式且当前进程非首实例，则直接退出。
    /// 对应 Wails v3 Go 版本 application.go 中 SingleInstanceLock 相关逻辑。
    /// </summary>
    private void SetupSingleInstance()
    {
        if (!_options.SingleInstance)
        {
            return;
        }

        var uniqueId = _options.SingleInstanceUniqueID ?? _options.Name;
        if (_platformApp is not null)
        {
            _isFirstInstance = _platformApp.AcquireSingleInstanceLock(uniqueId);
        }

        if (!_isFirstInstance)
        {
            // 非首实例：通知已运行的前一个实例后退出
            var args = Environment.GetCommandLineArgs();
            _platformApp?.NotifySingleInstance(args);
            Environment.Exit(_options.SingleInstanceExitCode);
        }
    }

    /// <summary>
    /// 初始化原生 IPC 传输层（P0-C2）。
    /// <para>
    /// 仅当 <see cref="UseNativeIpc"/> 为 <c>true</c> 时执行：
    /// <list type="number">
    /// <item>确保 <see cref="MessageProcessor"/> 已构造（懒初始化）。</item>
    /// <item>构造 <see cref="NativeIpcTransport"/> 实例并保存到 <see cref="NativeIpcTransport"/> 属性。</item>
    /// <item>注册为事件监听器，替代 <see cref="EventIPCTransport"/> 作为事件广播通道。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 之后平台 <c>IPlatformApp.CreateWebviewWindow</c> 在创建窗口后会调用
    /// <see cref="INativeIpcTransport.RegisterWindow"/> 完成窗口级回调注册。
    /// </para>
    /// </summary>
    private void InitializeNativeIpcTransport()
    {
        if (!UseNativeIpc)
        {
            return;
        }

        // 确保 MessageProcessor 已构造（NativeIpcTransport 需要它来路由消息）
        _messageProcessor ??= new MessageProcessor(_bindings, _events, id => GetWindow(id), _commandDispatcher, _serviceProvider);

        NativeIpcTransport = new NativeIpcTransport(_messageProcessor);
        _events.AddWailsEventListener(NativeIpcTransport);
    }

    /// <summary>
    /// 运行应用主循环。
    /// 执行单实例检查、启动服务（按注册顺序）、启动资源服务器、启动传输层，
    /// 然后进入平台主循环（阻塞）。主循环退出后执行清理。
    /// 对应 Wails v3 Go 版本 application.go 中的 Run 方法。
    /// </summary>
    public virtual void Run()
    {
        if (_isRunning)
        {
            return;
        }

        // 单实例检查
        SetupSingleInstance();

        _isRunning = true;

        // 触发启动回调
        _options.OnBeforeStart?.Invoke();

        // 执行所有通过 OnBeforeStart 注册的 hook，在启动服务之前运行。
        foreach (var hook in _onBeforeStartHooks)
        {
            hook(this);
        }

        // 按注册顺序启动服务
        foreach (var service in _serviceRegistry.Services)
        {
            if (service is IServiceStartup startup)
            {
                startup.ServiceStartup(_options, _cts.Token).GetAwaiter().GetResult();
            }
        }

        // 启动资源服务器（若实现了 IServiceStartup）
        if (_assetServer is IServiceStartup assetStartup)
        {
            assetStartup.ServiceStartup(_options, _cts.Token).GetAwaiter().GetResult();
        }

        // 启动传输层
        if (_transport is not null)
        {
            _transport.StartAsync(_cts.Token).GetAwaiter().GetResult();
        }

        // P0-C2：初始化原生 IPC 传输层（若启用）。
        // 必须在传输层启动后、窗口创建前完成，以便平台 CreateWebviewWindow 能注册窗口。
        InitializeNativeIpcTransport();

        // 触发 OnStartup 回调
        _options.OnStartup?.Invoke();

        // 仅当未设置 OnAfterStart 时，根据配置创建窗口。
        // 若用户在 OnAfterStart 中手动创建窗口，则跳过配置驱动创建，避免双重窗口。
        // 窗口在此处（UI 线程）创建，确保 Win32 窗口线程亲和性正确。
        if (_options.OnAfterStart is null)
        {
            CreateWindowsFromConfig();
        }

        // 触发 OnAfterStart 回调
        _options.OnAfterStart?.Invoke();

        // 启动所有插件（调用 IPlugin.StartupAsync）。
        // 对应 Wails v3 的 Startup() 和 Tauri v2 的 setup() 钩子。
        // 在 OnAfterStart 之后、平台主循环之前调用，确保插件资源在窗口显示前就绪。
        _pluginManager?.StartupPluginsAsync(_cts.Token).GetAwaiter().GetResult();

        // 注：IHostApplicationLifetime.ApplicationStarted 事件由宿主基础设施触发，
        // 此处不手动调用。当 Application 由 DesktopHostedService 驱动时，
        // host.StartAsync() 会自动触发 ApplicationStarted；
        // 当 Application 独立运行（无 Host）时，_lifetime 为 null。

        // 进入平台主循环（阻塞直到退出）
        if (_platformApp is not null)
        {
            _platformApp.Run();
        }

        // 平台主循环退出后清理
        Shutdown();
    }

    /// <summary>
    /// 关闭应用。
    /// 执行关闭任务、OnShutdown 回调、取消取消令牌、关闭所有窗口、
    /// 停止传输层、停止资源服务器、按逆序关闭服务。
    /// 对应 Wails v3 Go 版本 application.go 中的 Shutdown 方法。
    /// </summary>
    public virtual void Shutdown()
    {
        if (!_isRunning)
        {
            return;
        }

        // 触发 IHostApplicationLifetime.ApplicationStopping 事件。
        // 对应 ASP.NET Core 的优雅关闭钩子，让用户代码可以注册 Stopping 回调。
        _lifetime?.StopApplication();

        // 取消应用级取消令牌
        _cts.Cancel();

        // 执行关闭任务（包括 options 中的和 OnShutdown 注册的）
        foreach (var task in _shutdownTasks)
        {
            try
            {
                task();
            }
            catch
            {
                // 关闭任务中的异常不应中断关闭流程
            }
        }

        // 触发 OnShutdown 回调
        _options.OnShutdown?.Invoke();

        // 关闭所有窗口
        foreach (var window in _windows.Values.ToList())
        {
            try
            {
                window.Close();
            }
            catch
            {
                // 关闭窗口时的异常不应中断关闭流程
            }
        }
        _windows.Clear();

        // 停止传输层
        if (_transport is not null)
        {
            _transport.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        // 停止资源服务器（若实现了 IServiceShutdown）
        if (_assetServer is IServiceShutdown assetShutdown)
        {
            assetShutdown.ServiceShutdown(CancellationToken.None).GetAwaiter().GetResult();
        }

        // 关闭所有插件（调用 IPlugin.ShutdownAsync，按注册逆序）。
        // 对应 Wails v3 的 Shutdown() 和 Tauri v2 的 on_drop 钩子。
        // 在服务逆序关闭之前调用，确保插件能在服务仍然可用时释放资源。
        _pluginManager?.ShutdownPluginsAsync(CancellationToken.None).GetAwaiter().GetResult();

        // 逆序关闭服务
        var services = _serviceRegistry.Services.ToList();
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i] is IServiceShutdown shutdown)
            {
                shutdown.ServiceShutdown(CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        _platformApp?.Destroy();
        _isRunning = false;

        // 注：IHostApplicationLifetime.ApplicationStopped 事件由 StopApplication() 内部触发，
        // 此处不再手动调用。StopApplication() 会先触发 ApplicationStopping，
        // 然后在所有 hosted service 停止后触发 ApplicationStopped。

        // P1-7：在所有清理完成后触发 PostShutdown 回调。
        // 对应 Wails v3 Go 版本 application.go 中 cleanup() 末尾的 PostShutdown 调用：
        //   a.postQuit()
        //   if a.options.PostShutdown != nil { a.options.PostShutdown() }
        // 与 OnShutdown 的区别：此时所有资源（窗口/服务/传输/平台）均已释放，
        // 适合执行刷新日志、释放外部资源、上报关闭指标等收尾工作。
        // 异常被吞掉以避免影响已完成的关闭流程。
        try
        {
            _options.PostShutdown?.Invoke();
        }
        catch
        {
            // PostShutdown 中的异常不应中断已完成的关闭流程
        }
    }

    /// <summary>
    /// 退出应用，触发关闭流程。
    /// 对应 Wails v3 Go 版本 application.go 中的 Quit 方法。
    /// </summary>
    /// <remarks>
    /// 注意：此方法为用户主动退出，<c>不</c>会调用 <see cref="ShouldQuit"/> 回调。
    /// <see cref="ShouldQuit"/> 仅由平台信号处理器在收到系统级退出信号时调用
    /// （如最后一个窗口关闭、系统关机、Ctrl+C）。
    /// </remarks>
    public virtual void Quit()
    {
        Shutdown();
    }

    /// <summary>
    /// 判断应用是否应该退出。对应 Wails v3 Go 版本 <c>(a *App) shouldQuit() bool</c> 方法。
    /// <para>
    /// 由平台信号处理器在收到退出信号时调用。若返回 false，平台应取消退出流程，应用继续运行。
    /// 默认行为（<see cref="ApplicationOptions.ShouldQuit"/> 为 null 时）：始终返回 true。
    /// </para>
    /// <para>
    /// 典型用法：在窗口关闭事件、系统退出事件中调用此方法：
    /// <code>
    /// if (!Application.Get().ShouldQuit())
    /// {
    ///     // 用户取消退出，阻止窗口关闭
    ///     e.Cancel = true;
    /// }
    /// </code>
    /// </para>
    /// </summary>
    /// <returns>若应用应退出返回 true；否则返回 false。</returns>
    public virtual bool ShouldQuit()
    {
        return _options.ShouldQuit?.Invoke() ?? true;
    }

    /// <summary>
    /// 注册关闭时执行的任务。
    /// 对应 Wails v3 Go 版本 application.go 中的 OnShutdown 方法。
    /// </summary>
    /// <param name="task">关闭时执行的操作。</param>
    public void OnShutdown(Action task)
    {
        _shutdownTasks.Add(task);
    }

    /// <summary>
    /// 注册启动前执行的 hook。
    /// hook 将在 <see cref="Run"/> 方法中、实际启动服务和传输层之前按注册顺序执行，
    /// 接收当前 <see cref="Application"/> 实例作为参数。
    /// 对应 Wails v3 Go 版本 application.go 中的 OnBeforeStart hook 机制。
    /// </summary>
    /// <param name="hook">启动前执行的操作，参数为当前应用实例。</param>
    public void OnBeforeStart(Action<Application> hook)
    {
        _onBeforeStartHooks.Add(hook);
    }

    /// <summary>
    /// 获取当前活动窗口的 ID。委托给平台应用 <see cref="IPlatformApp.GetCurrentWindowId"/>。
    /// 对应 Wails v3 Go 版本 application.go 中的 GetCurrentWindowID 方法。
    /// </summary>
    /// <returns>当前活动窗口的 ID；若平台应用未设置则返回 0。</returns>
    public uint GetCurrentWindowID()
    {
        return _platformApp?.GetCurrentWindowId() ?? 0u;
    }

    /// <summary>
    /// 将应用主窗口的父窗口设置为指定平台原生句柄。
    /// 主要用于将应用嵌入到外部宿主进程（如插件宿主、IDE 面板等）。
    /// 委托给平台应用 <see cref="IPlatformApp.SetParent"/>；不支持的平台为 no-op。
    /// 对应 Wails v3 Go 版本 application.go 中的 SetParent 方法。
    /// </summary>
    /// <param name="parent">父窗口的平台原生句柄（如 Win32 HWND）。</param>
    public void SetParent(IntPtr parent)
    {
        _platformApp?.SetParent(parent);
    }

    /// <summary>
    /// 注册第二个实例启动时的回调。当启用单实例模式且有新进程尝试启动时，
    /// 已运行的首实例会通过此回调收到新实例的命令行参数。
    /// 对应 Wails v3 Go 版本 application.go 中的 OnSecondInstanceLaunch hook 机制。
    /// </summary>
    /// <param name="callback">回调函数，参数为新实例启动时的命令行参数。</param>
    public void OnSecondInstanceLaunch(Action<string[]> callback)
    {
        _onSecondInstanceLaunch = callback;
    }

    /// <summary>
    /// 触发 <see cref="KnownEvents.SecondInstanceLaunched"/> 事件并调用通过
    /// <see cref="OnSecondInstanceLaunch"/> 注册的回调。
    /// 由平台特定代码在检测到第二个实例启动时调用，替代直接 Emit 事件的分散式入口，
    /// 统一处理事件分发与用户回调。
    /// </summary>
    /// <param name="args">新实例启动时的命令行参数。</param>
    public void RaiseSecondInstanceLaunched(string[] args)
    {
        _events.Emit(KnownEvents.SecondInstanceLaunched, args, null);
        try
        {
            _onSecondInstanceLaunch?.Invoke(args);
        }
        catch
        {
            // 用户回调中的异常不应中断平台事件分发流程
        }
    }

    /// <summary>
    /// 报告错误，调用配置的错误处理器。
    /// 对应 Wails v3 Go 版本 application.go 中的 error 方法。
    /// </summary>
    /// <param name="ex">异常实例。</param>
    public void Error(Exception ex)
    {
        _options.ErrorHandler?.Invoke(ex);
    }

    /// <summary>
    /// 报告警告，调用配置的警告处理器。
    /// 对应 Wails v3 Go 版本 application.go 中的 warning 方法。
    /// </summary>
    /// <param name="message">警告消息。</param>
    public void Warning(string message)
    {
        _options.WarningHandler?.Invoke(message);
    }

    /// <summary>
    /// 报告致命错误，调用配置的致命错误处理器。
    /// 对应 Wails v3 Go 版本 application.go 中的 fatalError 方法。
    /// </summary>
    /// <param name="ex">异常实例。</param>
    public void Fatal(Exception ex)
    {
        _options.FatalErrorHandler?.Invoke(ex);
    }

    /// <summary>
    /// 处理平台级事件，将事件 ID 转换为事件名并通过事件处理器分发。
    /// 对应 Wails v3 Go 版本 application.go 中的 handlePlatformEvent 方法。
    /// <para>
    /// 同时走两条分发路径：
    /// <list type="bullet">
    /// <item>自定义事件路径（<see cref="EventProcessor.Emit"/>）：按事件名广播到前端传输层和本地订阅者。</item>
    /// <item>应用事件路径（<see cref="EventProcessor.EmitApplicationEvent"/>）：触发
    /// <see cref="EventProcessor.OnApplicationEvent"/> 监听器和
    /// <see cref="EventProcessor.RegisterApplicationEventHook"/> 钩子。
    /// 仅当 <paramref name="eventId"/> 属于 <see cref="ApplicationEventType"/> 时触发。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="eventId">平台事件 ID。</param>
    public void HandlePlatformEvent(uint eventId)
    {
        var eventName = KnownEvents.GetEventName(eventId);
        _events.Emit(eventName, null, null);

        // 同时触发应用事件路径，对齐 Wails v3 EventManager.handleApplicationEvent。
        if (Enum.IsDefined(typeof(ApplicationEventType), eventId))
        {
            _events.EmitApplicationEvent((ApplicationEventType)eventId, null, null);
        }
    }

    /// <summary>
    /// 处理平台级事件，将事件 ID 转换为事件名并携带数据通过事件处理器分发。
    /// </summary>
    /// <param name="eventId">平台事件 ID。</param>
    /// <param name="data">事件数据。</param>
    public void HandlePlatformEvent(uint eventId, object? data)
    {
        var eventName = KnownEvents.GetEventName(eventId);
        _events.Emit(eventName, data, null);

        // 同时触发应用事件路径，对齐 Wails v3 EventManager.handleApplicationEvent。
        if (Enum.IsDefined(typeof(ApplicationEventType), eventId))
        {
            _events.EmitApplicationEvent((ApplicationEventType)eventId, data, null);
        }
    }

    /// <summary>
    /// 分发窗口级事件，将事件类型转换为事件名并携带窗口 ID 通过事件处理器分发。
    /// 对应 Wails v3 Go 版本 application.go 中的 dispatchWindowEvent 方法。
    /// </summary>
    /// <param name="windowId">发生事件的窗口 ID。</param>
    /// <param name="eventType">窗口事件类型。</param>
    public void DispatchWindowEvent(uint windowId, uint eventType)
    {
        var eventName = KnownEvents.GetEventName(eventType);
        _events.Emit(eventName, null, windowId);
    }

    /// <summary>
    /// 处理从前端 WebView 发来的消息。
    /// 对应 Wails v3 Go 版本中的消息分发逻辑。
    /// 将 JSON 消息委托给 <see cref="MessageProcessor"/> 解析并处理，
    /// 支持 call、event、query、drag、contextmenu、window 等消息类型。
    /// </summary>
    /// <param name="message">JSON 格式的消息字符串。</param>
    /// <param name="windowId">发送消息的窗口 ID，用于窗口操作消息的分发。可为 null。</param>
    /// <returns>响应消息，若无需响应则返回 null。</returns>
    public async Task<ResponseMessage?> HandleMessageFromFrontend(string message, uint? windowId = null)
    {
        _messageProcessor ??= new MessageProcessor(_bindings, _events, id => GetWindow(id), _commandDispatcher, _serviceProvider);
        var parsed = _messageProcessor.ParseMessage(message);
        if (parsed is null)
        {
            return null;
        }

        // 若调用方提供了窗口 ID 且消息中未显式指定，则注入窗口 ID
        if (windowId is not null && parsed.WindowId is null)
        {
            parsed.WindowId = windowId;
        }

        return await _messageProcessor.ProcessAsync(parsed);
    }

    /// <summary>
    /// 在主线程上分发执行指定操作。
    /// 若平台应用存在则委托给平台应用，否则直接执行。
    /// </summary>
    /// <param name="action">要执行的操作。</param>
    public virtual void DispatchOnMainThread(Action action)
    {
        if (_platformApp is not null)
        {
            _platformApp.DispatchOnMainThread(action);
        }
        else
        {
            action();
        }
    }

    /// <summary>
    /// 设置应用菜单。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 SetApplicationMenu 方法。
    /// </summary>
    /// <param name="menu">菜单实例，可为 null。</param>
    public void SetApplicationMenu(Menu? menu)
    {
        _platformApp?.SetApplicationMenu(menu);
    }

    /// <summary>
    /// 显示关于对话框。使用应用选项中的名称、描述和图标。
    /// 对应 Wails v3 Go 版本 application.go 中的 ShowAboutDialog 方法。
    /// </summary>
    public void ShowAboutDialog()
    {
        _platformApp?.ShowAboutDialog(_options.Name, _options.Description, _options.Icon);
    }

    /// <summary>
    /// 设置应用图标。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 SetIcon 方法。
    /// </summary>
    /// <param name="icon">图标字节数据，可为 null。</param>
    public void SetIcon(byte[]? icon)
    {
        _platformApp?.SetIcon(icon);
    }

    /// <summary>
    /// 隐藏应用。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 Hide 方法。
    /// </summary>
    public void Hide()
    {
        _platformApp?.Hide();
    }

    /// <summary>
    /// 显示应用。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 Show 方法。
    /// </summary>
    public void Show()
    {
        _platformApp?.Show();
    }

    /// <summary>
    /// 获取主屏幕信息。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 GetPrimaryScreen 方法。
    /// </summary>
    /// <returns>主屏幕实例，若平台应用未设置则返回 null。</returns>
    public Screen? GetPrimaryScreen()
    {
        return _platformApp?.GetPrimaryScreen();
    }

    /// <summary>
    /// 获取所有屏幕信息。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 GetScreens 方法。
    /// </summary>
    /// <returns>屏幕数组，若平台应用未设置则返回空数组。</returns>
    public Screen[] GetScreens()
    {
        return _platformApp?.GetScreens() ?? Array.Empty<Screen>();
    }

    /// <summary>
    /// 判断当前是否为暗色模式。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 IsDarkMode 方法。
    /// </summary>
    /// <returns>若为暗色模式返回 true，否则返回 false。平台应用未设置时返回 false。</returns>
    public bool IsDarkMode()
    {
        return _platformApp?.IsDarkMode() ?? false;
    }

    /// <summary>
    /// 获取系统强调色。委托给平台应用。
    /// 对应 Wails v3 Go 版本 application.go 中的 GetAccentColor 方法。
    /// </summary>
    /// <returns>系统强调色字符串，平台应用未设置时返回 "#000000"。</returns>
    public string GetAccentColor()
    {
        return _platformApp?.GetAccentColor() ?? "#000000";
    }

    /// <summary>
    /// 生成 JavaScript 运行时代码并注入到资源服务器。
    /// </summary>
    /// <param name="isDebug">是否调试模式。</param>
    /// <returns>生成的 JS 运行时代码。</returns>
    public string GenerateRuntimeJs(bool isDebug = false)
    {
        var platform = _platformApp switch
        {
            null => "server",
            _ when OperatingSystem.IsWindows() => "windows",
            _ when OperatingSystem.IsLinux() => "linux",
            _ => "unknown",
        };

        var options = new RuntimeOptions
        {
            Platform = platform,
            IsDebug = isDebug,
            IsServerMode = _platformApp is null or Platform.ServerMode.ServerPlatformApp,
        };

        // P0-D：Server 模式事件 API 完善 — 将传输层 URL 注入 RuntimeOptions，
        // 使 ServerRuntime 生成的 JS 客户端代码能连接到正确的 WebSocket 端点。
        // 对应 Wails v3 中通过 wails:// 将 transport 信息注入到前端 runtime 的机制。
        if (options.IsServerMode)
        {
            if (_transport is WebSocketTransport wsTransport)
            {
                options.WebSocketUrl = wsTransport.WebSocketUrl;
                options.AssetServerUrl = wsTransport.BaseUrl;
            }
            else if (_transport is HttpTransport httpTransport)
            {
                options.AssetServerUrl = httpTransport.BaseUrl;
            }
        }

        return RuntimeGenerator.Generate(options);
    }

    /// <summary>
    /// 向所有已连接的前端客户端广播事件（P0-D：Server 模式事件 API 完善）。
    /// <para>
    /// 统一的事件广播入口，无论当前是桌面模式还是 Server 模式，均通过此方法广播事件：
    /// <list type="bullet">
    /// <item>桌面模式：通过 <see cref="NativeIpcTransport"/>（已注册窗口时）或
    /// <see cref="EventIPCTransport"/>（无窗口兜底）将事件注入到 webview。</item>
    /// <item>Server 模式：通过 <see cref="WebSocketTransport"/> 将事件推送到所有
    /// 已连接的 WebSocket 客户端，前端 <c>ServerRuntime</c> 接收并触发本地回调。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 对应 Wails v3 Go 版本 <c>Application.EmitEvent(name, data)</c>，
    /// 与直接调用 <see cref="Events.Emit(name, data)"/> 等价，提供更直观的 API 表面。
    /// </para>
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据，可为 null。</param>
    public void BroadcastEvent(string eventName, object? data = null)
    {
        _events.Emit(eventName, data, null);
    }
}
