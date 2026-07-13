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
}
