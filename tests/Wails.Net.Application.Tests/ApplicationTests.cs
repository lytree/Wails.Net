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
    public async Task Run_ThrowsNotImplementedException()
    {
        // 安排
        var app = new Application(new ApplicationOptions());

        // 操作与断言
        await Assert.That(() => app.Run()).ThrowsExactly<NotImplementedException>();
    }

    [Test]
    public async Task Shutdown_ThrowsNotImplementedException()
    {
        // 安排
        var app = new Application(new ApplicationOptions());

        // 操作与断言
        await Assert.That(() => app.Shutdown()).ThrowsExactly<NotImplementedException>();
    }
}
