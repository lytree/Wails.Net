using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Security;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// Server 模式传输层扩展方法（P0-D：Server 模式事件 API 完善）。
/// <para>
/// 为 <see cref="DesktopApplicationBuilder"/> 提供 <see cref="UseServerModeTransport"/>
/// 扩展，自动装配 <see cref="WebSocketTransport"/> + <see cref="WebSocketBroadcaster"/>
/// + <see cref="MessageProcessor"/>，使 Server 模式下事件广播与 RPC 调用开箱即用。
/// </para>
/// <para>
/// 典型用法：
/// <code>
/// var builder = DesktopApplicationBuilder.CreateBuilder(args);
/// builder.UseAutoPlatform()
///        .UseServerModeTransport();
/// </code>
/// </para>
/// </summary>
public static class ServerModeTransportExtensions
{
    /// <summary>
    /// 自动装配 Server 模式传输层。
    /// <para>
    /// 注册以下 DI 服务（若尚未注册）：
    /// <list type="bullet">
    /// <item><see cref="WebSocketBroadcaster"/> 单例 — 用于事件广播到所有 WS 客户端。</item>
    /// <item><see cref="MessageProcessor"/> 工厂 — 从 <see cref="BindingManager"/>、
    /// <see cref="EventProcessor"/> 创建，并接入 <see cref="Application"/> 的窗口查找。</item>
    /// <item><see cref="WebSocketTransport"/> 单例 — 主传输层，监听 /wails/ws 端点。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 同时注册一个 <see cref="IPlatformAppInitializer"/> 钩子，在 Application 启动时
    /// 将已构造的 <see cref="WebSocketTransport"/> 实例注入到 <see cref="Application.Transport"/>，
    /// 使 <see cref="Application.Run"/> 启动传输层并将 WebSocket URL 注入到前端 runtime。
    /// </para>
    /// <para>
    /// 调用此方法后，<see cref="Application.BroadcastEvent"/> 在 Server 模式下会通过
    /// <see cref="WebSocketTransport.NotifyEvent"/> → <see cref="WebSocketBroadcaster.BroadcastEvent"/>
    /// 推送到所有已连接的 WS 客户端，前端 <c>ServerRuntime</c> 接收并触发本地回调。
    /// </para>
    /// </summary>
    /// <param name="builder">桌面应用构建器。</param>
    /// <returns>当前构建器实例，用于链式调用。</returns>
    public static DesktopApplicationBuilder UseServerModeTransport(this DesktopApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // 注册 WebSocketBroadcaster 为单例（幂等：若用户已注册则保留）
        if (!builder.Services.Any(d => d.ServiceType == typeof(WebSocketBroadcaster)))
        {
            builder.Services.AddSingleton(sp =>
            {
                var ipcOptions = sp.GetService<Microsoft.Extensions.Options.IOptions<Wails.Net.Application.Security.IpcOptions>>()?.Value;
                var loggerFactory = sp.GetService<ILoggerFactory>();
                var logger = loggerFactory?.CreateLogger<WebSocketBroadcaster>();
                var capacity = ipcOptions?.MailboxCapacity ?? WebSocketBroadcaster.DefaultMailboxCapacity;
                return new WebSocketBroadcaster(logger, capacity);
            });
        }

        // 注册 MessageProcessor 工厂（依赖 BindingManager + EventProcessor，均为单例）
        if (!builder.Services.Any(d => d.ServiceType == typeof(MessageProcessor)))
        {
            builder.Services.AddSingleton(sp =>
            {
                var bindings = sp.GetRequiredService<BindingManager>();
                var events = sp.GetRequiredService<EventProcessor>();
                var app = sp.GetRequiredService<Application>();
                var dispatcher = app.CommandDispatcher;
                return new MessageProcessor(
                    bindings,
                    events,
                    id => app.GetWindow(id),
                    dispatcher,
                    sp);
            });
        }

        // 注册统一 IPC 传输策略（OPT-IPC-003）。
        // 传输层选择与安全配置（token/Origin/CORS）集中在本策略中，
        // 避免选择逻辑散落在平台实现中；平台窗口迁移见路线图 §5.2 后续批次。
        if (!builder.Services.Any(d => d.ServiceType == typeof(IIpcTransportStrategy)))
        {
            builder.Services.AddSingleton<IIpcTransportStrategy, DefaultIpcTransportStrategy>();
        }

        // 注册 WebSocketTransport 为单例。
        // OPT-IPC-001/002/003：由统一策略创建，装配时注入进程级 handshake token
        // 与 Origin/CORS 安全配置（来自 Wails:Ipc 节），使 Server 模式 WebSocket
        // 通道默认要求 token 校验。
        if (!builder.Services.Any(d => d.ServiceType == typeof(WebSocketTransport)))
        {
            builder.Services.AddSingleton(sp =>
            {
                var processor = sp.GetRequiredService<MessageProcessor>();
                var broadcaster = sp.GetRequiredService<WebSocketBroadcaster>();
                var ipcOptions = sp.GetService<Microsoft.Extensions.Options.IOptions<Wails.Net.Application.Security.IpcOptions>>()?.Value;
                var strategy = sp.GetService<IIpcTransportStrategy>() ?? new DefaultIpcTransportStrategy();
                var context = new IpcTransportContext
                {
                    Processor = processor,
                    Broadcaster = broadcaster,
                    IpcOptions = ipcOptions,
                    HandshakeToken = Wails.Net.Application.Security.IpcHandshakeToken.Current,
                    IsServerMode = true,
                };

                // 策略返回首选传输；Server 模式下即 WebSocketTransport。
                // 若策略未返回（异常场景），回退到直接构造。
                return strategy.CreatePrimaryTransport(context) as WebSocketTransport
                    ?? CreateServerWebSocketTransport(processor, broadcaster, ipcOptions);
            });
        }

        // 注册 IWailsEventListener 适配器：将 WebSocketTransport 作为事件监听器追加到 EventProcessor
        // EventProcessor.Emit 触发时会调用 NotifyEvent 将事件广播到所有 WS 客户端。
        if (!builder.Services.Any(d => d.ServiceType == typeof(WebSocketTransportEventListener)))
        {
            builder.Services.AddSingleton<WebSocketTransportEventListener>();
            builder.Services.AddSingleton<IWailsEventListener>(sp =>
                sp.GetRequiredService<WebSocketTransportEventListener>());
        }

        // 审计 §3.6 / R0-6：使用 DI 工厂注册 ServerModeTransportInitializer。
        // DI 单例工厂首次解析时即构造 initializer 实例，触发构造函数中的
        // _application.Transport = _transport 装配，早于 Host.StartAsync()。
        // 同时注册为 IHostedService 以保持与 Generic Host 的启动协议兼容
        // （StartAsync 变为空操作，保留接口契约）。
        if (!builder.Services.Any(d => d.ServiceType == typeof(ServerModeTransportInitializer)))
        {
            builder.Services.AddSingleton<ServerModeTransportInitializer>(sp =>
            {
                var app = sp.GetRequiredService<Application>();
                var transport = sp.GetRequiredService<WebSocketTransport>();
                var logger = sp.GetService<ILogger<ServerModeTransportInitializer>>();
                return new ServerModeTransportInitializer(app, transport, logger);
            });
            builder.Services.AddHostedService(sp => sp.GetRequiredService<ServerModeTransportInitializer>());
        }

        return builder;
    }

