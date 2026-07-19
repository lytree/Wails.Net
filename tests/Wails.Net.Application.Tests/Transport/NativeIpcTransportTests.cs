using System.Text.Json;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Transport;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// NativeIpcTransport 的单元测试（TUnit，P0-C2）。
/// 覆盖窗口注册/注销、消息路由、事件广播、错误路径与边界条件。
/// </summary>
[NotInParallel]
public sealed class NativeIpcTransportTests
{
    /// <summary>
    /// 创建 NSubstitute 桩实例，并捕获 SetNativeMessageHandler 的回调与 PostNativeMessageAsync 的入参。
    /// </summary>
    private static (IWebviewWindowImpl impl, List<Func<string, Task>?> handlers, List<string> posted) CreateStub()
    {
        var impl = Substitute.For<IWebviewWindowImpl>();
        var handlers = new List<Func<string, Task>?>();
        var posted = new List<string>();

        // SetNativeMessageHandler 返回 void，使用 When().Do() 模式捕获入参
        impl.When(x => x.SetNativeMessageHandler(Arg.Any<Func<string, Task>?>()))
            .Do(callInfo => handlers.Add((Func<string, Task>?)callInfo.Args()[0]));

        // PostNativeMessageAsync 返回 Task，使用 ReturnsForAnyArgs 捕获消息
        impl.PostNativeMessageAsync(Arg.Any<string>())
            .ReturnsForAnyArgs(callInfo =>
            {
                posted.Add((string)callInfo.Args()[0]);
                return Task.CompletedTask;
            });

        return (impl, handlers, posted);
    }

