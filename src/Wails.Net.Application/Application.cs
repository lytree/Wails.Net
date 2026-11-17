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
/// 核心应用类，对应 Wails v3 Go 版本 application.go 中的 Application 结构。
/// 负责管理应用生命周期、窗口、绑定、事件、传输层、资源服务器及各平台管理器。
/// </summary>
public partial class Application
{
    private readonly ApplicationOptions _options;
    private IPlatformApp? _platformApp = null;
    private readonly ServiceRegistry _serviceRegistry = new();

    /// <summary>
    /// 配置驱动的窗口工厂（RH-HOST-02 抽出）。
    /// 承载 <see cref="Hosting.DesktopApplicationBuilder.Build"/> 传入的窗口配置，
    /// 在 <see cref="Run"/> 的 UI 线程上按需创建，避免主线程创建窗口导致 Win32 线程亲和性问题。
    /// </summary>
    private readonly ConfiguredWindowFactory _windowFactory = new();

    /// <summary>
    /// 绑定管理器，负责注册和调用绑定方法。
    /// 对应 Wails v3 Go 版本 bindings.go 中的 Bindings 结构。
    /// </summary>
    /// <summary>
    /// 绑定管理器（R1-1 DI 构造注入）：DI 模式下即为 DI 注册的实例；
    /// 测试/遗留模式下为构造期局部创建。
    /// </summary>
    public BindingManager Bindings { get; }

    /// <summary>
    /// 事件处理器，管理事件订阅和发布。
    /// 对应 Wails v3 Go 版本 events.go 中的 EventProcessor。
    /// </summary>
    /// <summary>
    /// 事件处理器（R1-1 DI 构造注入）：DI 模式下即为 DI 注册的实例；
    /// 测试/遗留模式下为构造期局部创建。
    /// </summary>
    public EventProcessor Events { get; }

    /// <summary>
    /// Streams 双向字节流管理器（对应 Wails v3 Go 版本 application.go 中 <c>App.streams</c>）。
    /// <para>
    /// 提供「WebSocket 编程模型」而不绑定 TCP 端口：Go→JS 走持锁 poll
    /// （<c>GET /wails/stream/poll</c>），JS→Go 走普通 POST（<c>POST /wails/stream/send</c>）；
    /// Server 模式下为真实 WebSocket（<c>/wails/stream/ws</c>）。
    /// 由构造函数创建，随 <see cref="Application"/> 生命周期（<see cref="ShutdownAsync"/>）Dispose。
    /// </para>
    /// </summary>
    public Streams.StreamManager Streams { get; }


    /// <summary>
    /// 是否正在运行。
    /// </summary>
    private volatile bool _isRunning;

    /// <summary>
    /// Run 之前投递的主线程任务队列（Wails v3.1.0 #6287，RH-HOST-02 抽出）。
    /// </summary>
    private readonly MainThreadTaskQueue _mainThreadQueue = new();

    /// <summary>
    /// 默认信号处理器协调器（P2-21，RH-HOST-02 抽出）。
    /// </summary>
    private readonly SignalHandlerCoordinator _signalHandlers = new();


    private static Application? _globalApplication;


    /// <summary>
    /// 用户生命周期钩子（关闭任务 + 启动前 hook，RH-HOST-02 抽出）。
    /// 对应 Wails v3 Go 版本 application.go 中的 <c>shutdownTasks</c> / OnBeforeStart 机制。
    /// </summary>
    private readonly ApplicationLifecycleHooks _hooks = new();

    /// <summary>
    /// 单实例协调器（RH-HOST-02 抽出）：持有 <c>IsFirstInstance</c> 与进程级唯一性检查。
    /// </summary>
    private readonly SingleInstanceCoordinator _singleInstance;

