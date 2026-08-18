using System.Reflection;
using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests;

/// <summary>
/// R1-2 验证：Application 与 WindowManager 共享同一窗口注册表。
/// 审计 §3.4 P0 指出原实现同时持有 <c>Application._windows</c> + <c>WindowManager._windows</c>
/// 两套注册表，导致 EventIPCTransport 枚举窗口时遗漏 Application 路径创建的非 Native IPC 窗口。
/// 本测试类覆盖修复后的统一注册表契约。
/// </summary>
[NotInParallel]
public sealed class WindowRegistryUnificationTests
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
    public async Task Application_CreateWebviewWindow_VisibleInWindowManager()
    {
        // 安排
        var app = new Application(new ApplicationOptions());
        var windowManager = (WindowManager)app.WindowManager!;

        // 操作
        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "Unified" });
        SetWindowImpl(window);

        // 断言：Application.CreateWebviewWindow 创建的窗口同时出现在 WindowManager 注册表
        await Assert.That(windowManager.GetWindow(window.ID)).IsSameReferenceAs(window);
        await Assert.That(windowManager.GetWindowByName("Unified")).IsSameReferenceAs(window);
    }

    [Test]
    public async Task Application_Windows_ReturnsSameAsWindowManagerAllWindows()
    {
        // 安排
        var app = new Application(new ApplicationOptions());
        var windowManager = (WindowManager)app.WindowManager!;

        var window1 = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "W1" });
        var window2 = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "W2" });
        SetWindowImpl(window1);
        SetWindowImpl(window2);

        // 操作 + 断言：Application.Windows 与 WindowManager.AllWindows 等价
        var appWindows = app.Windows;
        var managerWindows = windowManager.AllWindows;

        await Assert.That(appWindows.Count).IsEqualTo(managerWindows.Count);
        await Assert.That(appWindows.Count).IsEqualTo(2);
        foreach (var w in managerWindows)
        {
            await Assert.That(appWindows).Contains(w);
        }
    }

    [Test]
    public async Task Application_GetWindowByName_MatchesWindowManager()
    {
        // 安排
        var app = new Application(new ApplicationOptions());
        var windowManager = (WindowManager)app.WindowManager!;

        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "NamedWindow" });
        SetWindowImpl(window);

        // 操作 + 断言：两条路径返回同一实例
        var fromApp = app.GetWindowByName("NamedWindow");
        var fromManager = windowManager.GetWindowByName("NamedWindow");

        await Assert.That(fromApp).IsNotNull();
        await Assert.That(fromManager).IsNotNull();
        await Assert.That(fromApp).IsSameReferenceAs(fromManager);
        await Assert.That(fromApp).IsSameReferenceAs(window);
    }

    [Test]
    public async Task Application_DestroyWindow_RemovesFromWindowManager()
    {
        // 安排
        var app = new Application(new ApplicationOptions());
        var windowManager = (WindowManager)app.WindowManager!;

        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "ToDestroy" });
        SetWindowImpl(window);
        await Assert.That(windowManager.GetWindow(window.ID)).IsSameReferenceAs(window);

        // 操作
        app.DestroyWindow(window.ID);

        // 断言：Application.DestroyWindow 后 WindowManager 同步移除
        await Assert.That(windowManager.GetWindow(window.ID)).IsNull();
        await Assert.That(windowManager.GetWindowByName("ToDestroy")).IsNull();
        await Assert.That(windowManager.AllWindows).DoesNotContain(window);
        await Assert.That(app.Windows).DoesNotContain(window);
        await Assert.That(app.GetWindow(window.ID)).IsNull();
    }
}