    /// <summary>
    /// 根据 <see cref="IpcOptions"/> 创建 IPC 来源校验器（OPT-IPC-002）。
    /// 白名单来自 <see cref="IpcOptions.AllowedOrigins"/>；本地源总是允许（校验器内建）。
    /// </summary>
    /// <param name="ipcOptions">IPC 安全配置。</param>
    /// <returns>校验器实例。</returns>
    private static IpcOriginValidator CreateOriginValidator(IpcOptions ipcOptions)
    {
        var whitelist = new UrlWhitelist();
        foreach (var origin in ipcOptions.AllowedOrigins)
        {
            if (!string.IsNullOrWhiteSpace(origin))
            {
                whitelist.Add(origin);
            }
        }

        return new IpcOriginValidator(whitelist);
    }

    /// <summary>
    /// 回退构造 Server 模式 WebSocket 传输（策略未返回首选传输时的兜底）。
    /// 保留原 OPT-IPC-001/002 的安全配置注入行为。
    /// </summary>
    /// <param name="processor">消息处理器。</param>
    /// <param name="broadcaster">事件广播器。</param>
    /// <param name="ipcOptions">IPC 安全配置（可为 null）。</param>
    /// <returns>配置完成的 WebSocketTransport 实例。</returns>
    private static WebSocketTransport CreateServerWebSocketTransport(
        MessageProcessor processor,
        WebSocketBroadcaster broadcaster,
        IpcOptions? ipcOptions)
    {
        var transport = new WebSocketTransport(processor, broadcaster);
        transport.HandshakeToken = Wails.Net.Application.Security.IpcHandshakeToken.Current;
        if (ipcOptions is not null)
        {
            transport.RequireHandshakeToken = ipcOptions.RequireHandshakeToken;
            transport.OriginValidator = CreateOriginValidator(ipcOptions);
            transport.CorsOptions = new CorsOptions
            {
                Enabled = ipcOptions.RequireOriginValidation,
                AllowedOrigins = ipcOptions.AllowedOrigins.ToList(),
            };
        }

        // P0 安全加固（对齐 Wails v3 v3.0.0）：Host 校验（DNS rebinding 防护）+ 同源 Origin 校验。
        transport.RequestGuard = new IpcRequestGuard(ipcOptions?.AllowedOrigins)
        {
            AllowAllOrigins = ipcOptions is not null && !ipcOptions.RequireOriginValidation,
        };

        return transport;
    }

