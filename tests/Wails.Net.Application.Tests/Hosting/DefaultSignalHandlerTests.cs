using TUnit.Core;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;

namespace Wails.Net.Application.Tests.Hosting;

/// <summary>
/// 默认信号处理器（P2-21，对应 Wails v3 Go 版本 <c>internal/signal/signal.go</c> 与
/// <c>app.setupSignalHandler</c>）的单元测试。
/// 覆盖：首个信号触发退出、重复信号逐级提示、达到上限强制退出、选项开关，
/// 以及 <see cref="Application.Run"/> 侧的安装/跳过行为。
/// </summary>
[NotInParallel]
public sealed class DefaultSignalHandlerTests
{
    /// <summary>首个信号应调用 cleanup 一次，且不强制退出。</summary>
    [Test]
    public async Task FirstSignal_InvokesCleanupOnce_AndDoesNotForceExit()
    {
        var cleanups = 0;
        var forceExits = 0;
        using var handler = new DefaultSignalHandler(
            cleanup: () => Interlocked.Increment(ref cleanups),
            forceExit: () => Interlocked.Increment(ref forceExits));

        handler.RaiseSignal("SIGINT");

        await Assert.That(cleanups).IsEqualTo(1);
        await Assert.That(forceExits).IsEqualTo(0);
    }

    /// <summary>未达上限的重复信号仅提示，不重复调用 cleanup、不强制退出。</summary>
    [Test]
    public async Task RepeatedSignalsBelowLimit_DoNotForceExit()
    {
        var cleanups = 0;
        var forceExits = 0;
        using var handler = new DefaultSignalHandler(
            cleanup: () => Interlocked.Increment(ref cleanups),
            maxSignal: 3,
            forceExit: () => Interlocked.Increment(ref forceExits));

        handler.RaiseSignal("SIGINT");
        handler.RaiseSignal("SIGINT");

        await Assert.That(cleanups).IsEqualTo(1);
        await Assert.That(forceExits).IsEqualTo(0);
    }

    /// <summary>达到次数上限时强制退出一次。</summary>
    [Test]
    public async Task SignalAtLimit_ForceExitsOnce()
    {
        var cleanups = 0;
        var forceExits = 0;
        using var handler = new DefaultSignalHandler(
            cleanup: () => Interlocked.Increment(ref cleanups),
            maxSignal: 3,
            forceExit: () => Interlocked.Increment(ref forceExits));

        handler.RaiseSignal("SIGTERM");
        handler.RaiseSignal("SIGTERM");
        handler.RaiseSignal("SIGTERM");

        await Assert.That(cleanups).IsEqualTo(1);
        await Assert.That(forceExits).IsEqualTo(1);
    }

    /// <summary>Start 重复调用为空操作；Dispose 可安全重复调用。</summary>
    [Test]
    public async Task StartAndDispose_AreIdempotent()
    {
        var handler = new DefaultSignalHandler(cleanup: static () => { });

        await Assert.That(() => handler.Start()).ThrowsNothing();
        await Assert.That(() => handler.Start()).ThrowsNothing();
        await Assert.That(() => handler.Dispose()).ThrowsNothing();
        await Assert.That(() => handler.Dispose()).ThrowsNothing();
    }

    /// <summary>次数上限参数校验。</summary>
    [Test]
    public async Task Constructor_RejectsNonPositiveMaxSignal()
    {
        await Assert.That(() => new DefaultSignalHandler(cleanup: static () => { }, maxSignal: 0))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    /// <summary>选项默认值应为 false（安装默认信号处理器）。</summary>
    [Test]
    public async Task ApplicationOptions_DisableDefaultSignalHandler_DefaultsToFalse()
    {
        await Assert.That(new ApplicationOptions().DisableDefaultSignalHandler).IsFalse();
    }

    /// <summary>默认选项下，Application 应安装信号处理器。</summary>
    [Test]
    public async Task Application_DefaultOptions_InstallsSignalHandler()
    {
        var app = new Application(new ApplicationOptions());

        var handler = app.SetupSignalHandlerForTest();

        await Assert.That(handler).IsNotNull();
        handler!.Dispose();
    }

    /// <summary>DisableDefaultSignalHandler=true 时，Application 跳过安装。</summary>
    [Test]
    public async Task Application_Disabled_DoesNotInstallSignalHandler()
    {
        var app = new Application(new ApplicationOptions { DisableDefaultSignalHandler = true });

        var handler = app.SetupSignalHandlerForTest();

        await Assert.That(handler).IsNull();
    }

    /// <summary>首个信号经 ShouldQuit 判定：返回 false 时不触发 Quit。</summary>
    [Test]
    public async Task FirstSignal_ShouldQuitFalse_DoesNotQuit()
    {
        var shouldQuitCalled = 0;
        var options = new ApplicationOptions
        {
            ShouldQuit = () =>
            {
                shouldQuitCalled++;
                return false;
            },
        };
        var app = new Application(options);

        var handler = app.SetupSignalHandlerForTest();
        await Assert.That(handler).IsNotNull();

        handler!.RaiseSignal("SIGINT");

        // ShouldQuit 被调用（判定发生），但应用未进入 Quit/Shutdown（IsRunning 仍为 false）。
        await Assert.That(shouldQuitCalled).IsEqualTo(1);
        await Assert.That(app.IsRunning).IsFalse();

        handler.Dispose();
    }
}
