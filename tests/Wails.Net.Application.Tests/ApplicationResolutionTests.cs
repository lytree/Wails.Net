using System.Reflection;
using TUnit.Core;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Security;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests;

/// <summary>
/// R1-3 验证：Application 引用注入路径。
/// 审计 §3.1 / R1-3 目标：新增代码应通过构造期或 DI 注入获取 Application 引用，
/// 而非通过 <see cref="Application.Get"/> 静态访问（服务定位器反模式）。
/// 本测试类覆盖 WindowManager 内部 Application 引用解析的契约。
/// </summary>
[NotInParallel]
public sealed class ApplicationResolutionTests
{
    /// <summary>
    /// 通过反射设置窗口的平台实现，因为 Impl 的 setter 是 internal 的。
    /// </summary>
    private static void SetWindowImpl(WebviewWindow window)
    {
        var impl = new ServerWebviewWindow();
        var property = typeof(WebviewWindow).GetProperty(nameof(WebviewWindow.Impl))!;
        property.SetValue(window, impl);
    }

    [Test]
    public async Task WindowManager_Constructor_AcceptsApplicationInjection()
    {
        // 安排
        var app = new Application(new ApplicationOptions());

        // 操作：直接构造 WindowManager，传入 Application 引用
        var manager = new WindowManager(platformApp: null, application: app);

        // 断言：可通过反射读取 _application 字段验证注入成功
        var field = typeof(WindowManager).GetField("_application",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var stored = field.GetValue(manager);

        await Assert.That(stored).IsSameReferenceAs(app);
    }

    [Test]
    public async Task Application_Constructor_PassesSelfToWindowManager()
    {
        // 安排 + 操作：构造 Application
        var app = new Application(new ApplicationOptions());

        // 断言：Application 构造期已将自身引用注入 _windowManager._application
        var windowManager = (WindowManager)app.WindowManager!;
        var field = typeof(WindowManager).GetField("_application",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var stored = field.GetValue(windowManager);

        await Assert.That(stored).IsSameReferenceAs(app);
    }

    [Test]
    public async Task Application_SetPlatformApp_KeepsApplicationInjection()
    {
        // 安排：构造 Application + 注入 WindowManager（带 this）
        var app = new Application(new ApplicationOptions());
        var windowManager = (WindowManager)app.WindowManager!;
        var field = typeof(WindowManager).GetField("_application",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        await Assert.That(field.GetValue(windowManager)).IsSameReferenceAs(app);

        // 操作：调用 SetPlatformApp（不应清除 Application 引用）
        app.SetPlatformApp(new ServerPlatformApp(app.Options));

        // 断言：Application 引用仍保留
        var windowManagerAfter = (WindowManager)app.WindowManager!;
        await Assert.That(field.GetValue(windowManagerAfter)).IsSameReferenceAs(app);
    }

    [Test]
    public async Task WindowManager_CreateWebviewWindow_DoesNotRequireGlobalApplication()
    {
        // R1-3 关键不变量：WindowManager.CreateWebviewWindow 内部不应依赖
        // Application.Get() 静态访问 —— 通过构造期注入的 _application 即可工作。

        // 安排：构造独立 WindowManager（不依赖任何全局 Application 状态）
        var app = new Application(new ApplicationOptions());
        var manager = new WindowManager(platformApp: null, application: app);

        // 操作：创建窗口
        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "IsolatedWin" });
        var window = manager.GetWindow(id)!;
        SetWindowImpl(window);

        // 断言：窗口成功创建并注册到 WindowManager
        await Assert.That(window).IsNotNull();
        await Assert.That(window.Name).IsEqualTo("IsolatedWin");
        await Assert.That(manager.GetWindow(id)).IsSameReferenceAs(window);
        await Assert.That(manager.GetWindowByName("IsolatedWin")).IsSameReferenceAs(window);
    }

    [Test]
    public async Task Application_CreateWebviewWindow_UsesInjectedApplicationReference()
    {
        // 安排
        var app = new Application(new ApplicationOptions());
        var windowManager = (WindowManager)app.WindowManager!;

        // 操作：通过 Application.CreateWebviewWindow 创建窗口
        var window = app.CreateWebviewWindow(new WebviewWindowOptions { Name = "ViaApp" });
        SetWindowImpl(window);

        // 断言：窗口同时注册到 WindowManager，且 WindowManager 内部 _application 指向 app
        var field = typeof(WindowManager).GetField("_application",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        await Assert.That(field.GetValue(windowManager)).IsSameReferenceAs(app);
        await Assert.That(windowManager.GetWindowByName("ViaApp")).IsSameReferenceAs(window);
    }
}
