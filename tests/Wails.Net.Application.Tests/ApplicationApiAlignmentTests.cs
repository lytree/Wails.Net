using System.Reflection;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Screens;
using Wails.Net.Events;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 主题 D：Application API 补齐的单元测试（TUnit）。
/// 覆盖 BundleID、GetCurrentWindowID、SetParent、ApplicationActive/Inactive 事件、
/// OnSecondInstanceLaunch 回调、KnownEvents 新增常量等。
/// 注意：Application 构造函数会设置静态全局实例，因此此类不并行执行。
/// </summary>
[NotInParallel]
public sealed class ApplicationApiAlignmentTests
{
    /// <summary>
    /// 可观察的平台应用测试桩，记录 SetParent 调用并允许配置 GetCurrentWindowId 返回值。
    /// </summary>
    private sealed class ObservablePlatformApp : IPlatformApp
    {
        public string Name => "ObservableApp";
        public uint CurrentWindowIdValue { get; set; } = 42u;
        public IntPtr? LastSetParentValue { get; private set; }
        public int SetParentCallCount { get; private set; }

        public uint GetCurrentWindowId() => CurrentWindowIdValue;

        public void SetParent(IntPtr parent)
        {
            LastSetParentValue = parent;
            SetParentCallCount++;
        }

        // 以下成员提供 IPlatformApp 其余成员的最小可运行实现
        public int Run() => 0;
        public bool AcquireSingleInstanceLock(string uniqueId) => true;
        public void NotifySingleInstance(string[] args) { }
        public void Destroy() { }
        public void SetApplicationMenu(Menus.Menu? menu) { }
        public void ShowAboutDialog(string name, string description, byte[]? icon) { }
        public void SetIcon(byte[]? icon) { }
        public void On(uint id) { }
        public void DispatchOnMainThread(uint id) { }
        public void Hide() { }
        public void Show() { }
        public Screens.Screen? GetPrimaryScreen() => null;
        public Screens.Screen[] GetScreens() => Array.Empty<Screens.Screen>();
        public Dictionary<string, object?> GetFlags(ApplicationOptions options) => new();
        public bool IsOnMainThread() => false;
        public bool IsDarkMode() => false;
        public string GetAccentColor() => "#000000";
        public void DispatchOnMainThread(Action action) => action();
        public void CreateWebviewWindow(uint id, WebviewWindowOptions options) { }
        public Task<int> ShowMessageDialog(string title, string message, Dialogs.DialogStyle style, string[] buttons)
            => Task.FromResult(0);
        public Task<string?> OpenFileDialog(Dialogs.OpenFileDialogOptions options) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileDialog(Dialogs.SaveFileDialogOptions options) => Task.FromResult<string?>(null);
        public Task<string[]?> OpenMultipleFilesDialog(Dialogs.OpenFileDialogOptions options)
            => Task.FromResult<string[]?>(null);
    }

    // ============== D-1: ApplicationOptions.BundleID ==============

    [Test]
    public async Task ApplicationOptions_BundleID_DefaultsToNull()
    {
        var options = new ApplicationOptions();
        await Assert.That(options.BundleID).IsNull();
    }

    [Test]
    public async Task ApplicationOptions_BundleID_CanBeSet()
    {
        var options = new ApplicationOptions { BundleID = "com.company.appname" };
        await Assert.That(options.BundleID).IsEqualTo("com.company.appname");
    }

    // ============== D-2: Application.GetCurrentWindowID ==============

    [Test]
    public async Task GetCurrentWindowID_ReturnsZeroWhenPlatformAppNotSet()
    {
        var app = new Application(new ApplicationOptions());
        await Assert.That(app.GetCurrentWindowID()).IsEqualTo(0u);
    }

    [Test]
    public async Task GetCurrentWindowID_DelegatesToPlatformApp()
    {
        var app = new Application(new ApplicationOptions());
        var platform = new ObservablePlatformApp { CurrentWindowIdValue = 99u };
        app.SetPlatformApp(platform);

        await Assert.That(app.GetCurrentWindowID()).IsEqualTo(99u);
    }

    // ============== D-5/D-6: Application.SetParent ==============

    [Test]
    public async Task SetParent_DoesNotThrowWhenPlatformAppNotSet()
    {
        var app = new Application(new ApplicationOptions());
        var parent = new IntPtr(0x1234);
        await Assert.That(() => app.SetParent(parent)).ThrowsNothing();
    }

    [Test]
    public async Task SetParent_DelegatesToPlatformApp()
    {
        var app = new Application(new ApplicationOptions());
        var platform = new ObservablePlatformApp();
        app.SetPlatformApp(platform);

        var parent = new IntPtr(0xABCD);
        app.SetParent(parent);

        await Assert.That(platform.SetParentCallCount).IsEqualTo(1);
        await Assert.That(platform.LastSetParentValue).IsEqualTo(parent);
    }

