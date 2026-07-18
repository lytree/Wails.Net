using System.Text.Json;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Transport;
using Wails.Net.Runtime.Js;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// Server 模式事件 API 完善的单元测试（P0-D）。
/// 覆盖 Application.BroadcastEvent、WebSocketTransport.WebSocketUrl、
/// GenerateRuntimeJs 注入 WebSocketUrl、UseServerModeTransport 扩展、
/// 以及 Server 模式下事件从后端到前端 WebSocket 客户端的完整链路。
/// </summary>
[NotInParallel]
public sealed class ServerModeEventApiTests
{
    /// <summary>
    /// 创建一个不带 DI 的最小 Application 实例用于测试。
    /// 通过 ServerPlatformApp 触发 IsServerMode=true 路径。
    /// </summary>
    private static (Application app, ServerPlatformApp platform) CreateServerModeApplication()
    {
        var options = new ApplicationOptions { Name = "TestServer" };
        var app = new Application(options);
        var platform = new ServerPlatformApp(options);
        app.SetPlatformApp(platform);
        return (app, platform);
    }

    /// <summary>
    /// 验证 Application.BroadcastEvent 等价于 Events.Emit。
    /// 通过 EventProcessor.On 订阅事件并触发 BroadcastEvent，确认回调被调用。
    /// </summary>
    [Test]
    public async Task BroadcastEvent_DelegatesToEventsEmit_AndDeliversToLocalSubscriber()
    {
        var (app, _) = CreateServerModeApplication();

        var received = new List<(string Name, object? Data)>();
        app.Events.On("test-event", evt =>
        {
            received.Add((evt.Name, evt.Data));
        });

        app.BroadcastEvent("test-event", new { message = "hello" });

        await Assert.That(received.Count).IsEqualTo(1);
        await Assert.That(received[0].Name).IsEqualTo("test-event");
        await Assert.That(received[0].Data is not null).IsTrue();
    }

    /// <summary>
    /// 验证 BroadcastEvent 在无监听器时不抛异常。
    /// </summary>
    [Test]
    public async Task BroadcastEvent_NoListeners_DoesNotThrow()
    {
        var (app, _) = CreateServerModeApplication();

        await Assert.That(() => app.BroadcastEvent("orphan-event", null)).ThrowsNothing();
    }

    /// <summary>
    /// 验证 BroadcastEvent 通知已注册的 IWailsEventListener。
    /// 模拟 WebSocketTransport 的事件监听器被追加后，BroadcastEvent 应触发其 NotifyEvent。
    /// </summary>
    [Test]
    public async Task BroadcastEvent_NotifiesRegisteredTransportListener()
    {
        var (app, _) = CreateServerModeApplication();

        var listener = Substitute.For<IWailsEventListener>();
        var received = new List<(string Name, object? Data)>();
        listener.When(x => x.NotifyEvent(Arg.Any<string>(), Arg.Any<object?>()))
            .Do(callInfo =>
            {
                received.Add(((string)callInfo.Args()[0], callInfo.Args()[1]));
            });

        app.Events.AddWailsEventListener(listener);

        app.BroadcastEvent("ws-event", new { payload = 42 });

        await Assert.That(received.Count).IsEqualTo(1);
        await Assert.That(received[0].Name).IsEqualTo("ws-event");
    }

    /// <summary>
    /// 验证 WebSocketTransport.WebSocketUrl 返回正确的 ws:// URL 格式。
    /// </summary>
    [Test]
    public async Task WebSocketTransport_WebSocketUrl_ReturnsWsSchemeWithPath()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var broadcaster = new WebSocketBroadcaster();
        var transport = new WebSocketTransport(processor, broadcaster);

        // 启动传输层以分配端口
        await transport.StartAsync();

