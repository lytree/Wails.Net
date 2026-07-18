using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Logging;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Options;
using Wails.Net.Application.Services;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests.Logging;

/// <summary>
/// <see cref="BrowserConsoleLogForwarder"/> 的单元测试（P1-3 Direction 2：LogService → 前端 console）。
/// </summary>
[NotInParallel]
public sealed class BrowserConsoleLogForwarderTests
{
    /// <summary>
    /// 创建一个 WindowManager，并预置 N 个窗口，每个窗口的 Impl 替换为 NSubstitute 桩。
    /// 返回桩列表用于断言 ExecJS 调用。
    /// </summary>
    private static (WindowManager manager, List<IWebviewWindowImpl> impls) CreateManagerWithWindows(int count)
    {
        var manager = new WindowManager(null);
        var impls = new List<IWebviewWindowImpl>();
        for (var i = 0; i < count; i++)
        {
            var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = $"W{i}" });
            var window = manager.GetWindow(id)!;
            var impl = Substitute.For<IWebviewWindowImpl>();
            window.Impl = impl;
            impls.Add(impl);
        }
        return (manager, impls);
    }

    [Test]
    public async Task Constructor_NullLogService_ThrowsArgumentNullException()
    {
        var manager = new WindowManager(null);

        await Assert.That(() => new BrowserConsoleLogForwarder(null!, manager))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_NullWindowManager_ThrowsArgumentNullException()
    {
        var logService = new LogService();

        await Assert.That(() => new BrowserConsoleLogForwarder(logService, null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task OnLog_NoWindowsRegistered_DoesNotThrow()
    {
        var logService = new LogService();
        var manager = new WindowManager(null);
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        // 没有窗口时不应抛异常
        logService.Info("hello");

        // 验证 forwarder 已构造（构造时已注册 handler）
        await Assert.That(forwarder).IsNotNull();
    }

    [Test]
    public async Task OnLog_SingleWindow_CallsExecJSOnce()
    {
        var logService = new LogService();
        var (manager, impls) = CreateManagerWithWindows(1);
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        logService.Info("test message");

        impls[0].Received(1).ExecJS(Arg.Is<string>(js => js.Contains("console.info") && js.Contains("test message")));
    }

    [Test]
    public async Task OnLog_MultipleWindows_CallsExecJSOnAll()
    {
        var logService = new LogService();
        var (manager, impls) = CreateManagerWithWindows(3);
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        logService.Info("broadcast");

        foreach (var impl in impls)
        {
            impl.Received(1).ExecJS(Arg.Is<string>(js => js.Contains("console.info") && js.Contains("broadcast")));
        }
    }

    [Test]
    public async Task OnLog_InformationLevel_UsesConsoleInfo()
    {
        var logService = new LogService();
        var (manager, impls) = CreateManagerWithWindows(1);
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        logService.Info("info msg");

        impls[0].Received(1).ExecJS(Arg.Is<string>(js => js.Contains("console.info") && js.Contains("info msg")));
    }

    [Test]
    public async Task OnLog_WarningLevel_UsesConsoleWarn()
    {
        var logService = new LogService();
        var (manager, impls) = CreateManagerWithWindows(1);
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        logService.Warning("warn msg");

        impls[0].Received(1).ExecJS(Arg.Is<string>(js => js.Contains("console.warn") && js.Contains("warn msg")));
    }

    [Test]
    public async Task OnLog_ErrorLevel_UsesConsoleError()
    {
        var logService = new LogService();
        var (manager, impls) = CreateManagerWithWindows(1);
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        logService.Error("err msg");

        impls[0].Received(1).ExecJS(Arg.Is<string>(js => js.Contains("console.error") && js.Contains("err msg")));
    }

    [Test]
    public async Task OnLog_FatalLevel_UsesConsoleError()
    {
        var logService = new LogService();
        var (manager, impls) = CreateManagerWithWindows(1);
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        logService.Fatal("fatal msg");

        impls[0].Received(1).ExecJS(Arg.Is<string>(js => js.Contains("console.error") && js.Contains("fatal msg")));
    }

    [Test]
    public async Task OnLog_DebugLevel_UsesConsoleLog()
    {
        var logService = new LogService();
        var (manager, impls) = CreateManagerWithWindows(1);
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        logService.Debug("debug msg");

        impls[0].Received(1).ExecJS(Arg.Is<string>(js => js.Contains("console.log") && js.Contains("debug msg")));
    }

    [Test]
    public async Task OnLog_WindowWithNullImpl_SkipsThatWindow()
    {
        var logService = new LogService();
        var manager = new WindowManager(null);
        // 创建窗口但不设置 Impl（保持 null）
        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "NullImpl" });
        // 添加另一个窗口，设置 mock Impl
        var id2 = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "WithImpl" });
        var impl2 = Substitute.For<IWebviewWindowImpl>();
        manager.GetWindow(id2)!.Impl = impl2;

        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        // 不应抛异常，且 impl2 应该收到 ExecJS
        logService.Info("test");

        impl2.Received(1).ExecJS(Arg.Any<string>());
    }

    [Test]
    public async Task OnLog_ImplThrowsException_ContinuesToOtherWindows()
    {
        var logService = new LogService();
        var (manager, impls) = CreateManagerWithWindows(2);
        // 第一个 impl 抛异常
        impls[0].When(x => x.ExecJS(Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("window disposed"));
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        // 不应抛异常
        logService.Info("test");

        // 第二个 impl 仍应被调用
        impls[1].Received(1).ExecJS(Arg.Any<string>());
    }

    [Test]
    public async Task OnLog_MessageWithSpecialCharacters_ProperlyJsonEscaped()
    {
        var logService = new LogService();
        var (manager, impls) = CreateManagerWithWindows(1);
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        // 包含双引号、反斜杠、换行符的消息
        logService.Info("a\"b\\c\nd");

        // ExecJS 调用的 JS 应正确转义特殊字符。
        // System.Text.Json 默认使用 HTML 安全编码，将 " 转义为 \u0022，而非 \"。
        impls[0].Received(1).ExecJS(Arg.Is<string>(js =>
            js.Contains("console.info") &&
            js.Contains("\\u0022") &&  // 双引号转义为 \u0022
            js.Contains("\\\\") &&     // 反斜杠转义为 \\
            js.Contains("\\n")));       // 换行符转义为 \n
    }

    [Test]
    public async Task OnLog_RecursiveForwarding_PreventsInfiniteLoop()
    {
        // 验证 AsyncLocal 防回环：模拟前端 console 被拦截后再次转发到后端的场景。
        // 流程：
        //   1. logService.Info("initial") → OnLog → impl.ExecJS
        //   2. impl.ExecJS mock 回调 → logService.Info("recursive")
        //   3. logService.Info → OnLog（_isForwarding=true，跳过） → return
        //   4. mock 回调返回 → 外层 OnLog 完成，_isForwarding 重置
        //
        // 期望：ExecJS 仅被调用 1 次（初始日志），递归尝试被 AsyncLocal 阻断。
        var logService = new LogService();
        var (manager, impls) = CreateManagerWithWindows(1);

        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        var execJsCallCount = 0;
        impls[0].When(x => x.ExecJS(Arg.Any<string>()))
            .Do(_ =>
            {
                execJsCallCount++;
                if (execJsCallCount < 10) // 防御性上限：若防回环失效则至少在 10 次后停止
                {
                    // 模拟前端 console 拦截后再次转发到后端
                    logService.Info("recursive from frontend");
                }
            });

        // 触发首次日志
        logService.Info("initial log");

        // 验证：ExecJS 仅被调用 1 次（AsyncLocal 阻断了递归）
        await Assert.That(execJsCallCount).IsEqualTo(1);

        // 验证：LogService 记录了 2 条日志（初始日志 + 递归尝试，因为递归尝试仍在 LogService 中写入，只是没触发 OnLog）
        var entries = logService.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries[0].Message.Contains("initial log")).IsTrue();
        await Assert.That(entries[1].Message.Contains("recursive from frontend")).IsTrue();
    }

    [Test]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        var logService = new LogService();
        var manager = new WindowManager(null);
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);

        forwarder.Dispose();
        await Assert.That(() => forwarder.Dispose()).ThrowsNothing();
    }

    [Test]
    public async Task Constructor_RegistersHandlerWithLogService()
    {
        // 验证构造时立即注册 LogHandler
        var logService = new LogService();
        var (manager, impls) = CreateManagerWithWindows(1);

        // 构造 forwarder 之前日志不会被转发
        logService.Info("before forwarder");
        impls[0].DidNotReceive().ExecJS(Arg.Any<string>());

        // 构造 forwarder 之后日志会被转发
        var forwarder = new BrowserConsoleLogForwarder(logService, manager);
        logService.Info("after forwarder");

        impls[0].Received(1).ExecJS(Arg.Is<string>(js => js.Contains("after forwarder")));
    }
}
