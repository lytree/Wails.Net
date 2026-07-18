using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
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
            builder.Services.AddSingleton<WebSocketBroadcaster>();
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

        // 注册 WebSocketTransport 为单例
        if (!builder.Services.Any(d => d.ServiceType == typeof(WebSocketTransport)))
        {
            builder.Services.AddSingleton(sp =>
            {
                var processor = sp.GetRequiredService<MessageProcessor>();
                var broadcaster = sp.GetRequiredService<WebSocketBroadcaster>();
                return new WebSocketTransport(processor, broadcaster);
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

        // 注册 IPlatformAppInitializer：在 Application 初始化时将 Transport 注入到 Application
        builder.Services.AddHostedService<ServerModeTransportInitializer>();

        return builder;
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
        public void NotifyEvent(string eventName, object? data)
        {
            _transport.NotifyEvent(eventName, data);
        }
    }

    /// <summary>
    /// Server 模式传输层初始化器。
    /// 在 Host 启动时将已构造的 <see cref="WebSocketTransport"/> 注入到
    /// <see cref="Application.Transport"/>，使 Application.Run 能启动传输层。
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
        }

        /// <inheritdoc />
        public Task StartAsync(System.Threading.CancellationToken cancellationToken)
        {
            // 若用户已显式设置 Transport，则保留用户配置不覆盖
            if (_application.Transport is null)
            {
                _application.Transport = _transport;
                _logger?.LogInformation(
                    "Server 模式传输层已自动装配：WebSocketTransport @ {BaseUrl}",
                    _transport.BaseUrl);
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(System.Threading.CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