        try
        {
            var url = transport.WebSocketUrl;
            await Assert.That(url).Contains("ws://localhost:");
            await Assert.That(url).Contains("/wails/ws");
            await Assert.That(url).Contains(transport.Port.ToString());
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    /// <summary>
    /// 验证 WebSocketTransport.BaseUrl 与 WebSocketUrl 端口一致。
    /// </summary>
    [Test]
    public async Task WebSocketTransport_BaseUrlAndWebSocketUrl_ShareSamePort()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var broadcaster = new WebSocketBroadcaster();
        var transport = new WebSocketTransport(processor, broadcaster);

        await transport.StartAsync();

        try
        {
            await Assert.That(transport.BaseUrl).Contains(transport.Port.ToString());
            await Assert.That(transport.WebSocketUrl).Contains(transport.Port.ToString());
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    /// <summary>
    /// 验证 GenerateRuntimeJs 在 Server 模式下若设置 WebSocketTransport，
    /// 会将 WebSocketUrl 注入到生成的 JS 代码中。
    /// </summary>
    [Test]
    public async Task GenerateRuntimeJs_ServerModeWithWebSocketTransport_InjectsWebSocketUrl()
    {
        var (app, _) = CreateServerModeApplication();

        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var broadcaster = new WebSocketBroadcaster();
        var transport = new WebSocketTransport(processor, broadcaster);

        await transport.StartAsync();
        app.Transport = transport;

        try
        {
            var js = app.GenerateRuntimeJs(isDebug: false);

            // 验证 ServerRuntime 模板中的 wsUrl 变量被注入
            await Assert.That(js).Contains("ws://localhost:");
            await Assert.That(js).Contains("/wails/ws");
            await Assert.That(js).Contains("WebSocket 已连接");
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    /// <summary>
    /// 验证 GenerateRuntimeJs 在 Server 模式下无传输层时不注入 ws:// URL。
    /// 确保向后兼容性（用户未设置传输层时不会出现空 URL）。
    /// </summary>
    [Test]
    public async Task GenerateRuntimeJs_ServerModeWithoutTransport_DoesNotInjectWebSocketUrl()
    {
        var (app, _) = CreateServerModeApplication();

        var js = app.GenerateRuntimeJs(isDebug: false);

        // 应该是 ServerRuntime 模板，但 wsUrl 应为空字符串序列化结果
        await Assert.That(js).Contains("WailsNET Server Runtime");
        // 空字符串序列化为 ""，不应包含 ws://localhost: 实际 URL
        await Assert.That(js.Contains("ws://localhost:")).IsFalse();
    }

    /// <summary>
    /// 验证 ServerRuntime 生成的 JS 代码包含 _wailsInvoke、_wailsCancelCall、_wailsOnEvent、_wailsEmitEvent 全套 API。
    /// </summary>
    [Test]
    public async Task ServerRuntime_Generate_ContainsAllAlignedEventApis()
    {
        var options = new RuntimeOptions
        {
            Platform = "server",
            IsServerMode = true,
            WebSocketUrl = "ws://localhost:9999/wails/ws",
            AssetServerUrl = "http://localhost:9999",
        };

        var js = ServerRuntime.Generate(options);

        await Assert.That(js).Contains("window._wailsInvoke");
        await Assert.That(js).Contains("window._wailsCancelCall");
        await Assert.That(js).Contains("window._wailsOnEvent");
        await Assert.That(js).Contains("window._wailsEmitEvent");
        await Assert.That(js).Contains("ws://localhost:9999/wails/ws");
    }

    /// <summary>
    /// 验证端到端事件广播：EventProcessor.Emit → WebSocketTransport.NotifyEvent → WebSocketBroadcaster.BroadcastEventAsync。
    /// 使用真实 WebSocketBroadcaster + 桩 WebSocket 客户端，验证消息格式正确。
    /// </summary>
    [Test]
    public async Task EndToEnd_EventEmit_ThroughWebSocketTransport_DeliversToClients()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var broadcaster = new WebSocketBroadcaster();
        var transport = new WebSocketTransport(processor, broadcaster);

        // 将 WebSocketTransport 注册为事件监听器
        events.AddWailsEventListener(transport);

        // 添加一个桩 WebSocket 客户端
        var stubWs = Substitute.For<System.Net.WebSockets.WebSocket>();
        var sentSegments = new List<ArraySegment<byte>>();

        stubWs.State.Returns(System.Net.WebSockets.WebSocketState.Open);
        stubWs.When(x => x.SendAsync(Arg.Any<ArraySegment<byte>>(),
                Arg.Any<System.Net.WebSockets.WebSocketMessageType>(),
                Arg.Any<bool>(),
                Arg.Any<System.Threading.CancellationToken>()))
            .Do(callInfo =>
            {
                sentSegments.Add((ArraySegment<byte>)callInfo.Args()[0]);
            });

        broadcaster.AddClient(stubWs);

        // 触发事件
        events.Emit("server-test-event", new { value = "data" });

        // 等待异步广播完成
        await Task.Delay(100);

        // 验证至少一次发送
        await Assert.That(sentSegments.Count).IsGreaterThan(0);

        // 解析发送的 JSON
        var json = System.Text.Encoding.UTF8.GetString(sentSegments[0].Array!, 0, sentSegments[0].Count);
        using var doc = JsonDocument.Parse(json);
        await Assert.That(doc.RootElement.GetProperty("type").GetString()).IsEqualTo("event");
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("server-test-event");
    }

    /// <summary>
    /// 验证 Server 模式下 EventIPCTransport 因无窗口而跳过事件派发。
    /// 这是 EventIPCTransport 与 NativeIpcTransport 的协作约定：
    /// Server 模式无窗口时 EventIPCTransport 应直接返回不执行 ExecJS。
    /// </summary>
    [Test]
    public async Task EventIPCTransport_ServerModeNoWindows_NoOpsSilently()
    {
        // Application 构造函数自动注册为全局实例（_globalApplication = this）
        var (app, _) = CreateServerModeApplication();

        var eventIpc = new EventIPCTransport();

        // 应不抛异常也不执行任何动作（WindowManager 无窗口可枚举）
        await Assert.That(() => eventIpc.NotifyEvent("any-event", null)).ThrowsNothing();
        await Assert.That(() => eventIpc.NotifyEvent("any-event", new { data = "test" })).ThrowsNothing();
    }

    /// <summary>
    /// 验证 Server 模式下 NativeIpcTransport 未注册时，EventIPCTransport 回退到 ExecJS 路径。
    /// 但由于 ServerPlatformApp.WindowManager 返回空窗口列表，实际仍无操作。
    /// </summary>
    [Test]
    public async Task EventIPCTransport_ServerMode_NativeIpcNotRegistered_FallsBackToExecJsButNoWindows()
    {
        var (app, _) = CreateServerModeApplication();

        var eventIpc = new EventIPCTransport();

        // NativeIpcTransport 未注册 → 回退到 ExecJS 路径 → 但无窗口 → 静默返回
        await Assert.That(() => eventIpc.NotifyEvent("test-fallback", "data")).ThrowsNothing();
    }
}
