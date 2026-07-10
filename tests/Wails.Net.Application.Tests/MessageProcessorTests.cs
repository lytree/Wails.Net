using System.Text.Json;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Transport;
using Wails.Net.Errors;

namespace Wails.Net.Application.Tests;

/// <summary>
/// MessageProcessor 的单元测试（TUnit）。
/// 测试消息解析、绑定调用、事件发布、查询处理。
/// </summary>
[NotInParallel]
public sealed class MessageProcessorTests
{
    /// <summary>
    /// 用于测试的服务类。
    /// </summary>
    private class TestService
    {
        public string GetName() => "Wails.Net";
        public int Add(int a, int b) => a + b;
        public string Greet(string name) => $"Hello, {name}!";
    }

    /// <summary>
    /// 创建配置好测试服务的 MessageProcessor 实例。
    /// </summary>
    private static MessageProcessor CreateProcessor()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());
        var events = new EventProcessor();
        return new MessageProcessor(bindings, events);
    }

    /// <summary>
    /// 构造包含指定 JSON 载荷的 Message 对象。
    /// </summary>
    private static Message CreateMessage(string id, string type, string payloadJson)
    {
        return new Message
        {
            Id = id,
            Type = type,
            Payload = JsonSerializer.Deserialize<JsonElement>(payloadJson)
        };
    }

    /// <summary>
    /// 获取绑定方法的实际全名（从 BindingManager 中查询）。
    /// </summary>
    private static string GetBoundMethodName(BindingManager bindings, string methodName)
    {
        return bindings.BoundMethods.Keys.First(k => k.EndsWith($".{methodName}"));
    }

    [Test]
    public async Task ParseMessage_ValidJson_ReturnsMessage()
    {
        var processor = CreateProcessor();
        var json = """{"id":"1","type":"call","payload":{"name":"TestService.GetName","args":[]}}""";

        var message = processor.ParseMessage(json);

        await Assert.That(message).IsNotNull();
        await Assert.That(message!.Id).IsEqualTo("1");
        await Assert.That(message.Type).IsEqualTo("call");
    }

    [Test]
    public async Task ParseMessage_InvalidJson_ReturnsNull()
    {
        var processor = CreateProcessor();

        var message = processor.ParseMessage("not valid json");

        await Assert.That(message).IsNull();
    }

    [Test]
    public async Task ProcessAsync_CallByName_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);

        var methodName = GetBoundMethodName(bindings, "GetName");
        var message = CreateMessage("1", "call",
            $$"""{"name":"{{methodName}}","args":[]}""");

        var response = await processor.ProcessAsync(message);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Id).IsEqualTo("1");
        await Assert.That(response.Type).IsEqualTo("response");
        await Assert.That(response.Result["error"]).IsNull();
        await Assert.That(response.Result["result"]?.ToString()).IsEqualTo("Wails.Net");
    }

    [Test]
    public async Task ProcessAsync_CallByID_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);

        var methodName = GetBoundMethodName(bindings, "Greet");
        var id = BindingManager.FNV1aHash(methodName);
        var message = CreateMessage("2", "call",
            $$"""{"id":{{id}},"args":["World"]}""");

        var response = await processor.ProcessAsync(message);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Result["error"]).IsNull();
        await Assert.That(response.Result["result"]?.ToString()).IsEqualTo("Hello, World!");
    }

    [Test]
    public async Task ProcessAsync_CallWithParameters_ReturnsCorrectResult()
    {
        var bindings = new BindingManager();
        bindings.Add(new TestService());
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);

        var methodName = GetBoundMethodName(bindings, "Add");
        var message = CreateMessage("3", "call",
            $$"""{"name":"{{methodName}}","args":[3,4]}""");

        var response = await processor.ProcessAsync(message);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Result["result"]?.ToString()).IsEqualTo("7");
    }

    [Test]
    public async Task ProcessAsync_CallWithoutIdOrName_ReturnsError()
    {
        var processor = CreateProcessor();
        var message = CreateMessage("4", "call", """{"args":[]}""");

        var response = await processor.ProcessAsync(message);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Type).IsEqualTo("error");
        await Assert.That(response.Result["error"]).IsNotNull();
    }

    [Test]
    public async Task ProcessAsync_CallUnknownMethod_ReturnsReferenceError()
    {
        var processor = CreateProcessor();
        var message = CreateMessage("5", "call", """{"name":"NonExistent.Method","args":[]}""");

        var response = await processor.ProcessAsync(message);

        await Assert.That(response).IsNotNull();
        var errorDict = response!.Result["error"] as Dictionary<string, object?>;
        await Assert.That(errorDict).IsNotNull();
        await Assert.That(errorDict!["kind"]?.ToString()).IsEqualTo(CallErrorKind.ReferenceError.ToString());
    }

    [Test]
    public async Task ProcessAsync_EventMessage_EmitsEvent()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);

        var received = false;
        events.On("test-event", _ => received = true);

        var message = CreateMessage("6", "event",
            """{"name":"test-event","data":null}""");

        await processor.ProcessAsync(message);

        await Assert.That(received).IsTrue();
    }

    [Test]
    public async Task ProcessAsync_EventMessage_DeliversData()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);

        object? receivedData = null;
        events.On("data-event", evt => receivedData = evt.Data);

        var message = CreateMessage("7", "event",
            """{"name":"data-event","data":"hello"}""");

        await processor.ProcessAsync(message);

        await Assert.That(receivedData?.ToString()).IsEqualTo("hello");
    }

    [Test]
    public async Task ProcessAsync_EventMessage_ReturnsNull()
    {
        var processor = CreateProcessor();
        var message = CreateMessage("8", "event",
            """{"name":"any-event","data":null}""");

        var response = await processor.ProcessAsync(message);

        await Assert.That(response).IsNull();
    }

    [Test]
    public async Task ProcessAsync_UnknownType_ReturnsNull()
    {
        var processor = CreateProcessor();
        var message = CreateMessage("9", "unknown", """{}""");

        var response = await processor.ProcessAsync(message);

        await Assert.That(response).IsNull();
    }

    [Test]
    public async Task Start_CanBeCalledSafely()
    {
        var processor = CreateProcessor();

        processor.Start();

        // 验证不抛出异常
        await Assert.That(() => processor.Start()).ThrowsNothing();

        await processor.StopAsync();
    }

    [Test]
    public async Task StopAsync_CanBeCalledMultipleTimes()
    {
        var processor = CreateProcessor();
        processor.Start();

        await processor.StopAsync();
        // 再次停止不应抛出异常
        await processor.StopAsync();
    }

    [Test]
    public async Task Enqueue_AfterStop_DoesNotThrow()
    {
        var processor = CreateProcessor();
        await processor.StopAsync();

        var message = new Message { Id = "1", Type = "call" };
        processor.Enqueue(message);

        await Assert.That(() => processor.Enqueue(message)).ThrowsNothing();
    }

    [Test]
    public async Task ProcessAsync_QueryBindings_ReturnsBoundMethodNames()
    {
        var processor = CreateProcessor();
        var message = CreateMessage("10", "query", """{"query":"bindings"}""");

        var response = await processor.ProcessAsync(message);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Result["result"]).IsNotNull();
    }

    [Test]
    public async Task ProcessAsync_InvalidCallPayload_ReturnsError()
    {
        var processor = CreateProcessor();
        // 提供无法解析为 CallPayload 的载荷
        var message = CreateMessage("11", "call", """{"args":"not-an-array"}""");

        var response = await processor.ProcessAsync(message);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Type).IsEqualTo("error");
    }

    [Test]
    public async Task MessageTypes_Constants_HaveCorrectValues()
    {
        await Assert.That(MessageProcessor.MessageTypes.Call).IsEqualTo("call");
        await Assert.That(MessageProcessor.MessageTypes.Event).IsEqualTo("event");
        await Assert.That(MessageProcessor.MessageTypes.Window).IsEqualTo("window");
        await Assert.That(MessageProcessor.MessageTypes.Query).IsEqualTo("query");
        await Assert.That(MessageProcessor.MessageTypes.Response).IsEqualTo("response");
        await Assert.That(MessageProcessor.MessageTypes.Error).IsEqualTo("error");
    }
}
