using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests;

/// <summary>
/// Application 的单元测试（TUnit）。
/// 注意：Application 构造函数会设置静态全局实例，因此此类中的测试不并行执行。
/// </summary>
[NotInParallel]
public sealed class ApplicationTests
{
    /// <summary>
    /// 通过反射设置窗口的平台实现，因为 Impl 的 setter 是 internal 的。
    /// DestroyWindow 会调用 window.Close()，而 Close() 要求 Impl 不为 null。
    /// </summary>
    private static void SetWindowImpl(WebviewWindow window)
    {
        var impl = new ServerWebviewWindow();
        var property = typeof(WebviewWindow).GetProperty(nameof(WebviewWindow.Impl))!;
        property.SetValue(window, impl);
    }

    [Test]
    public async Task Constructor_SetsOptions()
    {
        // 安排
        var options = new ApplicationOptions
        {
            Name = "TestApp",
            Version = "2.0.0"
        };

        // 操作
        var app = new Application(options);

        // 断言
        await Assert.That(app.Options).IsSameReferenceAs(options);
        await Assert.That(app.Options.Name).IsEqualTo("TestApp");
        await Assert.That(app.Options.Version).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task Constructor_SetsGlobalInstance()
    {
        // 安排与操作
        var app = new Application(new ApplicationOptions());

        // 断言
        await Assert.That(Application.Get()).IsSameReferenceAs(app);
    }

    [Test]
    public async Task RegisterService_DelegatesToRegistry()
    {
        // 安排
        var app = new Application(new ApplicationOptions());
        var service = new object();

        // 操作
        app.RegisterService(service);

        // 断言
        await Assert.That(app.Services.Services.Count).IsEqualTo(1);
        await Assert.That(app.Services.Services[0]).IsSameReferenceAs(service);
    }

    [Test]
    public async Task CreateWebviewWindow_AssignsIncrementalIDs()
    {
        // 安排
        var app = new Application(new ApplicationOptions());

        // 操作
        var window1 = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "Window1" });
        var window2 = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "Window2" });

        // 断言
        await Assert.That(window1.ID).IsEqualTo(1u);
        await Assert.That(window2.ID).IsEqualTo(2u);
    }

    [Test]
    public async Task CreateWebviewWindow_StoresWindow()
    {
        // 安排
        var app = new Application(new ApplicationOptions());

        // 操作
        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "TestWindow" });

        // 断言
        await Assert.That(app.GetWindow(window.ID)).IsSameReferenceAs(window);
    }

    [Test]
    public async Task GetWindowByName_ReturnsCorrectWindow()
    {
        // 安排
        var app = new Application(new ApplicationOptions());

        // 操作
        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "MyWindow" });

        // 断言
        await Assert.That(app.GetWindowByName("MyWindow")).IsSameReferenceAs(window);
    }

    [Test]
    public async Task GetWindowByName_ReturnsNullForUnknownName()
    {
        // 安排
        var app = new Application(new ApplicationOptions());

        // 操作与断言
        await Assert.That(app.GetWindowByName("UnknownName")).IsNull();
    }

    [Test]
    public async Task DestroyWindow_RemovesWindow()
    {
        // 安排
        var app = new Application(new ApplicationOptions());
        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "TestWindow" });
        // 设置平台实现，使 Close() 不会抛出异常
        SetWindowImpl(window);

        // 操作
        app.DestroyWindow(window.ID);

        // 断言
        await Assert.That(app.GetWindow(window.ID)).IsNull();
    }

    [Test]
    public async Task Run_WithServerPlatformApp_StartsServicesAndTransportsAndShutsDown()
    {
        // 安排
        var app = new Application(new ApplicationOptions { Name = "RunApp" });
        var serverPlatform = new ServerPlatformApp(app.Options);
        app.SetPlatformApp(serverPlatform);
        var startupService = new FakeLifecycleService();
        var transport = new FakeTransport();
        app.RegisterService(startupService);
        app.Transport = transport;

        // ServerPlatformApp.Run() 会阻塞直到 SignalShutdown 被调用，
        // 因此在后台线程延迟 100ms 触发关闭信号，使 Run() 能够返回。
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            serverPlatform.SignalShutdown();
        });

        // 操作
        app.Run();

        // 断言：服务启动、传输层启动、服务关闭、传输层停止均被调用
        await Assert.That(startupService.StartupCalled).IsTrue();
        await Assert.That(startupService.ShutdownCalled).IsTrue();
        await Assert.That(transport.Started).IsTrue();
        await Assert.That(transport.Stopped).IsTrue();
        await Assert.That(app.IsRunning).IsFalse();
    }

    [Test]
    public async Task Run_WhenAlreadyRunning_ReturnsImmediately()
    {
        // 安排：通过反射将 _isRunning 置为 true 模拟已运行
        var app = new Application(new ApplicationOptions());
        var field = typeof(Application).GetField("_isRunning",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(app, true);

        var startupService = new FakeLifecycleService();
        app.RegisterService(startupService);

        // 操作
        app.Run();

        // 断言：不会再次启动服务
        await Assert.That(startupService.StartupCalled).IsFalse();
    }

    [Test]
    public async Task Shutdown_WhenNotRunning_ReturnsImmediately()
    {
        // 安排：未调用 Run，_isRunning 为 false
        var app = new Application(new ApplicationOptions());
        var shutdownService = new FakeLifecycleService();
        app.RegisterService(shutdownService);

        // 操作
        app.Shutdown();

        // 断言：未运行时不会触发服务关闭
        await Assert.That(shutdownService.ShutdownCalled).IsFalse();
    }

    [Test]
    public async Task Shutdown_ClosesAllWindowsAndStopsTransportInReverseOrder()
    {
        // 安排
        var app = new Application(new ApplicationOptions { Name = "ShutdownApp" });
        app.SetPlatformApp(new ServerPlatformApp(app.Options));
        var first = new FakeLifecycleService { Name = "First" };
        var second = new FakeLifecycleService { Name = "Second" };
        app.RegisterService(first);
        app.RegisterService(second);
        var transport = new FakeTransport();
        app.Transport = transport;

        // 先标记为运行状态
        var field = typeof(Application).GetField("_isRunning",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(app, true);

        // 操作
        app.Shutdown();

        // 断言：传输层已停止
        await Assert.That(transport.Stopped).IsTrue();
        // 断言：两个服务均被关闭
        await Assert.That(first.ShutdownCalled).IsTrue();
        await Assert.That(second.ShutdownCalled).IsTrue();
        // 断言：不再处于运行状态
        await Assert.That(app.IsRunning).IsFalse();
    }

    /// <summary>
    /// 用于测试服务生命周期的假服务。
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

    /// <summary>
    /// 用于测试传输层生命周期的假传输层。
    /// </summary>
    private sealed class FakeTransport : Wails.Net.Application.Transport.ITransport
    {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }

        public string JSClient() => string.Empty;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            return Task.CompletedTask;
        }
    }
}
