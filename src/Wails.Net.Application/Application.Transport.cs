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
/// Application 的 IPC 传输 / 资源服务器 / 自定义 scheme 分部（RH-HOST-02 文件级拆分）。<para>承载传输策略装配、AssetServer 与 DevAssetProxy、自定义 scheme facade 与服务路由暂存。public API 与拆分前完全一致，仅按关注点拆分文件。</para>
/// </summary>
public partial class Application
{
    /// <summary>
    /// 传输层实例。
    /// </summary>
    private ITransport? _transport;

    /// <summary>
    /// IPC 传输策略（RH-IPC-02 第 4 点）：DI 可覆盖，缺省 <see cref="DefaultIpcTransportStrategy"/>。
    /// 原生通道启用判定、主传输与 fallback 链、事件监听器装配三件事统一由它决定。
    /// </summary>
    private IIpcTransportStrategy _ipcStrategy = new DefaultIpcTransportStrategy();

    /// <summary>
    /// fallback 传输链（由策略产出）。主传输启动后依次启动，关闭时先于主传输停止。
    /// 此前 <see cref="IIpcTransportStrategy.CreateFallbackTransports"/> 无任何调用方，
    /// 本字段让它成为真实的装配面而不是接口注释。
    /// </summary>
    private ITransport[] _fallbackTransports = [];

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
    /// 统一调用网关（R2-1 融合审计：Binding + Command 共用入口）。
    /// 在 CommandDispatcher 注入后创建，并注入到 MessageProcessor。
    /// 为 null 时 MessageProcessor 仍保留本地 Binding/Command 调用路径（向后兼容）。
    /// </summary>
    public InvocationGateway? InvocationGateway { get; private set; }

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
    /// dev 模式资产反向代理（P2-12）。宿主配置了 <c>DevServerUrl</c> 时创建并注入；
    /// 窗口请求管线（<see cref="Webview.WebviewRequestPipeline"/>）在处理资源请求时优先委托它。
    /// </summary>
    private Webview.DevAssetProxy? _devAssetProxy;

    /// <summary>
    /// 待挂载的服务路由暂存区（P1-6，RH-HOST-02 抽出）。
    /// 服务在 AssetServer 注入前注册且配置了 <see cref="Services.ServiceOptions.Route"/> 时暂存于此。
    /// </summary>
    private readonly ServiceRouteRegistry _serviceRoutes = new();

    /// <summary>
    /// 自定义 URI scheme 注册表（RH-HOST-02 抽出）。
    /// 存储与并发控制由 <see cref="Schemes.CustomSchemeRegistry"/> 独占，本类只保留 facade 方法。
    /// </summary>
    private readonly Schemes.ICustomSchemeRegistry _schemes = new Schemes.CustomSchemeRegistry();

    /// <summary>DI 容器槽。</summary>
    internal IServiceProvider? ServiceProviderSlot { get => _serviceProvider; set => _serviceProvider = value; }

    /// <summary>IPC 传输策略槽（null 忽略，缺省策略保持不变）。</summary>
    internal IIpcTransportStrategy? IpcStrategySlot
    {
        get => _ipcStrategy;
        set
        {
            if (value is not null)
            {
                _ipcStrategy = value;
            }
        }
    }

    /// <summary>
    /// 按策略装配 IPC 事件下行监听器（RH-IPC-02 第 4 点）。
    /// <para>
    /// 历史实现把"DI 提供的监听器 + EventIPCTransport 兜底"硬编码在此处，
    /// 现由 <see cref="IIpcTransportStrategy.CreateEventListeners"/> 决定装配内容与顺序，
    /// 保证 Server 模式与桌面模式走同一条判定路径。
    /// </para>
    /// </summary>
    /// <param name="serviceProvider">DI 容器，用于取出已注册的监听器与日志器。</param>
    internal void WireIpcEventListeners(IServiceProvider serviceProvider)
    {
        var context = BuildIpcContext(
            serviceProvider.GetService<IWailsEventListener>(),
            serviceProvider.GetService<ILogger<EventIPCTransport>>());

        foreach (var listener in _ipcStrategy.CreateEventListeners(context))
        {
            Events.AddWailsEventListener(listener);
        }
    }

