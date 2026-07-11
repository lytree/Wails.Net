using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
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
    /// 资源服务器实例。
    /// </summary>
    private Wails.Net.AssetServer.AssetServer? _assetServer;

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
    /// 日志回调，由外部设置。由于项目未直接引用 Microsoft.Extensions.Logging，
    /// 此处使用 <see cref="Action{T}"/> 委托替代 <c>ILogger</c> 接口。
    /// 对应 Wails v3 Go 版本 application.go 中的 logger 字段。
    /// </summary>
    private Action<string>? _logger;

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

        // 连接传输层事件监听器到事件处理器（_events 为只读字段，仅设置监听器而非替换实例）
        var listener = serviceProvider.GetService<IWailsEventListener>();
        if (listener is not null)
        {
            _events.SetWailsEventListener(listener);
        }
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
    /// 获取或设置传输层实例。
    /// </summary>
    public ITransport? Transport
    {
        get => _transport;
        set => _transport = value;
    }

    /// <summary>
    /// 获取或设置资源服务器实例。
    /// </summary>
    public Wails.Net.AssetServer.AssetServer? AssetServer
    {
        get => _assetServer;
        set => _assetServer = value;
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
    /// 获取或设置日志回调。
    /// 由于项目未直接引用 Microsoft.Extensions.Logging 包，
    /// 使用 <see cref="Action{T}"/> 委托替代 ILogger 接口。
    /// 对应 Wails v3 Go 版本 application.go 中的 logger 字段。
    /// </summary>
    public Action<string>? Logger
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
        _serviceRegistry.Register(service);
        _bindings.Add(service);
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

        // 触发 OnStartup 和 OnAfterStart 回调
        _options.OnStartup?.Invoke();
        _options.OnAfterStart?.Invoke();

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
    }

    /// <summary>
    /// 退出应用，触发关闭流程。
    /// </summary>
    public virtual void Quit()
    {
        Shutdown();
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
    /// </summary>
    /// <param name="eventId">平台事件 ID。</param>
    public void HandlePlatformEvent(uint eventId)
    {
        var eventName = KnownEvents.GetEventName(eventId);
        _events.Emit(eventName, null, null);
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
    /// <returns>表示处理操作的异步任务。</returns>
    public async Task HandleMessageFromFrontend(string message)
    {
        _messageProcessor ??= new MessageProcessor(_bindings, _events);
        var parsed = _messageProcessor.ParseMessage(message);
        if (parsed is not null)
        {
            await _messageProcessor.ProcessAsync(parsed);
        }
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

        return RuntimeGenerator.Generate(options);
    }
}
