using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Security;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// IPC 传输策略测试（OPT-IPC-003 / RH-IPC-02 第 4 点推广）。
/// <para>
/// 覆盖三件事集中化后的行为：
/// <list type="bullet">
/// <item>主传输与 fallback 链的选择（Server 模式 vs 桌面模式）；</item>
/// <item>原生 IPC 通道启用判定（用户开关 × 平台能力 × 模式）；</item>
/// <item>事件下行监听器装配（DI 提供 + EventIPC 兜底，顺序稳定）。</item>
/// </list>
/// </para>
/// </summary>
[NotInParallel]
public sealed class IpcTransportStrategyTests
{
    private static MessageProcessor CreateProcessor()
        => new(new BindingManager(), new EventProcessor());

    private static IpcTransportContext CreateServerContext(
        MessageProcessor? processor = null,
        WebSocketBroadcaster? broadcaster = null)
        => new()
        {
            Processor = processor ?? CreateProcessor(),
            Broadcaster = broadcaster ?? new WebSocketBroadcaster(),
            IsServerMode = true,
            HasNativeMessageChannel = false,
        };

    /// <summary>
    /// Server 模式：首选传输是 WebSocket，并带上 token / Origin 安全配置。
    /// </summary>
    [Test]
    public async Task CreatePrimaryTransport_ServerMode_ReturnsWebSocketWithSecurityConfig()
    {
        var strategy = new DefaultIpcTransportStrategy();
        var context = new IpcTransportContext
        {
            Processor = CreateProcessor(),
            Broadcaster = new WebSocketBroadcaster(),
            IsServerMode = true,
            HandshakeToken = "token-abc",
            IpcOptions = new IpcOptions
            {
                RequireHandshakeToken = true,
                RequireOriginValidation = true,
                AllowedOrigins = { "https://example.test" },
            },
        };

        var transport = strategy.CreatePrimaryTransport(context) as WebSocketTransport;

        await Assert.That(transport).IsNotNull();
        await Assert.That(transport!.HandshakeToken).IsEqualTo("token-abc");
        await Assert.That(transport.RequireHandshakeToken).IsTrue();
        await Assert.That(transport.RequestGuard).IsNotNull();
    }

    /// <summary>
    /// Server 模式缺少 MessageProcessor 时必须快速失败（而不是构造一个不可用的传输）。
    /// </summary>
    [Test]
    public async Task CreatePrimaryTransport_ServerModeWithoutProcessor_Throws()
    {
        var strategy = new DefaultIpcTransportStrategy();
        var context = new IpcTransportContext
        {
            IsServerMode = true,
            Broadcaster = new WebSocketBroadcaster(),
        };

        await Assert.That(() => strategy.CreatePrimaryTransport(context))
            .ThrowsExactly<InvalidOperationException>();
    }

    /// <summary>
    /// 桌面模式：不构造宿主级主传输（消息走平台原生通道），fallback 链默认为空。
    /// </summary>
    [Test]
    public async Task CreatePrimaryTransport_DesktopMode_ReturnsNullWithoutFallbacks()
    {
        var strategy = new DefaultIpcTransportStrategy();
        var context = new IpcTransportContext { HasNativeMessageChannel = true };

        var primary = strategy.CreatePrimaryTransport(context);
        var fallbacks = strategy.CreateFallbackTransports(context);

        await Assert.That(primary).IsNull();
        await Assert.That(fallbacks).IsEmpty();
    }

    /// <summary>
    /// 原生 IPC 通道需要同时满足：用户启用 + 平台具备原生通道 + 非 Server 模式。
    /// 这修掉了历史实现"只看 UseNativeIpc"导致非 Windows 平台构造空传输的问题。
    /// </summary>
    /// <param name="useNativeIpc">用户开关。</param>
    /// <param name="hasNativeChannel">平台能力。</param>
    /// <param name="isServerMode">是否 Server 模式。</param>
    /// <param name="expected">期望判定结果。</param>
    [Test]
    [Arguments(true, true, false, true)]
    [Arguments(false, true, false, false)]
    [Arguments(true, false, false, false)]
    [Arguments(true, true, true, false)]
    public async Task UseNativeIpcTransport_JudgesByCapabilityAndMode(
        bool useNativeIpc,
        bool hasNativeChannel,
        bool isServerMode,
        bool expected)
    {
        var strategy = new DefaultIpcTransportStrategy();
        var context = new IpcTransportContext
        {
            UseNativeIpc = useNativeIpc,
            HasNativeMessageChannel = hasNativeChannel,
            IsServerMode = isServerMode,
        };

        await Assert.That(strategy.UseNativeIpcTransport(context)).IsEqualTo(expected);
    }