    /// <summary>
    /// WebSocketTransport 事件监听器适配器。
    /// 将 <see cref="WebSocketTransport"/> 包装为 <see cref="IWailsEventListener"/>，
    /// 让 EventProcessor 在 Emit 时调用 WebSocketTransport.NotifyEvent 广播事件。
    /// </summary>
    private sealed class WebSocketTransportEventListener : IWailsEventListener
    {
        private readonly WebSocketTransport _transport;

        public WebSocketTransportEventListener(WebSocketTransport transport)
        {
            _transport = transport;
        }

        /// <inheritdoc />
        public void NotifyEvent(string eventName, object? data, uint? senderWindowId = null, uint? targetWindowId = null)
        {
            _transport.NotifyEvent(eventName, data, senderWindowId, targetWindowId);
        }
    }

    /// <summary>
    /// Server 模式传输层初始化器（审计 §3.6 / R0-6：启动顺序硬化）。
    /// <para>
    /// <b>关键变更</b>：传输装配从 <see cref="Microsoft.Extensions.Hosting.IHostedService.StartAsync"/>
    /// 阶段前移到 <see cref="ServerModeTransportInitializer"/> 构造期（即 DI 单例工厂首次解析时）。
    /// </para>
    /// <para>
    /// 这样保证：当 <see cref="Application.Run"/> 在 <see cref="DesktopHostedService.StartAsync"/>
    /// 触发的 UI 线程上执行 <c>_transport.StartAsync</c> 之前，
    /// <see cref="Application.Transport"/> 一定已被赋值，避免竞争条件下 transport 为 null 的 NRE。
    /// </para>
    /// <para>
    /// 实现：DI 工厂模式注册 <see cref="ServerModeTransportInitializer"/> 为单例，
    /// 工厂函数内立即完成 <c>_application.Transport = _transport</c> 装配，
    /// 后续 <see cref="Microsoft.Extensions.Hosting.IHostedService.StartAsync"/> 仅作空操作保留接口契约。
    /// </para>
    /// </summary>
    private sealed class ServerModeTransportInitializer : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly Application _application;
        private readonly WebSocketTransport _transport;
        private readonly ILogger<ServerModeTransportInitializer>? _logger;

        public ServerModeTransportInitializer(
            Application application,
            WebSocketTransport transport,
            ILogger<ServerModeTransportInitializer>? logger = null)
        {
            _application = application;
            _transport = transport;
            _logger = logger;

            // 审计 §3.6 / R0-6：构造期即完成传输装配，确保早于 Host.StartAsync 和 DesktopHostedService.StartAsync。
            // 工厂首次解析该单例时（通常是 host.Build() 末尾）即触发此处赋值，
            // 因此 Application.Run() 触发 _transport.StartAsync 前 Transport 一定非 null。
            // 若用户已显式设置 Transport，则保留用户配置不覆盖。
            if (_application.Transport is null)
            {
                _application.Transport = _transport;
                _logger?.LogInformation(
                    "Server 模式传输层已自动装配（构造期）：WebSocketTransport @ {BaseUrl}",
                    _transport.BaseUrl);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// 传输装配已前移到构造函数（DI 单例工厂阶段）。此方法保留为接口契约的空操作。
        /// </remarks>
        public Task StartAsync(System.Threading.CancellationToken cancellationToken)
        {
            _logger?.LogDebug(
                "ServerModeTransportInitializer.StartAsync 调用（实际装配已在构造期完成）");
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(System.Threading.CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
