using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;

namespace Wails.Net.Application.Tests;

/// <summary>
/// Application 生命周期事件钩子的单元测试（P1-7：PostShutdown / ShouldQuit 对齐 Wails v3）。
/// 对应 Wails v3 Go 版本 application.go 中的 PostShutdown 与 shouldQuit 机制。
/// 注意：Application 构造函数会设置静态全局实例，因此此类中的测试不并行执行。
/// </summary>
[NotInParallel]
public sealed class ApplicationEventHooksTests
{
    /// <summary>
    /// 通过反射设置 _isRunning 字段，模拟应用处于运行状态以触发 Shutdown 完整流程。
    /// </summary>
    private static void SetRunning(Application app, bool value)
    {
        var field = typeof(Application).GetField("_isRunning",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(app, value);
    }

    // ========== ShouldQuit 测试 ==========

    [Test]
    public async Task ShouldQuit_WhenOptionIsNull_ReturnsTrue()
    {
        // 安排：未配置 ShouldQuit 回调
        var app = new Application(new ApplicationOptions());

        // 操作 + 断言：默认允许退出
        await Assert.That(app.ShouldQuit()).IsTrue();
    }

    [Test]
    public async Task ShouldQuit_WhenCallbackReturnsTrue_ReturnsTrue()
    {
        // 安排
        var app = new Application(new ApplicationOptions
        {
            ShouldQuit = () => true
        });

        // 操作 + 断言
        await Assert.That(app.ShouldQuit()).IsTrue();
    }

    [Test]
    public async Task ShouldQuit_WhenCallbackReturnsFalse_ReturnsFalse()
    {
        // 安排：模拟未保存数据场景
        var app = new Application(new ApplicationOptions
        {
            ShouldQuit = () => false
        });

        // 操作 + 断言
        await Assert.That(app.ShouldQuit()).IsFalse();
    }

    [Test]
    public async Task ShouldQuit_WhenCallbackThrows_PropagatesException()
    {
        // 安排：回调抛出异常应直接传播，不应被吞掉
        // 对应 Wails v3 Go 版本 shouldQuit() 方法无 recover() 的行为
        var app = new Application(new ApplicationOptions
        {
            ShouldQuit = () => throw new InvalidOperationException("User canceled")
        });

        // 操作 + 断言
        await Assert.That(() => app.ShouldQuit()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ShouldQuit_CanBeUsedForConditionalQuit()
    {
        // 安排：模拟有未保存数据时阻止退出的场景
        var hasUnsavedData = true;
        var app = new Application(new ApplicationOptions
        {
            ShouldQuit = () => !hasUnsavedData
        });

        // 操作 + 断言：有未保存数据时阻止退出
        await Assert.That(app.ShouldQuit()).IsFalse();

        // 用户保存后允许退出
        hasUnsavedData = false;
        await Assert.That(app.ShouldQuit()).IsTrue();
    }

    // ========== Quit 方法行为测试 ==========

    [Test]
    public async Task Quit_DoesNotCheckShouldQuitCallback()
    {
        // 安排：ShouldQuit 返回 false，但 Quit() 应绕过此检查直接退出
        // 对应 Wails v3 Go 版本：Quit() 是用户主动退出，不走 shouldQuit() 检查
        var quitChecked = false;
        var app = new Application(new ApplicationOptions
        {
            ShouldQuit = () =>
            {
                quitChecked = true;
                return false;
            }
        });
        SetRunning(app, true);

        // 操作
        app.Quit();

        // 断言：Quit 未调用 ShouldQuit 回调，但应用已停止运行
        await Assert.That(quitChecked).IsFalse();
        await Assert.That(app.IsRunning).IsFalse();
    }

    // ========== PostShutdown 测试 ==========

    [Test]
    public async Task Shutdown_WhenNotRunning_DoesNotInvokePostShutdown()
    {
        // 安排：未运行时 Shutdown 直接返回，不应触发 PostShutdown
        var postShutdownCalled = false;
        var app = new Application(new ApplicationOptions
        {
            PostShutdown = () => postShutdownCalled = true
        });
        // _isRunning 默认为 false

        // 操作
        app.Shutdown();

        // 断言
        await Assert.That(postShutdownCalled).IsFalse();
    }

    [Test]
    public async Task Shutdown_WhenRunning_InvokesPostShutdownAfterCleanup()
    {
        // 安排
        var callSequence = new List<string>();
        var app = new Application(new ApplicationOptions { Name = "PostShutdownApp" });
        app.SetPlatformApp(new ServerPlatformApp(app.Options));

        // 注册一个会记录关闭顺序的服务
        var service = new SequenceRecordingService("Svc", callSequence);
        app.RegisterService(service);

        // OnShutdown 应在 PostShutdown 之前
        app.OnShutdown(() => callSequence.Add("OnShutdown"));
        app.Options.PostShutdown = () => callSequence.Add("PostShutdown");

        SetRunning(app, true);

        // 操作
        app.Shutdown();

        // 断言：PostShutdown 被调用，且在 OnShutdown 和 ServiceShutdown 之后
        await Assert.That(callSequence).Contains("PostShutdown");
        await Assert.That(callSequence).Contains("OnShutdown");
        await Assert.That(callSequence).Contains("Svc:ServiceShutdown");
        await Assert.That(callSequence.IndexOf("PostShutdown"))
            .IsGreaterThan(callSequence.IndexOf("OnShutdown"));
        await Assert.That(callSequence.IndexOf("PostShutdown"))
            .IsGreaterThan(callSequence.IndexOf("Svc:ServiceShutdown"));
    }

    [Test]
    public async Task Shutdown_WhenPostShutdownIsNull_DoesNotThrow()
    {
        // 安排：未配置 PostShutdown
        var app = new Application(new ApplicationOptions { Name = "NullPostShutdown" });
        app.SetPlatformApp(new ServerPlatformApp(app.Options));
        SetRunning(app, true);

        // 操作 + 断言：不应抛异常
        app.Shutdown();
        await Assert.That(app.IsRunning).IsFalse();
    }

    [Test]
    public async Task Shutdown_WhenPostShutdownThrows_DoesNotPropagateException()
    {
        // 安排：PostShutdown 抛异常应被吞掉，不影响已完成的关闭流程
        // 对应 Wails v3 Go 版本 cleanup() 中 PostShutdown 调用前没有 defer/recover，
        // 但 Wails.Net 选择更稳健的策略：吞掉异常以避免影响已完成的清理。
        var app = new Application(new ApplicationOptions
        {
            Name = "ThrowingPostShutdown",
            PostShutdown = () => throw new InvalidOperationException("PostShutdown failure")
        });
        app.SetPlatformApp(new ServerPlatformApp(app.Options));
        SetRunning(app, true);

        // 操作 + 断言：不应抛异常
        app.Shutdown();
        await Assert.That(app.IsRunning).IsFalse();
    }

    [Test]
    public async Task Shutdown_InvokesPostShutdownExactlyOnce()
    {
        // 安排
        var callCount = 0;
        var app = new Application(new ApplicationOptions
        {
            Name = "OncePostShutdown",
            PostShutdown = () => Interlocked.Increment(ref callCount)
        });
        app.SetPlatformApp(new ServerPlatformApp(app.Options));
        SetRunning(app, true);

        // 操作
        app.Shutdown();
        // 再次调用 Shutdown（_isRunning 已为 false，应直接返回）
        app.Shutdown();

        // 断言：PostShutdown 只被调用一次（第二次 Shutdown 因 _isRunning=false 直接返回）
        await Assert.That(callCount).IsEqualTo(1);
    }

    [Test]
    public async Task Shutdown_PostShutdownInvokedAfterPlatformDestroy()
    {
        // 安排：验证 PostShutdown 在 _platformApp.Destroy() 之后调用
        // 通过自定义 ServerPlatformApp 子类记录 Destroy 调用顺序
        var sequence = new List<string>();
        var platformApp = new RecordingServerPlatformApp(sequence);
        var app = new Application(new ApplicationOptions { Name = "OrderApp" });
        app.SetPlatformApp(platformApp);
        app.Options.PostShutdown = () => sequence.Add("PostShutdown");
        SetRunning(app, true);

        // 操作
        app.Shutdown();

        // 断言：PostShutdown 在 PlatformDestroy 之后
        await Assert.That(sequence).Contains("PlatformDestroy");
        await Assert.That(sequence).Contains("PostShutdown");
        await Assert.That(sequence.IndexOf("PostShutdown"))
            .IsGreaterThan(sequence.IndexOf("PlatformDestroy"));
    }

    [Test]
    public async Task Shutdown_PostShutdownInvokedAfterIsRunningSetToFalse()
    {
        // 安排：验证 PostShutdown 在 _isRunning = false 之后调用
        // 这确保 PostShutdown 内部检查 IsRunning 时会得到 false
        var isRunningDuringPostShutdown = true;
        Application? captured = null;
        var app = new Application(new ApplicationOptions { Name = "RunningCheckApp" });
        captured = app;
        app.Options.PostShutdown = () => isRunningDuringPostShutdown = captured.IsRunning;
        app.SetPlatformApp(new ServerPlatformApp(app.Options));
        SetRunning(app, true);

        // 操作
        app.Shutdown();

        // 断言：PostShutdown 触发时 IsRunning 已为 false
        await Assert.That(isRunningDuringPostShutdown).IsFalse();
    }

    // ========== OnShutdown 与 PostShutdown 顺序对比测试 ==========

    [Test]
    public async Task Shutdown_OnShutdownInvokedBeforePostShutdown()
    {
        // 安排：OnShutdown 在关闭流程开始时调用，PostShutdown 在结束时调用
        var sequence = new List<string>();
        var app = new Application(new ApplicationOptions { Name = "OrderCompareApp" });
        app.SetPlatformApp(new ServerPlatformApp(app.Options));

        app.Options.OnShutdown = () => sequence.Add("OnShutdown");
        app.Options.PostShutdown = () => sequence.Add("PostShutdown");
        app.OnShutdown(() => sequence.Add("OnShutdown-Registered"));

        SetRunning(app, true);

        // 操作
        app.Shutdown();

        // 断言：OnShutdown 类回调在 PostShutdown 之前
        var onShutdownIdx = sequence.IndexOf("OnShutdown");
        var onShutdownRegisteredIdx = sequence.IndexOf("OnShutdown-Registered");
        var postShutdownIdx = sequence.IndexOf("PostShutdown");

        await Assert.That(onShutdownIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(onShutdownRegisteredIdx).IsGreaterThanOrEqualTo(0);
        await Assert.That(postShutdownIdx).IsGreaterThanOrEqualTo(0);

        await Assert.That(postShutdownIdx).IsGreaterThan(onShutdownIdx);
        await Assert.That(postShutdownIdx).IsGreaterThan(onShutdownRegisteredIdx);
    }

    /// <summary>
    /// 记录调用顺序的服务，用于验证 PostShutdown 在 ServiceShutdown 之后。
    /// </summary>
    private sealed class SequenceRecordingService : Wails.Net.Application.Services.IServiceShutdown
    {
        private readonly string _name;
        private readonly List<string> _sequence;

        public SequenceRecordingService(string name, List<string> sequence)
        {
            _name = name;
            _sequence = sequence;
        }

        public Task ServiceShutdown(CancellationToken cancellationToken)
        {
            _sequence.Add($"{_name}:ServiceShutdown");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 记录 Destroy 调用的 ServerPlatformApp 子类，用于验证 PostShutdown 在 PlatformDestroy 之后。
    /// </summary>
    private sealed class RecordingServerPlatformApp : ServerPlatformApp
    {
        private readonly List<string> _sequence;

        public RecordingServerPlatformApp(List<string> sequence)
            : base(new ApplicationOptions { Name = "RecordingPlatform" })
        {
            _sequence = sequence;
        }

        public override void Destroy()
        {
            _sequence.Add("PlatformDestroy");
            base.Destroy();
        }
    }
}