    /// <summary>
    /// 事件监听器：DI 提供的监听器先注册，EventIPC 兜底其后（顺序即注册顺序）。
    /// </summary>
    [Test]
    public async Task CreateEventListeners_AppendsEventIpcFallbackAfterProvided()
    {
        var strategy = new DefaultIpcTransportStrategy();
        var provided = new RecordingListener();
        var context = new IpcTransportContext { ProvidedEventListener = provided };

        var listeners = strategy.CreateEventListeners(context);

        await Assert.That(listeners.Count).IsEqualTo(2);
        await Assert.That(listeners[0]).IsSameReferenceAs(provided);
        await Assert.That(listeners[1]).IsTypeOf<EventIPCTransport>();
    }

    /// <summary>
    /// 无 DI 监听器时仍只有 EventIPC 兜底一个（保证桌面端事件必达）。
    /// </summary>
    [Test]
    public async Task CreateEventListeners_WithoutProvided_OnlyFallback()
    {
        var strategy = new DefaultIpcTransportStrategy();

        var listeners = strategy.CreateEventListeners(new IpcTransportContext());

        await Assert.That(listeners.Count).IsEqualTo(1);
        await Assert.That(listeners[0]).IsTypeOf<EventIPCTransport>();
    }

    /// <summary>
    /// 装配链端到端：<see cref="ServiceProviderBinder"/> 经策略把 DI 监听器接进事件处理器，
    /// 派发事件后监听器确实收到（防止"策略写了但装配点没接"）。
    /// </summary>
    [Test]
    public async Task ServiceProviderBinder_WiresEventListenersThroughStrategy()
    {
        var provided = new RecordingListener();
        using var provider = new ServiceCollection()
            .AddSingleton<IWailsEventListener>(provided)
            .BuildServiceProvider();

        var app = new Wails.Net.Application.Application(new Options.ApplicationOptions());
        ServiceProviderBinder.Bind(app, provider);

        app.Events.Emit("strategy:test", "payload");

        // 前端事件经事件邮箱异步派发（P0-1 硬化：Emit 不阻塞调用方），需等待消费侧完成。
        var delivered = await WaitUntilAsync(() => provided.Count == 1);

        await Assert.That(delivered).IsTrue();
        await Assert.That(provided.LastEventName).IsEqualTo("strategy:test");
    }

    /// <summary>
    /// 有界等待条件成立（事件邮箱消费侧异步，避免依赖固定 sleep）。
    /// </summary>
    /// <param name="condition">待满足的条件。</param>
    /// <param name="timeoutMs">超时毫秒数。</param>
    /// <returns>条件在超时前成立时为 true。</returns>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    /// <summary>
    /// 装配链端到端：DI 注册的自定义策略必须被采用（而不是永远用默认策略）。
    /// </summary>
    [Test]
    public async Task ServiceProviderBinder_AdoptsRegisteredStrategy()
    {
        var custom = new StubStrategy();
        using var provider = new ServiceCollection()
            .AddSingleton<IIpcTransportStrategy>(custom)
            .BuildServiceProvider();

        var app = new Wails.Net.Application.Application(new Options.ApplicationOptions());
        ServiceProviderBinder.Bind(app, provider);

        await Assert.That(app.IpcStrategySlot).IsSameReferenceAs(custom);
        await Assert.That(custom.EventListenerCalls).IsEqualTo(1);
    }

    /// <summary>记录事件派发的监听器替身。</summary>
    private sealed class RecordingListener : IWailsEventListener
    {
        /// <summary>已收到的事件数量。</summary>
        public int Count { get; private set; }

        /// <summary>最后一次收到的事件名。</summary>
        public string? LastEventName { get; private set; }

        /// <inheritdoc />
        public void NotifyEvent(string eventName, object? data, uint? senderWindowId = null, uint? targetWindowId = null)
        {
            Count++;
            LastEventName = eventName;
        }
    }

    /// <summary>自定义策略替身：验证 DI 注册的策略被采用。</summary>
    private sealed class StubStrategy : IIpcTransportStrategy
    {
        /// <summary>事件监听器装配被调用的次数。</summary>
        public int EventListenerCalls { get; private set; }

        /// <inheritdoc />
        public ITransport? CreatePrimaryTransport(IpcTransportContext context) => null;

        /// <inheritdoc />
        public IReadOnlyList<ITransport> CreateFallbackTransports(IpcTransportContext context) => [];

        /// <inheritdoc />
        public IReadOnlyList<IWailsEventListener> CreateEventListeners(IpcTransportContext context)
        {
            EventListenerCalls++;
            return [];
        }
    }
}
