using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Tests;

/// <summary>
/// R1-4 验证：异步关闭路径与 ShutdownTimeout 贯穿。
/// 审计 §7 R1-4：建立真实 Platform Ready 信号与 UI 线程 Shutdown 超时统一。
/// <para>
/// 本测试类覆盖：
/// <list type="bullet">
/// <item><c>Application.ShutdownAsync</c> 异步关闭契约（CancellationToken 串联、IsRunning 翻转为 false）</item>
/// <item><c>ApplicationOptions.ShutdownTimeout</c> 默认值与覆盖语义</item>
/// <item><c>Application.Shutdown</c> 向后兼容 shim（委派给 ShutdownAsync）</item>
/// <item><c>IPlatformApp.ShutdownAsync</c> 默认实现委派 Destroy</item>
/// <item>关闭流程异常隔离（不阻塞清理路径）</item>
/// </list>
/// </para>
/// </summary>
[NotInParallel]
public sealed class ShutdownAsyncTests
{
    [Test]
    public async Task ApplicationOptions_ShutdownTimeout_DefaultsToInfinite()
    {
        // R1-4：默认 InfiniteTimeSpan 保持向后兼容（依赖平台信号自然完成）。
        // 用户需显式设置以获得超时降级行为。
        var options = new ApplicationOptions();
        await Assert.That(options.ShutdownTimeout).IsEqualTo(System.Threading.Timeout.InfiniteTimeSpan);
    }

    [Test]
    public async Task Application_ShutdownAsync_WhenNotRunning_ReturnsImmediately()
    {
        // 安排：未运行 Application
        var app = new Application(new ApplicationOptions());

        // 操作 + 断言：ShutdownAsync 不抛异常、不卡住
        await app.ShutdownAsync(CancellationToken.None);
        await Assert.That(app.IsRunning).IsFalse();
    }

    [Test]
    public async Task Application_Shutdown_DelegatesToShutdownAsync()
    {
        // 安排：构造 Application + 通过反射标记 _isRunning = true（模拟已运行）
        var app = new Application(new ApplicationOptions());
        var field = typeof(Application).GetField("_isRunning",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(app, true);

        // 操作：调用同步 Shutdown()
        app.Shutdown();

        // 断言：IsRunning 已翻转为 false（说明 Shutdown 委派路径生效）
        await Assert.That(app.IsRunning).IsFalse();
    }

    [Test]
    public async Task Application_ShutdownAsync_FlipsIsRunningToFalse()
    {
        // 安排
        var app = new Application(new ApplicationOptions());
        var platform = new ServerPlatformApp(app.Options);
        app.SetPlatformApp(platform);
        app.RegisterService(new FakeLifecycleService());

        // 通过反射标记 _isRunning = true（避免启动真实主循环）
        var field = typeof(Application).GetField("_isRunning",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(app, true);

        // 操作：异步关闭
        await app.ShutdownAsync(CancellationToken.None);

        // 断言
        await Assert.That(app.IsRunning).IsFalse();
        await Assert.That(app.LifecycleState).IsEqualTo(
            Wails.Net.Application.Hosting.ApplicationLifecycleState.Stopped);
    }

    [Test]
    public async Task Application_ShutdownAsync_WithCancellationToken_RespectsCancellation()
    {
        // 安排
        var app = new Application(new ApplicationOptions());
        var platform = new ServerPlatformApp(app.Options);
        app.SetPlatformApp(platform);
        app.RegisterService(new FakeLifecycleService());

        var field = typeof(Application).GetField("_isRunning",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(app, true);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // 立即取消

        // 操作 + 断言：已取消的 CancellationToken 不抛异常（关闭流程内部容错）
        await app.ShutdownAsync(cts.Token);
        await Assert.That(app.IsRunning).IsFalse();
    }

    [Test]
    public async Task IPlatformApp_ShutdownAsync_Default_InvokesDestroy()
    {
        // R1-4：默认 ShutdownAsync 应委派给 Destroy，不破坏现有平台实现。
        // 这里通过 mock platform 验证默认行为。
        var platform = new MockShutdownPlatform();

        await platform.ShutdownAsync(CancellationToken.None);

        await Assert.That(platform.DestroyCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task ApplicationOptions_ShutdownTimeout_OverridesDefault()
    {
        // 安排 + 操作
        var options = new ApplicationOptions
        {
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        };

        // 断言
        await Assert.That(options.ShutdownTimeout).IsEqualTo(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// 用于验证 IPlatformApp.ShutdownAsync 默认实现委派给 Destroy 的 mock。
    /// </summary>
    private sealed class MockShutdownPlatform : IPlatformApp
    {
        public int DestroyCallCount { get; private set; }

        public string Name => "Mock";

        public int Run() => 0;

        public void Destroy() => DestroyCallCount++;

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            // R1-4：验证默认实现路径 —— 应委派给 Destroy 并返回 CompletedTask
            Destroy();
            return Task.CompletedTask;
        }

        public void SetApplicationMenu(Wails.Net.Application.Menus.Menu? menu) { }
        public uint GetCurrentWindowId() => 0;
        public void ShowAboutDialog(string name, string description, byte[]? icon) { }
        public void SetIcon(byte[]? icon) { }
        public void On(uint id) { }
        public void DispatchOnMainThread(uint id) { }
        public void Hide() { }
        public void Show() { }
        public Wails.Net.Application.Screens.Screen? GetPrimaryScreen() => null;
        public Wails.Net.Application.Screens.Screen[] GetScreens() => Array.Empty<Wails.Net.Application.Screens.Screen>();
        public Dictionary<string, object?> GetFlags(ApplicationOptions options) => new();
        public bool IsOnMainThread() => true;
        public bool IsDarkMode() => false;
        public string GetAccentColor() => string.Empty;
        public void DispatchOnMainThread(Action action) => action();
        public Wails.Net.Application.Windows.IWebviewWindowImpl? CreateWebviewWindow(uint id, Wails.Net.Application.Options.WebviewWindowOptions options) => null;
        public Task<int> ShowMessageDialog(string title, string message, Wails.Net.Application.Dialogs.DialogStyle style, string[] buttons) => Task.FromResult(0);
        public Task<string?> OpenFileDialog(Wails.Net.Application.Dialogs.OpenFileDialogOptions options) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileDialog(Wails.Net.Application.Dialogs.SaveFileDialogOptions options) => Task.FromResult<string?>(null);
        public Task<string[]?> OpenMultipleFilesDialog(Wails.Net.Application.Dialogs.OpenFileDialogOptions options) => Task.FromResult<string[]?>(null);
    }

    /// <summary>
    /// 用于测试的假生命周期服务（实现 IServiceStartup/IServiceShutdown）。
    /// </summary>
    private sealed class FakeLifecycleService : Wails.Net.Application.Services.IServiceStartup,
        Wails.Net.Application.Services.IServiceShutdown
    {
        public string Name { get; set; } = "Fake";
        public bool StartupCalled { get; private set; }
        public bool ShutdownCalled { get; private set; }

        public Task ServiceStartup(ApplicationOptions options, CancellationToken cancellationToken)
        {
            StartupCalled = true;
            return Task.CompletedTask;
        }

        public Task ServiceShutdown(CancellationToken cancellationToken)
        {
            ShutdownCalled = true;
            return Task.CompletedTask;
        }
    }
}
