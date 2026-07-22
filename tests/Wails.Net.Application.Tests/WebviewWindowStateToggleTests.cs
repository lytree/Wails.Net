using TUnit.Core;
using Wails.Net.Application.Logging;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests;

/// <summary>
/// WebviewWindow 状态切换方法的单元测试（TUnit）。
/// 对应项 6：ToggleFullscreen/ToggleMaximise/ToggleFrameless/ForceReload/Flash。
/// </summary>
[NotInParallel]
public sealed class WebviewWindowStateToggleTests
{
    /// <summary>
    /// 创建带记录型桩实现的 WebviewWindow 实例。
    /// </summary>
    private static (WebviewWindow window, RecordingWebviewWindowStub stub) CreateWindow(bool frameless = false)
    {
        var stub = new RecordingWebviewWindowStub();
        var window = new WebviewWindow(1, "Test", new WebviewWindowOptions { Name = "Test", Frameless = frameless })
        {
            Impl = stub
        };
        return (window, stub);
    }

    [Test]
    public async Task ToggleFullscreen_WhenNotFullscreen_CallsFullscreen()
    {
        // 安排
        var (window, stub) = CreateWindow();
        stub.FullscreenReturnValue = false;

        // 操作
        window.ToggleFullscreen();

        // 断言
        await Assert.That(stub.FullscreenCallCount).IsEqualTo(1);
        await Assert.That(stub.UnFullscreenCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task ToggleFullscreen_WhenFullscreen_CallsUnFullscreen()
    {
        // 安排
        var (window, stub) = CreateWindow();
        stub.FullscreenReturnValue = true;

        // 操作
        window.ToggleFullscreen();

        // 断言
        await Assert.That(stub.FullscreenCallCount).IsEqualTo(0);
        await Assert.That(stub.UnFullscreenCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task ToggleMaximise_WhenNotMaximised_CallsMaximise()
    {
        // 安排
        var (window, stub) = CreateWindow();
        stub.MaximisedReturnValue = false;

        // 操作
        window.ToggleMaximise();

        // 断言
        await Assert.That(stub.MaximiseCallCount).IsEqualTo(1);
        await Assert.That(stub.UnMaximiseCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task ToggleMaximise_WhenMaximised_CallsUnMaximise()
    {
        // 安排
        var (window, stub) = CreateWindow();
        stub.MaximisedReturnValue = true;

        // 操作
        window.ToggleMaximise();

        // 断言
        await Assert.That(stub.MaximiseCallCount).IsEqualTo(0);
        await Assert.That(stub.UnMaximiseCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task ToggleFrameless_WhenFramed_CallsSetFramelessTrue()
    {
        // 安排 - 初始为有边框（Frameless=false）
        var (window, stub) = CreateWindow(frameless: false);

        // 操作
        window.ToggleFrameless();

        // 断言
        await Assert.That(stub.SetFramelessCallCount).IsEqualTo(1);
        await Assert.That(stub.LastFramelessValue).IsTrue();
        await Assert.That(window.Options.Frameless).IsTrue();
    }

    [Test]
    public async Task ToggleFrameless_WhenFrameless_CallsSetFramelessFalse()
    {
        // 安排 - 初始为无边框（Frameless=true）
        var (window, stub) = CreateWindow(frameless: true);

        // 操作
        window.ToggleFrameless();

        // 断言
        await Assert.That(stub.SetFramelessCallCount).IsEqualTo(1);
        await Assert.That(stub.LastFramelessValue).IsFalse();
        await Assert.That(window.Options.Frameless).IsFalse();
    }

    [Test]
    public async Task ToggleFrameless_CalledTwice_ReturnsToOriginalState()
    {
        // 安排
        var (window, stub) = CreateWindow(frameless: false);

        // 操作 - 切换两次
        window.ToggleFrameless();
        window.ToggleFrameless();

        // 断言 - 回到原始状态
        await Assert.That(stub.SetFramelessCallCount).IsEqualTo(2);
        await Assert.That(window.Options.Frameless).IsFalse();
    }

    [Test]
    public async Task SetFrameless_UpdatesOptionsFrameless()
    {
        // 安排
        var (window, stub) = CreateWindow(frameless: false);

        // 操作
        window.SetFrameless(true);

        // 断言 - Options.Frameless 被同步更新
        await Assert.That(window.Options.Frameless).IsTrue();
        await Assert.That(stub.LastFramelessValue).IsTrue();
    }

    [Test]
    public async Task ForceReload_DelegatesToImpl()
    {
        // 安排
        var (window, stub) = CreateWindow();

        // 操作
        window.ForceReload();

        // 断言
        await Assert.That(stub.ForceReloadCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Flash_DelegatesToImpl()
    {
        // 安排
        var (window, stub) = CreateWindow();

        // 操作
        window.Flash(true);

        // 断言
        await Assert.That(stub.FlashCallCount).IsEqualTo(1);
        await Assert.That(stub.LastFlashValue).IsTrue();
    }

    [Test]
    public async Task Flash_False_DelegatesToImpl()
    {
        // 安排
        var (window, stub) = CreateWindow();

        // 操作
        window.Flash(false);

        // 断言
        await Assert.That(stub.FlashCallCount).IsEqualTo(1);
        await Assert.That(stub.LastFlashValue).IsFalse();
    }

    [Test]
    public async Task ForceReload_InterfaceDefaultImpl_DelegatesToReload()
    {
        // 安排 - 使用仅 override Reload 的 stub 验证接口默认实现
        var stub = new ReloadOnlyStub();
        IWebviewWindowImpl impl = stub;

        // 操作 - 调用接口默认实现 ForceReload
        impl.ForceReload();

        // 断言 - 默认实现委托给 Reload
        await Assert.That(stub.ReloadCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Flash_InterfaceDefaultImpl_DoesNotThrow()
    {
        // 安排
        IWebviewWindowImpl impl = new ReloadOnlyStub();

        // 操作 + 断言 - 默认实现为 no-op，不抛异常
        await Assert.That(() => impl.Flash(true)).ThrowsNothing();
        await Assert.That(() => impl.Flash(false)).ThrowsNothing();
    }
}

/// <summary>
/// 记录型 WebviewWindow 桩实现，记录关键方法调用次数和参数。
/// </summary>
internal sealed class RecordingWebviewWindowStub : IWebviewWindowImpl
{
    public uint Id { get; set; }

    // 记录字段
    public int FullscreenCallCount;
    public int UnFullscreenCallCount;
    public int MaximiseCallCount;
    public int UnMaximiseCallCount;
    public int SetFramelessCallCount;
    public bool LastFramelessValue;
    public int ReloadCallCount;
    public int ForceReloadCallCount;
    public int FlashCallCount;
    public bool LastFlashValue;

    // 返回值控制
    public bool FullscreenReturnValue;
    public bool MaximisedReturnValue;

    public void SetTitle(string title) { }
    public void SetSize(int width, int height) { }
    public void SetMinSize(int width, int height) { }
    public void SetMaxSize(int width, int height) { }
    public void SetPosition(int x, int y) { }
    public void Show() { }
    public void Hide() { }
    public void Maximise() => MaximiseCallCount++;
    public void UnMaximise() => UnMaximiseCallCount++;
    public void Minimise() { }
    public void UnMinimise() { }
    public void Fullscreen() => FullscreenCallCount++;
    public void UnFullscreen() => UnFullscreenCallCount++;
    public void Restore() { }
    public void Close() { }
    public void Focus() { }
    public void ShowMenuBar() { }
    public void HideMenuBar() { }
    public void ToggleMenuBar() { }
    public void SetAlwaysOnTop(bool onTop) { }
    public void SetBackgroundColour(byte r, byte g, byte b, byte a) { }
    public void SetBackgroundColour(int r, int g, int b, int a) { }
    public bool IsFullscreen() => FullscreenReturnValue;
    public bool IsMaximised() => MaximisedReturnValue;
    public bool IsMinimised() => false;
    public bool IsVisible() => true;
    public bool IsFocused() => false;
    public void SetFrameless(bool frameless)
    {
        SetFramelessCallCount++;
        LastFramelessValue = frameless;
    }
    public void OpenDevTools() { }
    public void CloseDevTools() { }
    public void SetZoom(float zoom) { }
    public void SetZoomLevel(float level) { }
    public (int Width, int Height) GetSize() => (0, 0);
    public (int Width, int Height) GetContentSize() => (0, 0);
    public (int Width, int Height) GetMinSize() => (0, 0);
    public (int Width, int Height) GetMaxSize() => (0, 0);
    public (int X, int Y) GetPosition() => (0, 0);
    public float GetZoom() => 1f;
    public float GetZoomLevel() => 0f;
    public void ExecJS(string js) { }
    public void GoBack() { }
    public void GoForward() { }
    public void Reload() => ReloadCallCount++;
    public void ForceReload() => ForceReloadCallCount++;
    public void Flash(bool enabled)
    {
        FlashCallCount++;
        LastFlashValue = enabled;
    }
    public void SetURL(string url) { }
    public void SetHTML(string html) { }
    public void Print() { }
    public void PrintToPDF(string path) { }
    public void SetMenu(Menu? menu) { }
    public void StartDrag() { }
    public void StartResize() { }
    public void SetEnabled(bool enabled) { }
    public void SetContentProtection(bool enabled) { }
    public void AttachAsModal(uint parentWindowId) { }
    public void SetResizable(bool resizable) { }
    public void SetMaximisable(bool maximisable) { }
    public void SetMinimisable(bool minimisable) { }
    public void SetClosable(bool closable) { }
    public void SetHasShadow(bool hasShadow) { }
    public void SetTitleBarStyle(TitleBarStyle style) { }
    public void Centre() { }
    public void SetDebuggingEnabled(bool enabled) { }
    public string GetURL() => string.Empty;
    public void LoadURL(string url) { }
    public void LoadHTML(string html) { }
}

/// <summary>
/// 仅实现 Reload 的桩，用于验证 ForceReload 的接口默认实现委托给 Reload。
/// 其余必需方法留空。
/// </summary>
internal sealed class ReloadOnlyStub : IWebviewWindowImpl
{
    public int ReloadCallCount;

    public uint Id => 0;
    public void SetTitle(string title) { }
    public void SetSize(int width, int height) { }
    public void SetMinSize(int width, int height) { }
    public void SetMaxSize(int width, int height) { }
    public void SetPosition(int x, int y) { }
    public void Show() { }
    public void Hide() { }
    public void Maximise() { }
    public void UnMaximise() { }
    public void Minimise() { }
    public void UnMinimise() { }
    public void Fullscreen() { }
    public void UnFullscreen() { }
    public void Restore() { }
    public void Close() { }
    public void Focus() { }
    public void ShowMenuBar() { }
    public void HideMenuBar() { }
    public void ToggleMenuBar() { }
    public void SetAlwaysOnTop(bool onTop) { }
    public void SetBackgroundColour(byte r, byte g, byte b, byte a) { }
    public void SetBackgroundColour(int r, int g, int b, int a) { }
    public bool IsFullscreen() => false;
    public bool IsMaximised() => false;
    public bool IsMinimised() => false;
    public bool IsVisible() => true;
    public bool IsFocused() => false;
    public void SetFrameless(bool frameless) { }
    public void OpenDevTools() { }
    public void CloseDevTools() { }
    public void SetZoom(float zoom) { }
    public void SetZoomLevel(float level) { }
    public (int Width, int Height) GetSize() => (0, 0);
    public (int Width, int Height) GetContentSize() => (0, 0);
    public (int Width, int Height) GetMinSize() => (0, 0);
    public (int Width, int Height) GetMaxSize() => (0, 0);
    public (int X, int Y) GetPosition() => (0, 0);
    public float GetZoom() => 1f;
    public float GetZoomLevel() => 0f;
    public void ExecJS(string js) { }
    public void GoBack() { }
    public void GoForward() { }
    public void Reload() => ReloadCallCount++;
    public void SetURL(string url) { }
    public void SetHTML(string html) { }
    public void Print() { }
    public void PrintToPDF(string path) { }
    public void SetMenu(Menu? menu) { }
    public void StartDrag() { }
    public void StartResize() { }
    public void SetEnabled(bool enabled) { }
    public void SetContentProtection(bool enabled) { }
    public void AttachAsModal(uint parentWindowId) { }
    public void SetResizable(bool resizable) { }
    public void SetMaximisable(bool maximisable) { }
    public void SetMinimisable(bool minimisable) { }
    public void SetClosable(bool closable) { }
    public void SetHasShadow(bool hasShadow) { }
    public void SetTitleBarStyle(TitleBarStyle style) { }
    public void Centre() { }
    public void SetDebuggingEnabled(bool enabled) { }
    public string GetURL() => string.Empty;
    public void LoadURL(string url) { }
    public void LoadHTML(string html) { }
}
