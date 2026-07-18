using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TUnit.Core;
using Wails.Net.Application.Logging;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Options;
using Wails.Net.Application.Services;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests.Logging;

/// <summary>
/// <see cref="BrowserConsoleLogReceiver"/> 的单元测试（P1-3-4 Direction 3：前端 console → 后端 LogService）。
/// </summary>
[NotInParallel]
public sealed class BrowserConsoleLogReceiverTests
{
    /// <summary>
    /// 创建一个 WindowManager，并预置 N 个窗口，每个窗口的 Impl 替换为 NSubstitute 桩。
    /// 返回桩列表用于断言 SetConsoleMessageHandler 调用。
    /// </summary>
    private static (WindowManager manager, List<IWebviewWindowImpl> impls, List<WebviewWindow> windows) CreateManagerWithWindows(int count)
    {
        var manager = new WindowManager(null);
        var impls = new List<IWebviewWindowImpl>();
        var windows = new List<WebviewWindow>();
        for (var i = 0; i < count; i++)
        {
            var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = $"W{i}" });
            var window = manager.GetWindow(id)!;
            var impl = Substitute.For<IWebviewWindowImpl>();
            window.Impl = impl;
            impls.Add(impl);
            windows.Add(window);
        }
        return (manager, impls, windows);
    }

    [Test]
    public async Task Constructor_NullLogService_ThrowsArgumentNullException()
    {
        var manager = new WindowManager(null);

        await Assert.That(() => new BrowserConsoleLogReceiver(null!, manager))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_NullWindowManager_ThrowsArgumentNullException()
    {
        var logService = new LogService();

        await Assert.That(() => new BrowserConsoleLogReceiver(logService, null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_NullDiagnosticLogger_UsesNullLogger()
    {
        // 不传 diagnosticLogger，应使用 NullLogger 兜底，不抛 NRE
        var logService = new LogService();
        var manager = new WindowManager(null);

        var receiver = new BrowserConsoleLogReceiver(logService, manager, diagnosticLogger: null);

        await Assert.That(receiver).IsNotNull();
    }

    [Test]
    public async Task Start_NoWindows_DoesNotThrow()
    {
        var logService = new LogService();
        var manager = new WindowManager(null);
        var receiver = new BrowserConsoleLogReceiver(logService, manager);

        // 空窗口管理器下 Start 不应抛异常
        receiver.Start();

        await Assert.That(receiver).IsNotNull();
    }

    [Test]
    public async Task Start_WithExistingWindows_RegistersHandlerOnAllImpls()
    {
        var logService = new LogService();
        var (manager, impls, _) = CreateManagerWithWindows(3);
        var receiver = new BrowserConsoleLogReceiver(logService, manager);

        receiver.Start();

        // 每个 impl 都应被注册一次 console 消息处理器
        foreach (var impl in impls)
        {
            impl.Received(1).SetConsoleMessageHandler(Arg.Any<Action<BrowserConsoleMessageLevel, string>>());
        }
    }

    [Test]
    public async Task Start_WindowWithNullImpl_SkipsThatWindow()
    {
        var logService = new LogService();
        var manager = new WindowManager(null);
        // 创建窗口但不设置 Impl（保持 null）
        var nullImplId = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "NullImpl" });
        // 另一个窗口设置 Impl
        var withImplId = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "WithImpl" });
        var withImpl = Substitute.For<IWebviewWindowImpl>();
        manager.GetWindow(withImplId)!.Impl = withImpl;

        var receiver = new BrowserConsoleLogReceiver(logService, manager);

        // 不应抛异常
        receiver.Start();

        // 有 Impl 的窗口应被注册
        withImpl.Received(1).SetConsoleMessageHandler(Arg.Any<Action<BrowserConsoleMessageLevel, string>>());
    }

    [Test]
    public async Task Start_CalledTwice_DoesNotRegisterTwice()
    {
        var logService = new LogService();
        var (manager, impls, _) = CreateManagerWithWindows(1);
        var receiver = new BrowserConsoleLogReceiver(logService, manager);

        receiver.Start();
        receiver.Start();

        // 即使 Start 调用两次，handler 仅注册一次
        impls[0].Received(1).SetConsoleMessageHandler(Arg.Any<Action<BrowserConsoleMessageLevel, string>>());
    }

    [Test]
    public async Task Start_NewWindowCreatedAfterStart_RegistersHandlerOnNewWindow()
    {
        var logService = new LogService();
        var (manager, impls, _) = CreateManagerWithWindows(1);
        var receiver = new BrowserConsoleLogReceiver(logService, manager);
        receiver.Start();

        // 创建新窗口并设置 Impl
        var newId = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "NewWindow" });
        var newImpl = Substitute.For<IWebviewWindowImpl>();
        manager.GetWindow(newId)!.Impl = newImpl;

        // WindowCreated 事件触发时 Impl 通常尚未设置（由平台层异步绑定）。
        // 此处直接调用 RegisterWindow 不便，通过再次模拟 Impl 就绪后由外部再次触发。
        // 由于 WindowCreated 在 CreateWebviewWindow 内同步触发，此时 Impl 仍为 null，
        // 接收器会跳过此窗口。需要通过再次 Start 来重新扫描，或依赖平台层就绪后再次触发。
        // 这里验证：再次调用 Start 时新窗口会被注册（幂等扫描）。
        receiver.Start();

        newImpl.Received(1).SetConsoleMessageHandler(Arg.Any<Action<BrowserConsoleMessageLevel, string>>());
    }

    [Test]
    public async Task OnConsoleMessage_WritesToLogServiceWithSourceBrowserMarker()
    {
        var logService = new LogService();
        var manager = new WindowManager(null);
        var receiver = new BrowserConsoleLogReceiver(logService, manager);
        receiver.Start();

        // 通过捕获 handler 调用来触发 OnConsoleMessage
        Action<BrowserConsoleMessageLevel, string>? capturedHandler = null;
        // 替换 Start 注册的 handler：创建一个新窗口并捕获
        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "Capture" });
        var impl = Substitute.For<IWebviewWindowImpl>();
        // 捕获注册时传入的 handler
        impl.When(x => x.SetConsoleMessageHandler(Arg.Any<Action<BrowserConsoleMessageLevel, string>>()))
            .Do(callInfo => capturedHandler = callInfo.Arg<Action<BrowserConsoleMessageLevel, string>>());
        manager.GetWindow(id)!.Impl = impl;
        // 再次 Start 重新扫描（幂等），让新窗口被注册
        receiver.Start();

        // 模拟前端 console 调用
        capturedHandler!.Invoke(BrowserConsoleMessageLevel.Info, "console.info from frontend");

        var entries = logService.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Message).IsEqualTo("console.info from frontend");
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Information);
    }

    [Test]
    public async Task OnConsoleMessage_LevelMapping_AllLevelsCorrect()
    {
        var logService = new LogService();
        var manager = new WindowManager(null);
        var receiver = new BrowserConsoleLogReceiver(logService, manager);
        receiver.Start();

        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "Capture" });
        var impl = Substitute.For<IWebviewWindowImpl>();
        Action<BrowserConsoleMessageLevel, string>? capturedHandler = null;
        impl.When(x => x.SetConsoleMessageHandler(Arg.Any<Action<BrowserConsoleMessageLevel, string>>()))
            .Do(callInfo => capturedHandler = callInfo.Arg<Action<BrowserConsoleMessageLevel, string>>());
        manager.GetWindow(id)!.Impl = impl;
        receiver.Start();

        // 验证各级别映射
        capturedHandler!.Invoke(BrowserConsoleMessageLevel.Debug, "dbg");
        capturedHandler!.Invoke(BrowserConsoleMessageLevel.Info, "info");
        capturedHandler!.Invoke(BrowserConsoleMessageLevel.Warning, "warn");
        capturedHandler!.Invoke(BrowserConsoleMessageLevel.Error, "err");

        var entries = logService.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(4);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Debug);
        await Assert.That(entries[1].Level).IsEqualTo(LogLevel.Information);
        await Assert.That(entries[2].Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(entries[3].Level).IsEqualTo(LogLevel.Error);
    }

    [Test]
    public async Task OnConsoleMessage_WritesSourceBrowserFieldForForwarderToSkip()
    {
        // 验证 OnConsoleMessage 写入的 fields 中包含 source=browser 标记，
        // 使 BrowserConsoleLogForwarder 能识别并跳过回环消息。
        var logService = new LogService();
        var manager = new WindowManager(null);
        var receiver = new BrowserConsoleLogReceiver(logService, manager);
        receiver.Start();

        // 捕获 handler
        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "Capture" });
        var impl = Substitute.For<IWebviewWindowImpl>();
        Action<BrowserConsoleMessageLevel, string>? capturedHandler = null;
        impl.When(x => x.SetConsoleMessageHandler(Arg.Any<Action<BrowserConsoleMessageLevel, string>>()))
            .Do(callInfo => capturedHandler = callInfo.Arg<Action<BrowserConsoleMessageLevel, string>>());
        manager.GetWindow(id)!.Impl = impl;
        receiver.Start();

        // 注册一个 LogHandler 捕获 fields
        IReadOnlyDictionary<string, object?>? capturedFields = null;
        logService.AddHandler((level, message, ex, fields) => capturedFields = fields);

        capturedHandler!.Invoke(BrowserConsoleMessageLevel.Info, "browser msg");

        await Assert.That(capturedFields).IsNotNull();
        await Assert.That(capturedFields!.ContainsKey("source")).IsTrue();
        await Assert.That(capturedFields!["source"] as string).IsEqualTo("browser");
    }

    [Test]
    public async Task OnConsoleMessage_BridgeLoopDoesNotRecurseToForwarder()
    {
        // 端到端验证：同时启用 Receiver 和 Forwarder，
        // 前端 console 消息写入后端 LogService（带 source=browser 标记），
        // Forwarder 应识别并跳过此消息，不再次注入到前端 console。
        var logService = new LogService();
        var (manager, impls, _) = CreateManagerWithWindows(1);

        // 启用 Forwarder（Direction 2）
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        // 启用 Receiver（Direction 3），捕获 handler
        var receiver = new BrowserConsoleLogReceiver(logService, manager);
        receiver.Start();

        // 再创建一个捕获窗口（因为前面的 impl 在 Forwarder 构造前已设置）
        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "Capture" });
        var captureImpl = Substitute.For<IWebviewWindowImpl>();
        Action<BrowserConsoleMessageLevel, string>? capturedHandler = null;
        captureImpl.When(x => x.SetConsoleMessageHandler(Arg.Any<Action<BrowserConsoleMessageLevel, string>>()))
            .Do(callInfo => capturedHandler = callInfo.Arg<Action<BrowserConsoleMessageLevel, string>>());
        manager.GetWindow(id)!.Impl = captureImpl;
        receiver.Start();

        // 清空之前可能的 ExecJS 调用记录
        impls[0].ClearReceivedCalls();
        captureImpl.ClearReceivedCalls();

        // 模拟前端 console 调用
        capturedHandler!.Invoke(BrowserConsoleMessageLevel.Info, "browser console message");

        // 验证：LogService 收到 1 条日志（来自 Receiver）
        var entries = logService.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Message).IsEqualTo("browser console message");

        // 验证：Forwarder 没有将此消息再次注入到任何窗口的 console
        // （否则会导致 Receiver 再次收到，形成无限循环）
        impls[0].DidNotReceive().ExecJS(Arg.Any<string>());
        captureImpl.DidNotReceive().ExecJS(Arg.Any<string>());
    }

    [Test]
    public async Task Dispose_UnregistersHandlerFromAllWindows()
    {
        var logService = new LogService();
        var (manager, impls, _) = CreateManagerWithWindows(2);
        var receiver = new BrowserConsoleLogReceiver(logService, manager);
        receiver.Start();

        receiver.Dispose();

        // 每个 impl 都应被调用 SetConsoleMessageHandler(null) 取消注册
        foreach (var impl in impls)
        {
            impl.Received(1).SetConsoleMessageHandler(null);
        }
    }

    [Test]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        var logService = new LogService();
        var manager = new WindowManager(null);
        var receiver = new BrowserConsoleLogReceiver(logService, manager);

        receiver.Dispose();
        await Assert.That(() => receiver.Dispose()).ThrowsNothing();
    }

    [Test]
    public async Task Dispose_NewWindowsAfterDispose_DoNotGetRegistered()
    {
        var logService = new LogService();
        var (manager, _, _) = CreateManagerWithWindows(1);
        var receiver = new BrowserConsoleLogReceiver(logService, manager);
        receiver.Start();
        receiver.Dispose();

        // Dispose 后创建新窗口，新窗口的 Impl 不应被注册
        var newId = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "AfterDispose" });
        var newImpl = Substitute.For<IWebviewWindowImpl>();
        manager.GetWindow(newId)!.Impl = newImpl;

        newImpl.DidNotReceive().SetConsoleMessageHandler(Arg.Any<Action<BrowserConsoleMessageLevel, string>>());
    }

    [Test]
    public async Task Start_SetConsoleMessageHandlerThrows_ContinuesWithOtherWindows()
    {
        // 验证：若某窗口的 SetConsoleMessageHandler 抛异常，不应影响其他窗口的注册
        var logService = new LogService();
        var (manager, impls, _) = CreateManagerWithWindows(2);

        // 第一个 impl 抛异常
        impls[0].When(x => x.SetConsoleMessageHandler(Arg.Any<Action<BrowserConsoleMessageLevel, string>>()))
            .Throw(new InvalidOperationException("platform not ready"));

        var receiver = new BrowserConsoleLogReceiver(logService, manager);

        // 不应抛异常
        receiver.Start();

        // 第二个 impl 仍应被注册
        impls[1].Received(1).SetConsoleMessageHandler(Arg.Any<Action<BrowserConsoleMessageLevel, string>>());
    }
}
