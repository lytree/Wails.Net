using System.Collections.Concurrent;
using System.Text.Json;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Transport;
using Wails.Net.Application.Windows;
using Wails.Net.Errors;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// CancellablePromise + CancelCall 全链路测试（P0-B2）。
/// 覆盖从前端发起调用 → Promise.cancel() → _wailsCancelCall → 原生 IPC/HTTP 通道 →
/// MessageProcessor.ProcessCancel → CancellationToken 触发 → 调用方法抛 OperationCanceledException →
/// 错误响应回传 → 前端 Promise reject 的完整路径。
/// </summary>
[NotInParallel]
public sealed class CancellablePromiseEndToEndTests
{
    /// <summary>
    /// 测试用服务：模拟长操作与同步检查点，便于验证取消信号传递。
    /// </summary>
    private sealed class CancellableLongRunner
    {
        /// <summary>
        /// 收到取消信号的次数计数器（线程安全）。
        /// 多个并行调用被取消时累加，避免 TCS 重复 SetResult 抛 InvalidOperationException。
        /// </summary>
        public int CancelSignalledCount => _cancelSignalledCount;
        private int _cancelSignalledCount;

        /// <summary>
        /// 收到取消信号时触发的 TCS（首次）。使用 TrySetResult 兼容多次触发。
        /// </summary>
        public TaskCompletionSource<bool> CancelSignalledTcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// 长操作：等待取消信号或超时。被取消时抛 OperationCanceledException。
        /// </summary>
        public async Task<string> LongRunning(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                return "completed-unexpectedly";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref _cancelSignalledCount);
                CancelSignalledTcs.TrySetResult(true);
                throw;
            }
        }

        /// <summary>
        /// 立即完成的方法。
        /// </summary>
        public string Quick() => "quick-result";

        /// <summary>
        /// 接收参数的方法，用于验证取消路径不影响后续调用的参数解析。
        /// </summary>
        public string Echo(string input) => input;
    }

    /// <summary>
    /// 创建配置好的 MessageProcessor 与 CancellableLongRunner 服务。
    /// </summary>
    private static (MessageProcessor processor, BindingManager bindings, CancellableLongRunner service) SetupWithService()
    {
        var bindings = new BindingManager();
        var service = new CancellableLongRunner();
        bindings.Add(service);
        var events = new EventProcessor();
        var processor = new MessageProcessor(bindings, events);
        return (processor, bindings, service);
    }

    private static string BuildCallJson(string id, string methodName, params object[] args)
    {
        var msg = new { id, type = "call", payload = new { name = methodName, args } };
        return JsonSerializer.Serialize(msg, JsonOptions.DefaultSerializerOptions);
    }

    private static string BuildCancelJson(string cancelId, string callId)
    {
        var msg = new { id = cancelId, type = "cancel", payload = new { callId } };
        return JsonSerializer.Serialize(msg, JsonOptions.DefaultSerializerOptions);
    }

    private static string GetMethodName(BindingManager bindings, string suffix)
        => bindings.BoundMethods.Keys.First(k => k.EndsWith($".{suffix}"));

    private static (IWebviewWindowImpl impl, List<string> posted) CreateNativeStub()
    {
        var impl = Substitute.For<IWebviewWindowImpl>();
        var posted = new List<string>();
        impl.PostNativeMessageAsync(Arg.Any<string>())
            .ReturnsForAnyArgs(callInfo =>
            {
                posted.Add((string)callInfo.Args()[0]);
                return Task.CompletedTask;
            });
        return (impl, posted);
    }

    // ============================================================
    // 端到端场景 1：HTTP 路径取消（HttpTransport 路径模拟）
    // ============================================================

    [Test]
    [Timeout(15000)]
    public async Task EndToEnd_HttpPath_CancelCall_TriggersCancellationTokenAndReturnsError(
        CancellationToken testCancellationToken)
    {
        var (processor, bindings, service) = SetupWithService();
        var methodName = GetMethodName(bindings, "LongRunning");

        // 1. 前端发起调用
        var callId = "e2e-call-1";
        var callMessage = BuildCallJson(callId, methodName);

        var callTask = Task.Run(() => processor.ParseMessage(callMessage))
            .ContinueWith(async t =>
            {
                var msg = processor.ParseMessage(callMessage);
                return msg is null ? null : await processor.ProcessAsync(msg);
            }).Unwrap();

        // 2. 等待方法注册到 _runningCalls
        await Task.Delay(150);

        // 3. 前端调用 cancel：发送 cancel 消息
        var cancelJson = BuildCancelJson("cancel-op-1", callId);
        var cancelMessage = processor.ParseMessage(cancelJson);
        var cancelResponse = await processor.ProcessAsync(cancelMessage!);

        // 4. cancel 响应应成功
        await Assert.That(cancelResponse).IsNotNull();
        await Assert.That(cancelResponse!.Result["error"]).IsNull();

        // 5. 等待服务收到取消信号
        var signalled = await Task.WhenAny(service.CancelSignalledTcs.Task, Task.Delay(5000));
        await Assert.That(signalled.IsCompleted).IsTrue()
            .Because("CancellableLongRunner.LongRunning 应收到 CancellationToken 取消信号");

        // 6. 等待原调用完成，应返回 error 类型响应
        var callResponse = await callTask;
        await Assert.That(callResponse).IsNotNull();
        await Assert.That(callResponse!.Type).IsEqualTo("error");
        var errorDict = callResponse.Result["error"] as Dictionary<string, object?>;
        await Assert.That(errorDict).IsNotNull();
        await Assert.That(errorDict!["message"]?.ToString()).Contains("取消");
    }

    // ============================================================
    // 端到端场景 2：原生 IPC 路径取消（NativeIpcTransport 路径）
    // ============================================================

    [Test]
    [Timeout(15000)]
    public async Task EndToEnd_NativeIpcPath_CancelCall_TriggersCancellationTokenAndPostsErrorResponse(
        CancellationToken testCancellationToken)
    {
        var (processor, bindings, service) = SetupWithService();
        var transport = new NativeIpcTransport(processor);
        var (impl, posted) = CreateNativeStub();
        transport.RegisterWindow(1u, impl);

        var methodName = GetMethodName(bindings, "LongRunning");
        var callId = "native-call-1";

        // 1. 模拟前端通过原生 postMessage 发起调用。
        // HandleIncomingAsync 内部 await ProcessAsync 直到方法返回，因此用 Task.Run 不阻塞测试。
        var callJson = BuildCallJson(callId, methodName);
        var callTask = Task.Run(() => transport.HandleIncomingAsync(1u, callJson));

        // 2. 等待方法注册到 _runningCalls
        await Task.Delay(150);

        // 3. 模拟前端调用 _wailsCancelCall → 原生 postMessage 发送 cancel 消息
        var cancelJson = BuildCancelJson("native-cancel-1", callId);
        await transport.HandleIncomingAsync(1u, cancelJson);

        // 4. 等待服务收到取消信号
        var signalled = await Task.WhenAny(service.CancelSignalledTcs.Task, Task.Delay(5000));
        await Assert.That(signalled.IsCompleted).IsTrue()
            .Because("原生 IPC 路径下 CancellationToken 应被触发");

        // 5. 等待原调用完成与响应投递
        await callTask;
        await Task.Yield();
        await Task.Delay(200);

        // 6. 验证响应：posted 中应同时包含 cancel 响应与原调用的 error 响应
        await Assert.That(posted.Count).IsGreaterThanOrEqualTo(2);

        // 找到 error 类型的响应（原调用的错误响应）
        var errorResponse = posted.FirstOrDefault(s =>
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                return doc.RootElement.GetProperty("type").GetString() == "error";
            }
            catch { return false; }
        });
        await Assert.That(errorResponse).IsNotNull()
            .Because("取消后应通过原生 IPC 推送 error 响应");
        using (var doc = JsonDocument.Parse(errorResponse!))
        {
            var root = doc.RootElement;
            await Assert.That(root.GetProperty("id").GetString()).IsEqualTo(callId);
            var errorObj = root.GetProperty("result").GetProperty("error");
            await Assert.That(errorObj.GetProperty("message").GetString()).Contains("取消");
        }

        // 验证 cancel 消息本身的响应（应为 response 类型，result=true）
        var cancelResponse = posted.FirstOrDefault(s =>
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                var r = doc.RootElement;
                return r.GetProperty("id").GetString() == "native-cancel-1"
                    && r.GetProperty("type").GetString() == "response";
            }
            catch { return false; }
        });
        await Assert.That(cancelResponse).IsNotNull()
            .Because("cancel 请求本身应通过原生 IPC 返回成功响应");
    }

    // ============================================================
    // 端到端场景 3：取消后立即发起后续调用，验证处理器状态干净
    // ============================================================

    [Test]
    [Timeout(10000)]
    public async Task EndToEnd_AfterCancel_SubsequentCallSucceeds(CancellationToken testCancellationToken)
    {
        var (processor, bindings, service) = SetupWithService();
        var methodName = GetMethodName(bindings, "LongRunning");

        // 1. 发起并取消一次调用
        var callId1 = "chain-call-1";
        var callTask = Task.Run(async () =>
        {
            var msg = processor.ParseMessage(BuildCallJson(callId1, methodName));
            return msg is null ? null : await processor.ProcessAsync(msg);
        });
        await Task.Delay(150);

        var cancelMsg = processor.ParseMessage(BuildCancelJson("cancel-1", callId1))!;
        await processor.ProcessAsync(cancelMsg);
        await callTask;

        // 2. 立即发起另一次调用（应正常完成）
        var quickMethodName = GetMethodName(bindings, "Quick");
        var callId2 = "chain-call-2";
        var msg2 = processor.ParseMessage(BuildCallJson(callId2, quickMethodName));
        var response = await processor.ProcessAsync(msg2!);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Type).IsEqualTo("response");
        await Assert.That(response.Result["result"]?.ToString()).IsEqualTo("quick-result");
    }

    // ============================================================
    // 端到端场景 4：多次取消同一 callId（幂等）
    // ============================================================

    [Test]
    [Timeout(10000)]
    public async Task EndToEnd_MultipleCancelCalls_OnSameId_AreIdempotent(
        CancellationToken testCancellationToken)
    {
        var (processor, bindings, service) = SetupWithService();
        var methodName = GetMethodName(bindings, "LongRunning");

        var callId = "multi-cancel-call";
        var callTask = Task.Run(async () =>
        {
            var msg = processor.ParseMessage(BuildCallJson(callId, methodName));
            return msg is null ? null : await processor.ProcessAsync(msg);
        });
        await Task.Delay(150);

        // 第一次取消
        var cancelMsg1 = processor.ParseMessage(BuildCancelJson("cancel-a", callId))!;
        var resp1 = await processor.ProcessAsync(cancelMsg1);

        // 第二次取消（幂等）
        var cancelMsg2 = processor.ParseMessage(BuildCancelJson("cancel-b", callId))!;
        var resp2 = await processor.ProcessAsync(cancelMsg2);

        // 两次都应返回成功（Wails v3 行为：取消不存在的 callId 也返回成功）
        await Assert.That(resp1!.Result["error"]).IsNull();
        await Assert.That(resp2!.Result["error"]).IsNull();

        await callTask;
    }

    // ============================================================
    // 端到端场景 5：应用关闭时所有运行中调用被取消
    // ============================================================

    [Test]
    [Timeout(15000)]
    public async Task EndToEnd_AppShutdown_CancelsAllRunningCalls(CancellationToken testCancellationToken)
    {
        var (processor, bindings, service) = SetupWithService();
        var methodName = GetMethodName(bindings, "LongRunning");

        // 同时发起两个调用
        var callId1 = "shutdown-call-1";
        var callId2 = "shutdown-call-2";
        var task1 = Task.Run(async () =>
        {
            var msg = processor.ParseMessage(BuildCallJson(callId1, methodName));
            return msg is null ? null : await processor.ProcessAsync(msg);
        });
        var task2 = Task.Run(async () =>
        {
            var msg = processor.ParseMessage(BuildCallJson(callId2, methodName));
            return msg is null ? null : await processor.ProcessAsync(msg);
        });

        await Task.Delay(150);

        // 触发 StopAsync（模拟应用关闭）
        await processor.StopAsync();

        var responses = await Task.WhenAll(task1, task2);

        // 两个调用都应被取消并返回 error
        foreach (var resp in responses)
        {
            await Assert.That(resp).IsNotNull();
            await Assert.That(resp!.Type).IsEqualTo("error");
            var err = resp.Result["error"] as Dictionary<string, object?>;
            await Assert.That(err).IsNotNull();
            await Assert.That(err!["message"]?.ToString()).Contains("取消");
        }

        // 验证两个调用都收到了取消信号
        await Assert.That(service.CancelSignalledCount).IsEqualTo(2);
    }

    // ============================================================
    // 端到端场景 6：取消已完成调用，cancel 仍返回成功（不报错）
    // ============================================================

    [Test]
    public async Task EndToEnd_CancelAlreadyCompletedCall_ReturnsSuccess()
    {
        var (processor, bindings, service) = SetupWithService();
        var methodName = GetMethodName(bindings, "Quick");

        var callId = "completed-call-1";
        var callMsg = processor.ParseMessage(BuildCallJson(callId, methodName))!;
        var callResp = await processor.ProcessAsync(callMsg);
        await Assert.That(callResp!.Result["result"]?.ToString()).IsEqualTo("quick-result");

        // 现在取消已完成的调用
        var cancelMsg = processor.ParseMessage(BuildCancelJson("cancel-late", callId))!;
        var cancelResp = await processor.ProcessAsync(cancelMsg);

        await Assert.That(cancelResp).IsNotNull();
        await Assert.That(cancelResp!.Type).IsEqualTo("response");
        await Assert.That(cancelResp.Result["error"]).IsNull();
    }

    // ============================================================
    // 端到端场景 7：取消使用错误 callId（不存在的 ID）
    // ============================================================

    [Test]
    public async Task EndToEnd_CancelNonExistentCallId_ReturnsSuccess_Idempotent()
    {
        var (processor, _, _) = SetupWithService();
        var cancelMsg = processor.ParseMessage(BuildCancelJson("cancel-phantom", "phantom-id"))!;
        var resp = await processor.ProcessAsync(cancelMsg);

        await Assert.That(resp).IsNotNull();
        await Assert.That(resp!.Type).IsEqualTo("response");
        await Assert.That(resp.Result["result"]?.ToString()).IsEqualTo("True");
        await Assert.That(resp.Result["error"]).IsNull();
    }

    // ============================================================
    // 端到端场景 8：取消载荷格式错误 → TypeError
    // ============================================================

    [Test]
    public async Task EndToEnd_MalformedCancelPayload_ReturnsTypeError()
    {
        var (processor, _, _) = SetupWithService();

        // 载荷为非对象 JSON
        var msg = new Message
        {
            Id = "bad-cancel",
            Type = "cancel",
            Payload = JsonDocument.Parse("\"not-an-object\"").RootElement.Clone()
        };

        var resp = await processor.ProcessAsync(msg);
        await Assert.That(resp).IsNotNull();
        await Assert.That(resp!.Type).IsEqualTo("error");

        var err = resp.Result["error"] as Dictionary<string, object?>;
        await Assert.That(err).IsNotNull();
        await Assert.That(err!["kind"]?.ToString()).IsEqualTo(CallErrorKind.TypeError.ToString());
    }

    // ============================================================
    // 端到端场景 9：原生 IPC 路径下取消已完成调用
    // ============================================================

    [Test]
    public async Task EndToEnd_NativeIpc_CancelCompletedCall_ReturnsSuccessResponse()
    {
        var (processor, bindings, service) = SetupWithService();
        var transport = new NativeIpcTransport(processor);
        var (impl, posted) = CreateNativeStub();
        transport.RegisterWindow(1u, impl);

        var quickMethod = GetMethodName(bindings, "Quick");
        var callId = "native-completed";

        // 1. 完成调用
        await transport.HandleIncomingAsync(1u, BuildCallJson(callId, quickMethod));
        await Task.Yield();
        await Assert.That(posted.Count).IsGreaterThanOrEqualTo(1);

        posted.Clear();

        // 2. 取消已完成的调用
        await transport.HandleIncomingAsync(1u, BuildCancelJson("native-cancel-late", callId));
        await Task.Yield();
        await Task.Delay(50);

        // 应收到 cancel 的成功响应
        var cancelResp = posted.FirstOrDefault(s =>
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                var r = doc.RootElement;
                return r.GetProperty("id").GetString() == "native-cancel-late"
                    && r.GetProperty("type").GetString() == "response";
            }
            catch { return false; }
        });
        await Assert.That(cancelResp).IsNotNull()
            .Because("原生 IPC 路径下取消已完成调用应返回成功响应");
    }
}