    /// <summary>
    /// 构建 IPC 传输策略上下文（平台能力 + 安全配置 + 诊断日志）。
    /// 桌面模式无宿主级 <see cref="ITransport"/>，因此 Processor/Broadcaster 允许为 null。
    /// </summary>
    private IpcTransportContext BuildIpcContext(
        IWailsEventListener? providedEventListener = null,
        ILogger<EventIPCTransport>? eventIpcLogger = null)
        => new()
        {
            Processor = _messageProcessor,
            Broadcaster = _serviceProvider?.GetService<WebSocketBroadcaster>(),
            IpcOptions = _serviceProvider?
                .GetService<Microsoft.Extensions.Options.IOptions<Security.IpcOptions>>()?.Value,
            HandshakeToken = Security.IpcHandshakeToken.Current,
            IsServerMode = _platformApp is null or Platform.ServerMode.ServerPlatformApp,
            HasNativeMessageChannel = _platformApp?.SupportsNativeMessageChannel == true,
            UseNativeIpc = UseNativeIpc,
            ProvidedEventListener = providedEventListener,
            EventIpcLogger = eventIpcLogger,
            DiagnosticsLogger = _logger,
        };

    /// <summary>
    /// 获取服务注册表。
    /// </summary>
    public ServiceRegistry Services => _serviceRegistry;


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
        internal set
        {
            _commandDispatcher = value;
            // R2-1：CommandDispatcher 注入后创建统一调用网关，
            // 将 BindingManager、CommandDispatcher、PermissionManager、IServiceProvider、ILogger 注入网关，
            // 并通过 SetWindowLookup 注入窗口查找，使网关能在调用时解析窗口名（用于窗口级 Capability 隔离）。
            if (value is not null)
            {
                var gatewayLogger = _serviceProvider?.GetService<Microsoft.Extensions.Logging.ILogger<InvocationGateway>>();
                InvocationGateway = new InvocationGateway(
                    Bindings,
                    value,
                    _serviceProvider?.GetService<Wails.Net.Application.Security.PermissionManager>(),
                    _serviceProvider,
                    gatewayLogger);
                InvocationGateway.SetWindowLookup(GetWindow);

                // 同时将网关注入到 MessageProcessor（若已创建）。
                _messageProcessor?.SetGateway(InvocationGateway);
            }
        }
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
            _serviceRoutes.FlushTo(value);
        }
    }

    /// <summary>
    /// dev 模式资产反向代理（P2-12）。
    /// 仅在宿主配置了 <c>DevServerUrl</c> 时由 <see cref="Hosting.DesktopApplicationBuilder"/> 注入；
    /// 为 null 时不影响任何请求路径（生产模式行为与改造前一致）。
    /// </summary>
    internal Webview.DevAssetProxy? DevAssetProxy
    {
        get => _devAssetProxy;
        set => _devAssetProxy = value;
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
        _schemes.Register(
            scheme,
            assetServer ?? _assetServer
            ?? throw new InvalidOperationException(
                "RegisterScheme 需要提供 assetServer，或在 Application.AssetServer 已设置后调用。"));
    }

    /// <summary>
    /// 取消注册指定 URI scheme。
    /// </summary>
    /// <param name="scheme">要取消注册的 scheme 名称。</param>
    /// <returns>是否成功移除；若 scheme 未注册则返回 false。</returns>
    public bool UnregisterScheme(string scheme) => _schemes.Unregister(scheme);

    /// <summary>
    /// 查询指定 URI scheme 是否已注册。
    /// </summary>
    /// <param name="scheme">scheme 名称。</param>
    /// <returns>若已注册返回 true；否则返回 false。</returns>
    public bool IsSchemeRegistered(string scheme) => _schemes.IsRegistered(scheme);

    /// <summary>
    /// 尝试获取指定 URI scheme 关联的 <see cref="Wails.Net.AssetServer.AssetServer"/>。
    /// 供平台 WebviewWindow 实现在创建时查询已注册的 scheme 以设置请求拦截。
    /// </summary>
    /// <param name="scheme">scheme 名称。</param>
    /// <param name="assetServer">查找到的资源服务器；若未注册则为 null。</param>
    /// <returns>是否找到已注册的 scheme。</returns>
    public bool TryGetSchemeAssetServer(string scheme, out Wails.Net.AssetServer.AssetServer? assetServer)
        => _schemes.TryGetAssetServer(scheme, out assetServer);

    /// <summary>
    /// 获取所有已注册的自定义 URI scheme 名称的只读列表。
    /// </summary>
    public IReadOnlyList<string> RegisteredSchemes => _schemes.RegisteredSchemes;


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
    /// 获取超大事件载荷的宿主侧有界存储（事件投递硬化 P0-1 / Layer 3，
    /// 对应 Wails v3 Go 版本 <c>App.eventPayloads</c>）。
    /// <para>
    /// 超过 <see cref="Events.EventPayloadStore.MaxInlinePayloadBytes"/> 字节的事件载荷在此停放，
    /// 前端通过 <c>GET /wails/eventpayload/&lt;id&gt;</c> 一次性取回，避免大载荷经过 eval 通道
    /// 触发 WebKit 的进程外共享内存滞留（macOS 宿主 / WebKitGTK Web 进程膨胀）。
    /// </para>
    /// </summary>
    internal Events.EventPayloadStore EventPayloads { get; } = new();

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
    /// 装配并启动 IPC 传输层（P0-C2 / RH-IPC-02 第 4 点）。
    /// <para>
    /// 顺序与语义（全部由 <see cref="IIpcTransportStrategy"/> 判定）：
    /// <list type="number">
    /// <item>主传输：<see cref="Transport"/> 已被用户或 DI 初始化器设置时保持不动，
    /// 否则由策略创建（桌面模式返回 null，消息走平台拦截）；</item>
    /// <item>fallback 链：由策略按统一顺序产出，主传输启动后依次启动；</item>
    /// <item>原生 IPC 通道：仅当平台具备原生 postMessage 能力且未禁用时构造
    /// <see cref="NativeIpcTransport"/>，并注册为事件监听器。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 必须在窗口创建前完成，以便平台 <c>CreateWebviewWindow</c> 能注册窗口到原生通道。
    /// </para>
    /// </summary>
    private void InitializeIpcTransport()
    {
        var context = BuildIpcContext();

        // 主传输：仅在 DI 宿主下由策略补建。非 DI 构造（单测 / 嵌入场景）没有
        // MessageProcessor 与 Broadcaster，策略无从构造 Server 传输——保持"无宿主级传输"的旧行为。
        // Server 模式下 Transport 通常已由 ServerModeTransportInitializer 提前装配，此处不覆盖。
        if (_transport is null && _serviceProvider is not null)
        {
            _transport = _ipcStrategy.CreatePrimaryTransport(context);
        }

        if (_transport is not null)
        {
            _transport.StartAsync(_cts.Token).GetAwaiter().GetResult();
        }

        // fallback 链：主传输起不来时仍有兜底路径（默认为空，仅自定义策略会返回）。
        var fallbacks = _ipcStrategy.CreateFallbackTransports(context);
        if (fallbacks.Count > 0)
        {
            var started = new List<ITransport>(fallbacks.Count);
            foreach (var fallback in fallbacks)
            {
                fallback.StartAsync(_cts.Token).GetAwaiter().GetResult();
                started.Add(fallback);
            }

            _fallbackTransports = started.ToArray();
            _logger?.LogInformation("IPC fallback 传输链已启动：{Count} 条", started.Count);
        }

        // 原生 IPC 通道（P0-C2）：判定集中在策略，避免"只看 UseNativeIpc 就构造"的旧逻辑
        // 在 Linux/macOS/Android 与 Server 模式下产生永不注册窗口的空传输。
        if (_ipcStrategy.UseNativeIpcTransport(context))
        {
            // 确保 MessageProcessor 已构造（NativeIpcTransport 需要它来路由消息）
            _messageProcessor ??= new MessageProcessor(
                Bindings, Events, id => GetWindow(id), _commandDispatcher, _serviceProvider, InvocationGateway,
                _serviceProvider?.GetService<ILogger<MessageProcessor>>());

            // 传入日志器：NativeIpcTransport 的事件投递走每窗口队列，单条投递失败
            // （窗口已销毁、跨线程调用被平台拒绝等）只记录 Debug 日志，无日志器时会完全静默。
            NativeIpcTransport = new NativeIpcTransport(
                _messageProcessor,
                _serviceProvider?.GetService<ILogger<NativeIpcTransport>>());
            Events.AddWailsEventListener(NativeIpcTransport);
        }
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
        _messageProcessor ??= new MessageProcessor(Bindings, Events, id => GetWindow(id), _commandDispatcher, _serviceProvider, InvocationGateway,
            _serviceProvider?.GetService<ILogger<MessageProcessor>>());
        var parsed = _messageProcessor.ParseMessage(message);
        if (parsed is null)
        {
            _logger?.LogWarning(
                "前端消息解析失败，已忽略：windowId={WindowId} 长度={Length}",
                windowId, message.Length);
            return null;
        }

        // 若调用方提供了窗口 ID 且消息中未显式指定，则注入窗口 ID
        if (windowId is not null && parsed.WindowId is null)
        {
            parsed.WindowId = windowId;
        }

        try
        {
            return await _messageProcessor.ProcessAsync(parsed);
        }
        catch (Exception ex)
        {
            // 记录后原样抛出，保持既有错误传播语义不变。
            _logger?.LogError(ex,
                "处理前端消息失败：windowId={WindowId} type={Type} id={MessageId}",
                parsed.WindowId, parsed.Type, parsed.Id);
            throw;
        }
    }

}