    /// <summary>
    /// 前端运行时注入协调器（RH-HOST-02 抽出）：生成 <c>window._wails</c> 标志片段。
    /// </summary>
    private readonly FrontendRuntimeCoordinator _frontendRuntime = new();

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
    /// 运行时服务协调器（P1-1 审计项落地）。
    /// 负责注册服务生命周期顺序管理（IServiceStartup / IServiceShutdown），
    /// 从 <see cref="Run"/> / <see cref="Shutdown"/> 中抽取，防止 Application 聚合根继续膨胀。
    /// 新增运行时职责应优先进入 coordinator（<see cref="IRuntimeCoordinator"/>），而不是堆到 Application。
    /// </summary>
    private readonly RuntimeServicesCoordinator _runtimeServices;

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
        // R1-2：复用构造期创建的 WindowManager，避免再次 new 造成窗口注册表分离。
        _windowManager?.SetPlatformApp(platformApp);
        _dialogManager = new DialogManager(platformApp, () => IsRunning);
        _screenManager = new ScreenManager(platformApp);
    }

    /// <summary>
    /// 从 DI 容器初始化管理器。
    /// 尝试从 DI 获取已注册的管理器实例，替换 Application 内部的管理器。
    /// R1-1：Bindings/Events 已改为构造注入的 get-only 自动属性。
    /// DI 模式下与 DI 容器注册实例一致；测试/遗留模式下为局部实例。
    /// 此方法仅用于注入非只读管理器（WindowManager/DialogManager 等），保留向后兼容。
    /// </summary>
    /// <param name="serviceProvider">DI 容器。</param>
    internal void InitializeFromServiceProvider(IServiceProvider serviceProvider)
        => Hosting.ServiceProviderBinder.Bind(this, serviceProvider);

    // ── RH-HOST-02：供 Hosting.ServiceProviderBinder 写入的服务槽（同程序集可见） ──
    // 保留字段而非改成"槽对象"，因为 Application 内部有百余处直接使用这些字段，
    // 换成槽对象只会把读取点全部改写，行数与耦合都不会下降。Logger 已有公共 Setter，不设槽。


    /// <summary>Host 生命周期槽。</summary>
    internal IHostApplicationLifetime? LifetimeSlot { get => _lifetime; set => _lifetime = value; }






    /// <summary>
    /// 获取是否正在运行。
    /// </summary>
    public bool IsRunning => _isRunning;



    /// <summary>
    /// 获取是否启用单实例模式。
    /// 对应 Wails v3 Go 版本 application.go 中 SingleInstance 选项。
    /// </summary>
    public bool IsSingleInstance => _options.SingleInstance;

    /// <summary>
    /// 获取当前进程是否为首实例。
    /// 仅在 <see cref="IsSingleInstance"/> 为 true 且 <see cref="SetupSingleInstance"/> 已调用时有意义。
    /// </summary>
    public bool IsFirstInstance => _singleInstance.IsFirstInstance;

    /// <summary>
    /// 获取应用级取消令牌，在应用关闭时被取消。
    /// 对应 Wails v3 Go 版本 application.go 中的 context.Context。
    /// </summary>
    public CancellationToken ApplicationCancellationToken => _cts.Token;

    /// <summary>
    /// 获取当前平台的原生能力描述。
    /// 对应 Wails v3 Go 版本 <c>App.Capabilities()</c> 方法。
    /// 委托给 <see cref="IPlatformApp.Capabilities"/>；若平台应用未设置则返回
    /// <see cref="PlatformCapabilities.Default"/>。
    /// </summary>
    public PlatformCapabilities Capabilities => _platformApp?.Capabilities ?? PlatformCapabilities.Default;

    /// <summary>
    /// 获取当前进程 ID。
    /// 对应 Wails v3 Go 版本 <c>App.GetPID()</c> 方法。
    /// </summary>
    /// <returns>当前进程的操作系统 PID。</returns>
    public int GetPID() => Environment.ProcessId;

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
    /// <remarks>
    /// 向后兼容入口：测试/遗留代码使用，构造期局部创建 BindingManager/EventProcessor。
    /// 推荐 DI 模式使用下方接受 IServiceProvider 的重载，确保 Host、Server Transport 与
    /// Application 共享同一个 BindingManager/EventProcessor（避免 R1-1 描述的"两套对象图"问题）。
    /// </remarks>
    public Application(ApplicationOptions options)
        : this(options, new BindingManager(), new EventProcessor(), services: null)
    {
    }

    /// <summary>
    /// DI 构造注入版本（审计 §3.1 / R1-1）：从 DI 容器传入 BindingManager、EventProcessor、
    /// IServiceProvider。Host/DI 成为唯一对象图，<see cref="Application"/> 仅作为 facade/coordinator。
    /// <para>
    /// 服务定位器式 <c>InitializeFromServiceProvider</c>（旧路径）已逐步收敛：
    /// DI 模式下 Bindings/Events 在构造期即与 DI 注册的实例一致；该方法仅保留
    /// WindowManager/DialogManager 等非只读管理器的后置注入（向后兼容）。
    /// </para>
    /// </summary>
    /// <param name="options">应用选项。</param>
    /// <param name="bindings">绑定管理器；DI 模式下即为 DI 容器注册的实例。</param>
    /// <param name="events">事件处理器；DI 模式下即为 DI 容器注册的实例。</param>
    /// <param name="services">DI 服务容器；为 null 时跳过 InitializeFromServiceProvider 调用。</param>
    public Application(
        ApplicationOptions options,
        BindingManager bindings,
        EventProcessor events,
        IServiceProvider? services)
    {
        _options = options;
        Bindings = bindings;
        Events = events;
        _serviceProvider = services;
        _globalApplication = this;
        _hooks.AddShutdownTasks(options.ShutdownTasks);
        _singleInstance = new SingleInstanceCoordinator(options);
        _runtimeServices = new RuntimeServicesCoordinator(_serviceRegistry);

        // Streams 管理器在构造期创建，随 Application 生命周期（ShutdownAsync）Dispose。
        // 日志记录器可从 DI 获取；不可用时允许空日志（对应 Go 中 streams==nil 的降级语义不存在，
        // 因为 .NET 侧 Streams 恒非 null）。
        Streams = new StreamManager(services?.GetService<ILogger<StreamManager>>());

        // R1-1：构造期不自动调用 InitializeFromServiceProvider，避免与
        // DesktopApplicationBuilder.Build() 中的显式调用重复（双调用会触发
        // EventIPCTransport/PluginManager 等组件重复初始化乃至 stack overflow）。
        // 调用方负责在合适时机显式调用 InitializeFromServiceProvider。
        _ = services;

        // R1-2：构造期创建 WindowManager（platformApp 可为 null），
        // Application 不再持有 _windows/_nextWindowId 双注册表，统一委托给 WindowManager。
        // SetPlatformApp 时通过 SetPlatformApp(...) 注入 platformApp。
        _windowManager = new WindowManager(_platformApp, this);
    }



    /// <summary>
    /// 设置单实例锁。若启用单实例模式且当前进程非首实例，则直接退出。
    /// 对应 Wails v3 Go 版本 application.go 中 SingleInstanceLock 相关逻辑。
    /// </summary>
    private void SetupSingleInstance()
    {
        _singleInstance.Acquire(_platformApp);
    }


    /// <summary>
    /// 安装默认信号处理器（P2-21）。对应 Wails v3 Go 版本 <c>App.setupSignalHandler</c>。
    /// <para>
    /// <see cref="ApplicationOptions.DisableDefaultSignalHandler"/> 为 true 时跳过，
    /// 进程沿用操作系统默认处置（Ctrl+C 直接终止）。
    /// </para>
    /// <para>
    /// 首个信号先经 <see cref="ShouldQuit"/> 判定：返回 false 时应用继续运行（不退出）。
    /// </para>
    /// </summary>
    private void SetupSignalHandler()
    {
        _signalHandlers.Setup(_options, ShouldQuit, Quit, _logger);
    }

    /// <summary>
    /// 测试用：直接安装信号处理器并返回结果（不进入平台主循环）。
    /// <see cref="ApplicationOptions.DisableDefaultSignalHandler"/> 为 true 时返回 null。
    /// </summary>
    /// <returns>已安装的信号处理器；被禁用时为 null。</returns>
    internal Hosting.DefaultSignalHandler? SetupSignalHandlerForTest()
    {
        SetupSignalHandler();
        return _signalHandlers.Current;
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
        LifecycleState = Hosting.ApplicationLifecycleState.Starting;

        // Wails v3.1.0 #6287：先把 Run 之前排队的主线程任务在启动回调前按序执行。
        FlushPendingMainThreadActions();

        // 触发启动回调
        _options.OnBeforeStart?.Invoke();

        // 执行所有通过 OnBeforeStart 注册的 hook，在启动服务之前运行。
        _hooks.RunBeforeStartHooks(this);

        // 按注册顺序启动服务（P1-1：委托给 RuntimeServicesCoordinator 管理顺序）
        _runtimeServices.StartAsync(_options, _cts.Token).GetAwaiter().GetResult();

        // 启动资源服务器（若实现了 IServiceStartup）
        if (_assetServer is IServiceStartup assetStartup)
        {
            assetStartup.ServiceStartup(_options, _cts.Token).GetAwaiter().GetResult();
        }

        // IPC 传输层装配（RH-IPC-02 第 4 点）：原生通道判定、主传输、fallback 链
        // 全部由 IIpcTransportStrategy 集中决定，Application 不再硬编码平台差异。
        InitializeIpcTransport();

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
        // OPT-RUNTIME-002：进入主循环前设置 PlatformStarted 就绪信号——
        // 所有启动准备（服务、资源服务器、传输层、窗口创建回调）均已完成，
        // 使 Hosting.DesktopHostedService.StartAsync 的返回语义与 UI 就绪对齐。
        // P2-21：进入平台主循环前安装默认信号处理器（SIGINT / SIGTERM），
        // 首个信号经 ShouldQuit 判定后退出应用；重复信号逐级提示，达到上限强制退出。
        // 必须在应用已就绪（服务/窗口已起）之后再装，与 Go application.go 中
        // `a.setupSignalHandler(a.options)` 紧跟 `a.impl.run()` 的时序一致。
        SetupSignalHandler();

        if (_platformApp is not null)
        {
            LifecycleState = Hosting.ApplicationLifecycleState.PlatformStarted;
            _platformStartedTcs.TrySetResult();
            LifecycleState = Hosting.ApplicationLifecycleState.Running;

            try
            {
                _platformApp.Run();
            }
            catch (Exception ex)
            {
                // 平台主循环异常：通知等待方（HostedService.StartAsync 可快速失败），
                // 然后继续走 Shutdown 清理路径。
                _platformStartedTcs.TrySetException(ex);
                _logger?.LogError(ex, "平台主循环异常");
            }
        }
        else
        {
            // 无平台实现（纯 Server 模式降级）：直接置为就绪。
            LifecycleState = Hosting.ApplicationLifecycleState.PlatformStarted;
            _platformStartedTcs.TrySetResult();
            LifecycleState = Hosting.ApplicationLifecycleState.Running;
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
    /// <summary>
    /// 关闭应用（同步入口，向后兼容）。
    /// 委派给 <see cref="ShutdownAsync(CancellationToken)"/> 并同步等待。
    /// 推荐 Host 集成场景使用 <see cref="ShutdownAsync(CancellationToken)"/>。
    /// </summary>
    public virtual void Shutdown()
    {
        // R1-4：保留同步入口以兼容既有调用方（Quit()、DesktopHostedService.Dispose 等）。
        // 内部委派给异步实现；CancellationToken.None 表示不限时（依赖 ShutdownTimeout 兜底）。
        ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步关闭应用（R1-4）。
    /// <para>
    /// 执行关闭任务、OnShutdown 回调、取消取消令牌、关闭所有窗口、停止传输层、停止资源服务器、
    /// 按逆序关闭服务、异步销毁平台应用。
    /// </para>
    /// <para>
    /// 内部使用 <see cref="ApplicationOptions.ShutdownTimeout"/>（默认无超时）作为取消令牌链的上限：
    /// 当关闭流程超过此时长时，<paramref name="cancellationToken"/> 与 ShutdownTimeout 中**先到者**触发取消，
    /// 强制中断等待以保证进程在合理时间内退出。
    /// </para>
    /// <para>
    /// 对应 Wails v3 Go 版本 application.go 中的 Shutdown 方法；与 ASP.NET Core
    /// <c>IHostedService.StopAsync</c> 的契约对齐——支持 CancellationToken 串联、超时降级。
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌。宿主关闭超时或用户主动取消时触发。</param>
    public virtual async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (!_isRunning)
        {
            return;
        }

        // RH-HOST-02：关闭顺序整体下沉到 ApplicationShutdownCoordinator。
        // 上下文在调用点按当前字段快照构建，协调器不反向依赖 Application 实例，关闭顺序可独立测试。
        var shutdown = new ApplicationShutdownCoordinator(new ApplicationShutdownContext
        {
            Options = _options,
            Lifetime = _lifetime,
            ApplicationCancellation = _cts,
            ReleaseSignalHandler = _signalHandlers.Dispose,
            ReleaseDevAssetProxy = () =>
            {
                _devAssetProxy?.Dispose();
                _devAssetProxy = null;
            },
            SetLifecycleState = state => LifecycleState = state,
            MarkStopped = () =>
            {
                _isRunning = false;
                LifecycleState = Hosting.ApplicationLifecycleState.Stopped;
            },
            Hooks = _hooks,
            EventPayloads = EventPayloads,
            Streams = Streams,
            RuntimeServices = _runtimeServices,
            Windows = _windowManager,
            Transport = _transport,
            FallbackTransports = _fallbackTransports,
            AssetServer = _assetServer,
            Plugins = _pluginManager,
            PlatformApp = _platformApp,
        });

        await shutdown.RunAsync(cancellationToken).ConfigureAwait(false);
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
        _hooks.AddShutdownTask(task);
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
        _hooks.AddBeforeStartHook(hook);
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
        Events.Emit(KnownEvents.SecondInstanceLaunched, args, null);
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
        Events.Emit(eventName, null, null);

        // 同时触发应用事件路径，对齐 Wails v3 EventManager.handleApplicationEvent。
        if (Enum.IsDefined(typeof(ApplicationEventType), eventId))
        {
            Events.EmitApplicationEvent((ApplicationEventType)eventId, null, null);
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
        Events.Emit(eventName, data, null);

        // 同时触发应用事件路径，对齐 Wails v3 EventManager.handleApplicationEvent。
        if (Enum.IsDefined(typeof(ApplicationEventType), eventId))
        {
            Events.EmitApplicationEvent((ApplicationEventType)eventId, data, null);
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
        Events.Emit(eventName, null, windowId);
    }

    /// <summary>
    /// 在主线程上分发执行指定操作。
    /// 若平台应用存在则委托给平台应用，否则直接执行。
    /// </summary>
    /// <param name="action">要执行的操作。</param>
    public virtual void DispatchOnMainThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_isRunning)
        {
            // Wails v3.1.0 #6287：Run 之前的主线程任务先入队，
            // 由 Run 进入主循环前在当前（主）线程上按 FIFO 执行。
            _mainThreadQueue.Enqueue(action);
            return;
        }

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
    /// 执行 Run 之前排队的主线程任务（Wails v3.1.0 #6287）。
    /// 仅在 <see cref="Run"/> 的主线程上调用。
    /// </summary>
    private void FlushPendingMainThreadActions()
    {
        _mainThreadQueue.Flush(_logger);
    }


    /// <summary>
    /// 生成 JavaScript 运行时代码并注入到资源服务器。
    /// <para>
    /// 重构（2026-08-02）：前端运行时 JS 已全量迁往 <c>@wailsnet/runtime</c>（npm 包，
    /// 见 <c>packages/wails-net-runtime/</c>），本方法仅负责注入后端 → 前端的最小配置
    /// —— <c>window._wails</c> 标志对象（platform / isDebug / isServerMode / 各 URL），
    /// 供 <c>@wailsnet/runtime/src/internal/types.ts#readRuntimeFlags()</c> 在初始化时读取。
    /// 前端业务 API（call / bindings / events / 46 个命名空间）由前端 <c>import { wails } from "@wailsnet/runtime"</c> 加载。
    /// </para>
    /// <para>
    /// 同时向 <c>console.info</c> 输出一次性加载提示，便于诊断「前端未引 npm 包 → 调用全 404」问题：
    /// 未看到提示 = 后端未注入；看到提示但前端仍不工作 = 前端未 import。
    /// </para>
    /// </summary>
    /// <param name="isDebug">是否调试模式。</param>
    /// <returns>生成的 JS 运行时代码（含 window._wails 标志 + 一次性 console 提示）。</returns>
    public string GenerateRuntimeJs(bool isDebug = false)
        => _frontendRuntime.GenerateRuntimeJs(_platformApp, _transport, isDebug);

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
        Events.Emit(eventName, data, null);
    }

    /// <summary>
    /// 向指定窗口派发跨窗口定向事件（OPT-EVENT-002，2026-08-15 新增）。
    /// <para>
    /// 对应 Wails v3 Go 版本 <c>Application.EmitEventTo(name, data, targetWindowId)</c>：
    /// 事件仅派发到目标窗口的前端（<c>wails.events.on</c> 订阅者），其他窗口不收到；
    /// 后端本地订阅者（<see cref="Events"/>）不受定向限制，仍会收到事件
    /// （可在回调中通过 <see cref="Wails.Net.Application.Events.CustomEvent.TargetWindowID"/> 判断是否定向派发）。
    /// </para>
    /// </summary>
    /// <param name="targetWindowId">目标窗口 ID。</param>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据，可为 null。</param>
    public void EmitTo(uint targetWindowId, string eventName, object? data = null)
    {
        Events.EmitTo(targetWindowId, eventName, data);
    }

}