    [Test]
    public async Task IPlatformApp_SetParent_DefaultImplementationIsNoOp()
    {
        // ServerPlatformApp 未覆盖 SetParent，应使用接口默认 no-op 实现
        IPlatformApp platform = new ServerPlatformApp(new ApplicationOptions());
        await Assert.That(() => platform.SetParent(new IntPtr(0x100))).ThrowsNothing();
    }

    // ============== D-6: Application.OnSecondInstanceLaunch ==============

    [Test]
    public async Task RaiseSecondInstanceLaunched_InvokesRegisteredCallback()
    {
        var app = new Application(new ApplicationOptions());
        string[]? receivedArgs = null;
        app.OnSecondInstanceLaunch(args => receivedArgs = args);

        var sentArgs = new[] { "arg1", "arg2" };
        app.RaiseSecondInstanceLaunched(sentArgs);

        await Assert.That(receivedArgs).IsSameReferenceAs(sentArgs);
    }

    [Test]
    public async Task RaiseSecondInstanceLaunched_WithoutCallbackDoesNotThrow()
    {
        var app = new Application(new ApplicationOptions());
        await Assert.That(() => app.RaiseSecondInstanceLaunched(Array.Empty<string>())).ThrowsNothing();
    }

    [Test]
    public async Task RaiseSecondInstanceLaunched_SwallowsCallbackExceptions()
    {
        var app = new Application(new ApplicationOptions());
        app.OnSecondInstanceLaunch(_ => throw new InvalidOperationException("boom"));

        // 即使回调抛出异常，也不应向外传播
        await Assert.That(() => app.RaiseSecondInstanceLaunched(Array.Empty<string>())).ThrowsNothing();
    }

    [Test]
    public async Task RaiseSecondInstanceLaunched_LastRegisteredCallbackWins()
    {
        var app = new Application(new ApplicationOptions());
        var firstCallCount = 0;
        var secondCallCount = 0;
        app.OnSecondInstanceLaunch(_ => firstCallCount++);
        app.OnSecondInstanceLaunch(_ => secondCallCount++);

        app.RaiseSecondInstanceLaunched(Array.Empty<string>());

        // 后注册的回调覆盖前者（与 Wails v3 单回调语义一致）
        await Assert.That(firstCallCount).IsEqualTo(0);
        await Assert.That(secondCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task RaiseSecondInstanceLaunched_EmitsSecondInstanceLaunchedEvent()
    {
        var app = new Application(new ApplicationOptions());
        var eventEmitted = false;
        object? receivedData = null;
        app.Events.On(KnownEvents.SecondInstanceLaunched, evt =>
        {
            eventEmitted = true;
            receivedData = evt.Data;
        });

        var args = new[] { "--flag", "value" };
        app.RaiseSecondInstanceLaunched(args);

        await Assert.That(eventEmitted).IsTrue();
        await Assert.That(receivedData).IsSameReferenceAs(args);
    }

    // ============== D-3/D-4: ApplicationEventType + KnownEvents 新增项 ==============

    [Test]
    public async Task KnownEvents_ApplicationActive_ConstantValue()
    {
        await Assert.That(KnownEvents.ApplicationActive).IsEqualTo("wails:application:active");
    }

    [Test]
    public async Task KnownEvents_ApplicationInactive_ConstantValue()
    {
        await Assert.That(KnownEvents.ApplicationInactive).IsEqualTo("wails:application:inactive");
    }

    [Test]
    public async Task KnownEvents_SecondInstanceLaunched_ConstantValue()
    {
        await Assert.That(KnownEvents.SecondInstanceLaunched).IsEqualTo("wails:second-instance:launched");
    }

    [Test]
    public async Task KnownEvents_GetEventName_ApplicationActive_ReturnsCorrectName()
    {
        await Assert.That(KnownEvents.GetEventName(ApplicationEventType.ApplicationActive))
            .IsEqualTo(KnownEvents.ApplicationActive);
    }

    [Test]
    public async Task KnownEvents_GetEventName_ApplicationInactive_ReturnsCorrectName()
    {
        await Assert.That(KnownEvents.GetEventName(ApplicationEventType.ApplicationInactive))
            .IsEqualTo(KnownEvents.ApplicationInactive);
    }

    [Test]
    public async Task KnownEvents_GetEventName_UIntDispatchesApplicationActive()
    {
        // uint 值 25 应通过 ApplicationEventType 路径解析为 ApplicationActive
        await Assert.That(KnownEvents.GetEventName(25u)).IsEqualTo(KnownEvents.ApplicationActive);
    }

    [Test]
    public async Task KnownEvents_GetEventName_UIntDispatchesApplicationInactive()
    {
        await Assert.That(KnownEvents.GetEventName(26u)).IsEqualTo(KnownEvents.ApplicationInactive);
    }
}
