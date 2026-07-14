using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Android.Tests;

/// <summary>
/// AndroidWebviewWindow 的单元测试（TUnit）。
/// 测试构造函数、属性查询、状态查询等不依赖真实 Android WebView 实例的逻辑。
/// </summary>
[NotInParallel]
public sealed class AndroidWebviewWindowTests
{
    private static AndroidPlatformApp CreateApp()
        => new(new ApplicationOptions { Name = "TestApp" });

    [Test]
    public async Task Constructor_CachesOptions()
    {
        // 安排
        var app = CreateApp();
        var options = new WebviewWindowOptions
        {
            Width = 800,
            Height = 600,
            MinWidth = 400,
            MinHeight = 300,
            MaxWidth = 1600,
            MaxHeight = 1200,
            X = 100,
            Y = 200,
            URL = "https://example.com"
        };

        // 操作
        var window = new AndroidWebviewWindow(1u, options, app);

        // 断言：构造时缓存了 options 的几何参数
        var size = window.GetSize();
        await Assert.That(size.Width).IsEqualTo(800);
        await Assert.That(size.Height).IsEqualTo(600);

        var minSize = window.GetMinSize();
        await Assert.That(minSize.Width).IsEqualTo(400);
        await Assert.That(minSize.Height).IsEqualTo(300);

        var maxSize = window.GetMaxSize();
        await Assert.That(maxSize.Width).IsEqualTo(1600);
        await Assert.That(maxSize.Height).IsEqualTo(1200);

        var pos = window.GetPosition();
        await Assert.That(pos.X).IsEqualTo(100);
        await Assert.That(pos.Y).IsEqualTo(200);
    }

    [Test]
    public async Task GetURL_ReturnsCachedUrl_WhenWebViewNotCreated()
    {
        // 安排
        var app = CreateApp();
        var options = new WebviewWindowOptions { URL = "https://test.example.com" };

        // 操作
        var window = new AndroidWebviewWindow(1u, options, app);

        // 断言：WebView 未创建时返回缓存的 URL
        await Assert.That(window.GetURL()).IsEqualTo("https://test.example.com");
    }