    /// <summary>
    /// 创建配置好绑定方法的消息处理器。
    /// </summary>
    private static (MessageProcessor processor, BindingManager bindings, EventProcessor events) CreateProcessor()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        return (processor, bindings, events);
    }

    /// <summary>
    /// 构造 call 类型的 JSON 消息字符串。
    /// </summary>
    private static string BuildCallMessageJson(string id, string methodName, params object[] args)
    {
        var msg = new
        {
            id,
            type = "call",
            payload = new { name = methodName, args }
        };
        return JsonSerializer.Serialize(msg, JsonOptions.DefaultSerializerOptions);
    }

    /// <summary>
    /// 获取注册的桩 impl 最近一次接收到的 handler。
    /// </summary>
    private static Func<string, Task> GetLatestHandler(List<Func<string, Task>?> handlers)
    {
        return handlers.Last(h => h is not null)!;
    }

    [Test]
    public async Task Constructor_NullProcessor_ThrowsArgumentNullException()
    {
        await Assert.That(() => new NativeIpcTransport(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task RegisterWindow_StoresImpl_IncreasesCount()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);
        var (impl, handlers, _) = CreateStub();

        transport.RegisterWindow(42u, impl);

        await Assert.That(transport.RegisteredWindowCount).IsEqualTo(1);
        await Assert.That(handlers.Count).IsEqualTo(1);
        await Assert.That(handlers[0]).IsNotNull();
    }

    [Test]
    public async Task RegisterWindow_NullImpl_ThrowsArgumentNullException()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);

        await Assert.That(() => transport.RegisterWindow(1u, null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task RegisterWindow_OverwriteSameId_ReplacesImpl()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);
        var (impl1, _, _) = CreateStub();
        var (impl2, handlers2, _) = CreateStub();

        transport.RegisterWindow(5u, impl1);
        transport.RegisterWindow(5u, impl2);

        await Assert.That(transport.RegisteredWindowCount).IsEqualTo(1);
        // 第二次注册会安装新 handler 到 impl2
        await Assert.That(handlers2.Count).IsEqualTo(1);
    }

    [Test]
    public async Task UnregisterWindow_RemovesImpl_ClearsHandler()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);
        var (impl, handlers, _) = CreateStub();
        transport.RegisterWindow(7u, impl);

        // handlers[0] 为注册时安装的回调
        transport.UnregisterWindow(7u);

        await Assert.That(transport.RegisteredWindowCount).IsEqualTo(0);
        // 卸载时调用 SetNativeMessageHandler(null)
        await Assert.That(handlers.Count).IsEqualTo(2);
        await Assert.That(handlers[1]).IsNull();
    }

    [Test]
    public async Task UnregisterWindow_UnknownId_NoOp()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);

        // 不应抛异常
        transport.UnregisterWindow(999u);
        await Assert.That(transport.RegisteredWindowCount).IsEqualTo(0);
    }

    [Test]
    public async Task PostToWindowAsync_RegisteredWindow_CallsPostNativeMessage()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);
        var (impl, _, posted) = CreateStub();
        transport.RegisterWindow(1u, impl);

        await transport.PostToWindowAsync(1u, """{"hello":"world"}""");

        await Assert.That(posted.Count).IsEqualTo(1);
        await Assert.That(posted[0]).IsEqualTo("""{"hello":"world"}""");
    }

    [Test]
    public async Task PostToWindowAsync_UnknownWindow_SilentlyIgnored()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);

        // 不抛异常，静默忽略
        await transport.PostToWindowAsync(404u, "anything");
    }

    [Test]
    public async Task PostToWindowAsync_NullMessage_ThrowsArgumentNullException()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);
        var (impl, _, _) = CreateStub();
        transport.RegisterWindow(1u, impl);

        await Assert.That(() => transport.PostToWindowAsync(1u, null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task HandleIncomingAsync_InvalidJson_ReturnsEarly_NoPost()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);
        var (impl, _, posted) = CreateStub();
        transport.RegisterWindow(1u, impl);

        await transport.HandleIncomingAsync(1u, "not valid json");

        await Assert.That(posted.Count).IsEqualTo(0);
    }

    [Test]
    public async Task HandleIncomingAsync_NullMessage_ThrowsArgumentNullException()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);

        await Assert.That(() => transport.HandleIncomingAsync(1u, null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task HandleIncomingAsync_ValidCallMessage_PostsResponseBack()
    {
        // 注册一个返回固定值的服务
        var bindings = new BindingManager();
        bindings.Add(new EchoService());
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var transport = new NativeIpcTransport(processor);
        var (impl, _, posted) = CreateStub();
        transport.RegisterWindow(1u, impl);

        var methodName = GeneratedBindingsMetadata.Methods
            .First(m => m.ClassName == "EchoService" && m.MethodName == "Echo").FullName;
        var json = BuildCallMessageJson("call-1", methodName, "hi");

        await transport.HandleIncomingAsync(1u, json);

        // 应至少投递一条响应到 impl
        await Assert.That(posted.Count).IsGreaterThan(0);

        // 解析响应并验证包含 result.result == "hi"
        var respJson = posted[0];
        using var doc = JsonDocument.Parse(respJson);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("id").GetString()).IsEqualTo("call-1");
        var resultObj = root.GetProperty("result");
        await Assert.That(resultObj.GetProperty("result").GetString()).IsEqualTo("hi");
        await Assert.That(resultObj.GetProperty("error").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task HandleIncomingAsync_EventEmit_NoResponsePosted()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);
        var (impl, _, posted) = CreateStub();
        transport.RegisterWindow(99u, impl);

        // event.emit 消息不产生 RPC 响应
        var json = """{"id":"e1","type":"event.emit","payload":{"name":"test","data":null}}""";

        await transport.HandleIncomingAsync(99u, json);

        await Assert.That(posted.Count).IsEqualTo(0);
    }

    [Test]
    public async Task NotifyEvent_NoWindowsRegistered_NoOp()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);

        // 不抛异常
        transport.NotifyEvent("some.event", new { foo = 1 });
    }

    [Test]
    public async Task NotifyEvent_WithRegisteredWindows_BroadcastsToAll()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);
        var (stub1, _, posted1) = CreateStub();
        var (stub2, _, posted2) = CreateStub();
        transport.RegisterWindow(1u, stub1);
        transport.RegisterWindow(2u, stub2);

        transport.NotifyEvent("app.ready", new { pid = 1234 });

        // 等待异步投递完成（fire-and-forget）
        await Task.Yield();
        await Task.Delay(50);

        await Assert.That(posted1.Count).IsEqualTo(1);
        await Assert.That(posted2.Count).IsEqualTo(1);

        // 验证事件载荷格式：{ type: "event", name, data }
        var posted = posted1[0];
        using var doc = JsonDocument.Parse(posted);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("event");
        await Assert.That(root.GetProperty("name").GetString()).IsEqualTo("app.ready");
        await Assert.That(root.GetProperty("data").GetProperty("pid").GetInt32()).IsEqualTo(1234);
    }

    [Test]
    public async Task NotifyEvent_AfterUnregister_NoBroadcastToRemovedWindow()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);
        var (stub1, _, posted1) = CreateStub();
        var (stub2, _, posted2) = CreateStub();
        transport.RegisterWindow(1u, stub1);
        transport.RegisterWindow(2u, stub2);

        transport.UnregisterWindow(1u);
        transport.NotifyEvent("evt", null);

        await Task.Yield();
        await Task.Delay(50);

        await Assert.That(posted1.Count).IsEqualTo(0);
        await Assert.That(posted2.Count).IsEqualTo(1);
    }

    [Test]
    public async Task NotifyEvent_NullData_SerializesWithoutThrowing()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);
        var (stub, _, posted) = CreateStub();
        transport.RegisterWindow(1u, stub);

        transport.NotifyEvent("null.event", null);

        await Task.Yield();
        await Task.Delay(50);

        await Assert.That(posted.Count).IsEqualTo(1);
        var postedJson = posted[0];
        using var doc = JsonDocument.Parse(postedJson);
        await Assert.That(doc.RootElement.GetProperty("data").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task NotifyEvent_WithSenderWindowId_IncludesFieldInPayload()
    {
        // P1-2：验证 senderWindowId 出现在事件载荷中，前端可据此识别事件发起窗口
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);
        var (stub, _, posted) = CreateStub();
        transport.RegisterWindow(1u, stub);

        transport.NotifyEvent("from.window", "payload", senderWindowId: 42u);

        await Task.Yield();
        await Task.Delay(50);

        await Assert.That(posted.Count).IsEqualTo(1);
        var postedJson = posted[0];
        using var doc = JsonDocument.Parse(postedJson);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("event");
        await Assert.That(root.GetProperty("name").GetString()).IsEqualTo("from.window");
        await Assert.That(root.GetProperty("senderWindowId").GetUInt32()).IsEqualTo(42u);
    }

    [Test]
    public async Task NotifyEvent_WithNullSenderWindowId_PayloadContainsNullSenderField()
    {
        // P1-2：应用级事件（无来源窗口）载荷中 senderWindowId 应为 null
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);
        var (stub, _, posted) = CreateStub();
        transport.RegisterWindow(1u, stub);

        transport.NotifyEvent("app.event", null, senderWindowId: null);

        await Task.Yield();
        await Task.Delay(50);

        await Assert.That(posted.Count).IsEqualTo(1);
        var postedJson = posted[0];
        using var doc = JsonDocument.Parse(postedJson);
        await Assert.That(doc.RootElement.GetProperty("senderWindowId").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task RegisterWindow_InstallsHandlerThatCallsHandleIncomingAsync()
    {
        // 端到端验证：注册后，调用 stub.ReceivedHandler 应与直接调用 HandleIncomingAsync 等价
        var bindings = new BindingManager();
        bindings.Add(new EchoService());
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        var transport = new NativeIpcTransport(processor);
        var (stub, handlers, posted) = CreateStub();
        transport.RegisterWindow(7u, stub);

        // 模拟前端通过原生 postMessage 发送的消息
        var methodName = GeneratedBindingsMetadata.Methods
            .First(m => m.ClassName == "EchoService" && m.MethodName == "Echo").FullName;
        var json = BuildCallMessageJson("via-handler", methodName, "from-frontend");

        // 通过注册的 handler 触发（与平台层调用路径一致）
        var handler = GetLatestHandler(handlers);
        await handler(json);

        await Assert.That(posted.Count).IsEqualTo(1);
        using var doc = JsonDocument.Parse(posted[0]);
        await Assert.That(doc.RootElement.GetProperty("id").GetString()).IsEqualTo("via-handler");
        await Assert.That(doc.RootElement.GetProperty("result").GetProperty("result").GetString()).IsEqualTo("from-frontend");
    }

    [Test]
    public async Task SupportsBinary_ReturnsPlatformSpecificValue()
    {
        var (processor, _, _) = CreateProcessor();
        var transport = new NativeIpcTransport(processor);

        // 仅断言不抛异常，具体值由操作系统决定
        var value = transport.SupportsBinary;
        await Assert.That(value).IsEqualTo(OperatingSystem.IsWindows());
    }

    /// <summary>
    /// 用于测试的回显服务，提供 Echo 方法。
    /// 必须为 public 以便源生成器生成调用器代码。
    /// </summary>
    public sealed class EchoService
    {
        [Binding]
        public string Echo(string input) => input;
    }
}
