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

    // ============================================================
    // P0-B1: CancelCall 路由测试
    // 对应 Wails v3 Go 版本 messageprocessor_call.go processCallCancelMethod
    // ============================================================

    /// <summary>
    /// 测试用服务：包含接受 CancellationToken 的长操作方法。
    /// </summary>
    private class CancellableService
    {
        /// <summary>
        /// 长操作方法，等待 CancellationToken 取消或超时。
        /// </summary>
        public async Task<string> LongOperation(CancellationToken cancellationToken)
        {
            await Task.Delay(5000, cancellationToken);
            return "completed";
        }

        /// <summary>
        /// 立即完成的方法，用于测试取消已完成调用。
        /// </summary>
        public string QuickMethod() => "quick";
    }

    /// <summary>
    /// 取消运行中调用应触发 CancellationToken 取消信号。
    /// 对应 Wails v3 processCallCancelMethod 中的 cancel() 调用。
    /// </summary>
    [Test]
    [Timeout(10000)]
    public async Task CancelCall_RunningCall_CancelsCancellationToken(CancellationToken testCancellationToken)
    {
        var bindings = new BindingManager();
        bindings.Add(new CancellableService());
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);

        var methodName = GetBoundMethodName(bindings, "LongOperation");
        var callMessage = CreateMessage("call-1", "call",
            $$"""{"name":"{{methodName}}","args":[]}""");

        // 异步启动调用（不等待完成）
        var callTask = processor.ProcessAsync(callMessage);

        // 给绑定方法一点时间启动并注册到 _runningCalls
        await Task.Delay(100);

        // 发送取消消息
        var cancelMessage = CreateMessage("cancel-1", "cancel",
            """{"callId":"call-1"}""");
        var cancelResponse = await processor.ProcessAsync(cancelMessage);

        // 取消请求应返回成功
        await Assert.That(cancelResponse).IsNotNull();
        await Assert.That(cancelResponse!.Type).IsEqualTo("response");
        await Assert.That(cancelResponse.Result["error"]).IsNull();

        // 等待原调用完成（应因取消而返回错误）
        var callResponse = await callTask;

        await Assert.That(callResponse).IsNotNull();
        await Assert.That(callResponse!.Type).IsEqualTo("error");
        var errorDict = callResponse.Result["error"] as Dictionary<string, object?>;
        await Assert.That(errorDict).IsNotNull();
        await Assert.That(errorDict!["message"]?.ToString()).Contains("取消");
    }

    /// <summary>
    /// 取消不存在的 callId 应幂等返回成功（与 Wails v3 行为一致）。
    /// </summary>
    [Test]
    public async Task CancelCall_NonExistentCallId_ReturnsSuccess()
    {
        var processor = CreateProcessor();
        var cancelMessage = CreateMessage("cancel-x", "cancel",
            """{"callId":"non-existent-call-id"}""");

        var response = await processor.ProcessAsync(cancelMessage);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Type).IsEqualTo("response");
        await Assert.That(response.Result["error"]).IsNull();
        await Assert.That(response.Result["result"]?.ToString()).IsEqualTo("True");
    }

    /// <summary>
    /// 缺少 callId 字段的取消消息应返回 TypeError。
    /// </summary>
    [Test]
    public async Task CancelCall_MissingCallId_ReturnsTypeError()
    {
        var processor = CreateProcessor();
        var cancelMessage = CreateMessage("cancel-y", "cancel", """{}""");

        var response = await processor.ProcessAsync(cancelMessage);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Type).IsEqualTo("error");
        var errorDict = response.Result["error"] as Dictionary<string, object?>;
        await Assert.That(errorDict).IsNotNull();
        await Assert.That(errorDict!["kind"]?.ToString()).IsEqualTo(CallErrorKind.TypeError.ToString());
    }

    /// <summary>
    /// 无效 JSON 载荷的取消消息应返回 TypeError。
    /// </summary>
    [Test]
    public async Task CancelCall_InvalidPayload_ReturnsTypeError()
    {
        var processor = CreateProcessor();
        var cancelMessage = new Message
        {
            Id = "cancel-z",
            Type = "cancel",
            Payload = JsonDocument.Parse("\"not-an-object\"").RootElement.Clone()
        };

        var response = await processor.ProcessAsync(cancelMessage);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Type).IsEqualTo("error");
    }

    /// <summary>
    /// 已完成的调用被取消时应幂等返回成功（调用已从 _runningCalls 移除）。
    /// </summary>
    [Test]
    public async Task CancelCall_AlreadyCompletedCall_ReturnsSuccess()
    {
        var bindings = new BindingManager();
        bindings.Add(new CancellableService());
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);

        var methodName = GetBoundMethodName(bindings, "QuickMethod");
        var callMessage = CreateMessage("call-2", "call",
            $$"""{"name":"{{methodName}}","args":[]}""");

        // 完成调用
        var callResponse = await processor.ProcessAsync(callMessage);
        await Assert.That(callResponse!.Result["result"]?.ToString()).IsEqualTo("quick");

        // 现在取消已完成调用应幂等返回成功
        var cancelMessage = CreateMessage("cancel-2", "cancel",
            """{"callId":"call-2"}""");
        var cancelResponse = await processor.ProcessAsync(cancelMessage);

        await Assert.That(cancelResponse).IsNotNull();
        await Assert.That(cancelResponse!.Type).IsEqualTo("response");
        await Assert.That(cancelResponse.Result["error"]).IsNull();
    }

    /// <summary>
    /// StopAsync 应取消所有运行中调用（通过 _cts 链接传递取消信号）。
    /// 对应 Wails v3 中应用关闭时所有运行中调用应被取消。
    /// </summary>
    [Test]
    [Timeout(10000)]
    public async Task StopAsync_CancelsRunningCalls(CancellationToken testCancellationToken)
    {
        var bindings = new BindingManager();
        bindings.Add(new CancellableService());
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);

        var methodName = GetBoundMethodName(bindings, "LongOperation");
        var callMessage = CreateMessage("call-3", "call",
            $$"""{"name":"{{methodName}}","args":[]}""");

        // 异步启动调用
        var callTask = processor.ProcessAsync(callMessage);
        await Task.Delay(100);

        // 停止处理器应触发运行中调用被取消
        await processor.StopAsync();

        var callResponse = await callTask;
        await Assert.That(callResponse).IsNotNull();
        await Assert.That(callResponse!.Type).IsEqualTo("error");
        var errorDict = callResponse.Result["error"] as Dictionary<string, object?>;
        await Assert.That(errorDict).IsNotNull();
        await Assert.That(errorDict!["message"]?.ToString()).Contains("取消");
    }

    /// <summary>
    /// Cancel 消息类型常量应有正确值。
    /// </summary>
    [Test]
    public async Task MessageTypes_CancelConstant_HasCorrectValue()
    {
        await Assert.That(MessageProcessor.MessageTypes.Cancel).IsEqualTo("cancel");
    }
}