    [Test]
    public async Task IsVisible_ReturnsFalse_BeforeShow()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：未 Show 之前不可见
        await Assert.That(window.IsVisible()).IsFalse();
    }

    [Test]
    public async Task IsFullscreen_ReturnsFalse_Initially()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：初始未全屏
        await Assert.That(window.IsFullscreen()).IsFalse();
    }

    [Test]
    public async Task Fullscreen_SetsState()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作
        window.Fullscreen();

        // 断言
        await Assert.That(window.IsFullscreen()).IsTrue();

        // 操作：取消全屏
        window.UnFullscreen();
        await Assert.That(window.IsFullscreen()).IsFalse();
    }

    [Test]
    public async Task IsMaximised_AlwaysReturnsFalse()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：Android 无窗口最大化概念
        await Assert.That(window.IsMaximised()).IsFalse();
    }

    [Test]
    public async Task IsMinimised_AlwaysReturnsFalse()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：Android 无窗口最小化概念
        await Assert.That(window.IsMinimised()).IsFalse();
    }

    [Test]
    public async Task SetSize_UpdatesGetSize()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions { Width = 100, Height = 100 }, app);

        // 操作
        window.SetSize(1024, 768);

        // 断言
        var size = window.GetSize();
        await Assert.That(size.Width).IsEqualTo(1024);
        await Assert.That(size.Height).IsEqualTo(768);
    }

    [Test]
    public async Task SetPosition_UpdatesGetPosition()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions { X = 0, Y = 0 }, app);

        // 操作
        window.SetPosition(250, 350);

        // 断言
        var pos = window.GetPosition();
        await Assert.That(pos.X).IsEqualTo(250);
        await Assert.That(pos.Y).IsEqualTo(350);
    }

    [Test]
    public async Task SetZoom_UpdatesGetZoom()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作
        window.SetZoom(1.5f);

        // 断言
        await Assert.That(window.GetZoom()).IsEqualTo(1.5f);
    }

    [Test]
    public async Task PrintToPDF_ThrowsNotSupportedException()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：Android WebView 不支持 PDF 导出
        await Assert.That(() => window.PrintToPDF("/tmp/test.pdf")).ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task SetMinSize_UpdatesGetMinSize()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions { MinWidth = 100, MinHeight = 100 }, app);

        // 操作
        window.SetMinSize(320, 240);

        // 断言
        var minSize = window.GetMinSize();
        await Assert.That(minSize.Width).IsEqualTo(320);
        await Assert.That(minSize.Height).IsEqualTo(240);
    }

    [Test]
    public async Task SetMaxSize_UpdatesGetMaxSize()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions { MaxWidth = 1000, MaxHeight = 1000 }, app);

        // 操作
        window.SetMaxSize(1920, 1080);

        // 断言
        var maxSize = window.GetMaxSize();
        await Assert.That(maxSize.Width).IsEqualTo(1920);
        await Assert.That(maxSize.Height).IsEqualTo(1080);
    }

    [Test]
    public async Task GetContentSize_ReturnsSameAsGetSize()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions { Width = 640, Height = 480 }, app);

        // 操作
        var size = window.GetSize();
        var contentSize = window.GetContentSize();

        // 断言：Android WebView 内容区域与窗口大小一致
        await Assert.That(contentSize.Width).IsEqualTo(size.Width);
        await Assert.That(contentSize.Height).IsEqualTo(size.Height);
    }

    [Test]
    public async Task SetURL_UpdatesGetURL()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作：WebView 未创建时仅更新缓存
        window.SetURL("https://updated.example.com");

        // 断言
        await Assert.That(window.GetURL()).IsEqualTo("https://updated.example.com");
    }

    [Test]
    public async Task LoadURL_UpdatesGetURL()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作：WebView 未创建时仅更新缓存
        window.LoadURL("https://loaded.example.com");

        // 断言
        await Assert.That(window.GetURL()).IsEqualTo("https://loaded.example.com");
    }

    [Test]
    public async Task Hide_SetsIsVisibleFalse()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作
        window.Hide();

        // 断言
        await Assert.That(window.IsVisible()).IsFalse();
    }

    [Test]
    public async Task Close_SetsIsVisibleFalse()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作：WebView 未创建时 Close 仍设置 _visible 为 false
        window.Close();

        // 断言
        await Assert.That(window.IsVisible()).IsFalse();
    }

    [Test]
    public async Task SetZoomLevel_UpdatesGetZoomLevel()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作
        window.SetZoomLevel(2.5f);

        // 断言
        await Assert.That(window.GetZoomLevel()).IsEqualTo(2.5f);
    }

    [Test]
    public async Task IsFocused_ReturnsFalse_WhenWebViewNotCreated()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：WebView 未创建时 HasFocus 为 null，== true 为 false
        await Assert.That(window.IsFocused()).IsFalse();
    }

    [Test]
    public async Task IsMaximised_ReturnsFalse_AfterMaximiseCalled()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作：调用 Maximise（no-op）
        window.Maximise();

        // 断言：Android 无窗口最大化概念，仍返回 false
        await Assert.That(window.IsMaximised()).IsFalse();
    }

    [Test]
    public async Task IsMinimised_ReturnsFalse_AfterMinimiseCalled()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作：调用 Minimise（no-op）
        window.Minimise();

        // 断言：Android 无窗口最小化概念，仍返回 false
        await Assert.That(window.IsMinimised()).IsFalse();
    }

    [Test]
    public async Task GetURL_ReturnsEmptyString_WhenNoUrlOptionProvided()
    {
        // 安排：不设置 URL 选项
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：无 URL 时返回空字符串
        await Assert.That(window.GetURL()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SetHTML_DoesNotThrow_WhenWebViewNotCreated()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：WebView 未创建时仅更新缓存，不抛异常
        await Assert.That(() => window.SetHTML("<h1>Test</h1>")).ThrowsNothing();
    }

    [Test]
    public async Task LoadHTML_DoesNotThrow_WhenWebViewNotCreated()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：WebView 未创建时仅更新缓存，不抛异常
        await Assert.That(() => window.LoadHTML("<h1>Loaded</h1>")).ThrowsNothing();
    }

    [Test]
    public async Task Reload_DoesNotThrow_WhenWebViewNotCreated()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：WebView 未创建时 _webView 为 null，?. 安全调用不抛异常
        await Assert.That(() => window.Reload()).ThrowsNothing();
    }

    [Test]
    public async Task GoBack_DoesNotThrow_WhenWebViewNotCreated()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言
        await Assert.That(() => window.GoBack()).ThrowsNothing();
    }

    [Test]
    public async Task GoForward_DoesNotThrow_WhenWebViewNotCreated()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言
        await Assert.That(() => window.GoForward()).ThrowsNothing();
    }

    [Test]
    public async Task ExecJS_DoesNotThrow_WhenWebViewNotCreated()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：WebView 未创建时 _webView 为 null，?. 安全调用不抛异常
        await Assert.That(() => window.ExecJS("alert('hello')")).ThrowsNothing();
    }

    [Test]
    public async Task Focus_DoesNotThrow_WhenWebViewNotCreated()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：WebView 未创建时 _webView 为 null，?. 安全调用不抛异常
        await Assert.That(() => window.Focus()).ThrowsNothing();
    }

    [Test]
    public async Task SetBackgroundColour_ByteOverload_DoesNotThrow()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：骨架实现为 no-op，不抛异常
        await Assert.That(() => window.SetBackgroundColour(255, 128, 0, 255)).ThrowsNothing();
    }

    [Test]
    public async Task SetBackgroundColour_IntOverload_DoesNotThrow()
    {
        // 安排
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：int 重载委托给 byte 重载，不抛异常
        await Assert.That(() => window.SetBackgroundColour(0, 255, 0, 128)).ThrowsNothing();
    }

    [Test]
    public async Task ShowMenuBar_DoesNotThrow_AlwaysNoOp()
    {
        // 安排：Android 无菜单栏概念
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：no-op 实现
        await Assert.That(() => window.ShowMenuBar()).ThrowsNothing();
    }

    [Test]
    public async Task HideMenuBar_DoesNotThrow_AlwaysNoOp()
    {
        // 安排：Android 无菜单栏概念
        var app = CreateApp();
        var window = new AndroidWebviewWindow(1u, new WebviewWindowOptions(), app);

        // 操作与断言：no-op 实现
        await Assert.That(() => window.HideMenuBar()).ThrowsNothing();
    }
}
