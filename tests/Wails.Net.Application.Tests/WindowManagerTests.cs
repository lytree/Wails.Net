using System.Collections.Concurrent;
using System.Reflection;
using TUnit.Core;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests;

/// <summary>
/// WindowManager 的单元测试（TUnit）。
/// 测试窗口创建、查询、销毁和线程安全 ID 生成。
/// </summary>
[NotInParallel]
public sealed class WindowManagerTests
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
    public async Task Constructor_WithNullPlatformApp_AllowsServerMode()
    {
        // 安排与操作
        var manager = new WindowManager(null);

        // 断言
        await Assert.That(manager.Count).IsEqualTo(0);
        await Assert.That(manager.AllWindows).IsEmpty();
    }

    [Test]
    public async Task CreateWebviewWindow_ReturnsIncrementalIDs()
    {
        // 安排
        var manager = new WindowManager(null);

        // 操作
        var id1 = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "Window1" });
        var id2 = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "Window2" });
        var id3 = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "Window3" });

        // 断言
        await Assert.That(id1).IsEqualTo(1u);
        await Assert.That(id2).IsEqualTo(2u);
        await Assert.That(id3).IsEqualTo(3u);
    }

    [Test]
    public async Task CreateWebviewWindow_StoresWindow()
    {
        // 安排
        var manager = new WindowManager(null);

        // 操作
        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "TestWindow" });

        // 断言
        var window = manager.GetWindow(id);
        await Assert.That(window).IsNotNull();
        await Assert.That(window!.Name).IsEqualTo("TestWindow");
        await Assert.That(window.ID).IsEqualTo(id);
    }

    [Test]
    public async Task CreateWebviewWindow_IncrementsCount()
    {
        // 安排
        var manager = new WindowManager(null);

        // 操作
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W1" });
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W2" });

        // 断言
        await Assert.That(manager.Count).IsEqualTo(2);
    }

    [Test]
    public async Task CreateWebviewWindow_InvokesPlatformApp()
    {
        // 安排
        var fakePlatform = new FakePlatformApp();
        var manager = new WindowManager(fakePlatform);
        var options = new WebviewWindowOptions { Name = "PlatformWindow", Title = "Test" };

        // 操作
        var id = manager.CreateWebviewWindow(options);

        // 断言
        await Assert.That(fakePlatform.CreateWebviewWindowCalls).Count().IsEqualTo(1);
        var call = fakePlatform.CreateWebviewWindowCalls.ToArray()[0];
        await Assert.That(call.id).IsEqualTo(id);
        await Assert.That(call.options).IsSameReferenceAs(options);
    }

    [Test]
    public async Task CreateWebviewWindow_WithNullPlatformApp_DoesNotThrow()
    {
        // 安排
        var manager = new WindowManager(null);

        // 操作与断言
        await Assert.That(() => manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "NoPlatform" }))
            .ThrowsNothing();
    }

    [Test]
    public async Task GetWindow_ReturnsNullForUnknownID()
    {
        // 安排
        var manager = new WindowManager(null);

        // 操作与断言
        await Assert.That(manager.GetWindow(999u)).IsNull();
    }

    [Test]
    public async Task GetWindowByName_ReturnsCorrectWindow()
    {
        // 安排
        var manager = new WindowManager(null);

        // 操作
        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "FindMe" });

        // 断言
        var window = manager.GetWindowByName("FindMe");
        await Assert.That(window).IsNotNull();
        await Assert.That(window!.ID).IsEqualTo(id);
    }

    [Test]
    public async Task GetWindowByName_ReturnsNullForUnknownName()
    {
        // 安排
        var manager = new WindowManager(null);

        // 操作与断言
        await Assert.That(manager.GetWindowByName("Unknown")).IsNull();
    }

    [Test]
    public async Task GetWindowByName_WithEmptyName_ReturnsNull()
    {
        // 安排
        var manager = new WindowManager(null);

        // 操作
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "" });

        // 断言 - 空名称不应被索引
        await Assert.That(manager.GetWindowByName("")).IsNull();
    }

    [Test]
    public async Task GetAllWindows_ReturnsAllCreatedWindows()
    {
        // 安排
        var manager = new WindowManager(null);

        // 操作
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W1" });
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W2" });
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W3" });

        // 断言
        var all = manager.GetAllWindows();
        await Assert.That(all).Count().IsEqualTo(3);
    }

    [Test]
    public async Task DestroyWindow_RemovesWindowFromManager()
    {
        // 安排
        var manager = new WindowManager(null);
        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "ToDestroy" });
        var window = manager.GetWindow(id)!;
        SetWindowImpl(window);

        // 操作
        manager.DestroyWindow(id);

        // 断言
        await Assert.That(manager.GetWindow(id)).IsNull();
        await Assert.That(manager.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DestroyWindow_RemovesNameIndex()
    {
        // 安排
        var manager = new WindowManager(null);
        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "NamedWindow" });
        SetWindowImpl(manager.GetWindow(id)!);

        // 操作
        manager.DestroyWindow(id);

        // 断言
        await Assert.That(manager.GetWindowByName("NamedWindow")).IsNull();
    }

    [Test]
    public async Task DestroyWindow_UnknownID_DoesNotThrow()
    {
        // 安排
        var manager = new WindowManager(null);

        // 操作与断言
        await Assert.That(() => manager.DestroyWindow(999u)).ThrowsNothing();
    }

    [Test]
    public async Task DestroyWindow_WindowWithoutImpl_SwallowsException()
    {
        // 安排 - 不设置 Impl，Close() 会抛出 InvalidOperationException
        var manager = new WindowManager(null);
        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "NoImpl" });

        // 操作与断言 - DestroyWindow 应吞下异常而非传播
        await Assert.That(() => manager.DestroyWindow(id)).ThrowsNothing();
        await Assert.That(manager.GetWindow(id)).IsNull();
    }

    [Test]
    public async Task Clear_RemovesAllWindows()
    {
        // 安排
        var manager = new WindowManager(null);
        var id1 = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W1" });
        var id2 = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W2" });
        SetWindowImpl(manager.GetWindow(id1)!);
        SetWindowImpl(manager.GetWindow(id2)!);

        // 操作
        manager.Clear();

        // 断言
        await Assert.That(manager.Count).IsEqualTo(0);
        await Assert.That(manager.GetAllWindows()).IsEmpty();
    }

    [Test]
    public async Task Clear_WithWindowsWithoutImpl_DoesNotThrow()
    {
        // 安排 - 不设置 Impl
        var manager = new WindowManager(null);
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W1" });
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W2" });

        // 操作与断言
        await Assert.That(() => manager.Clear()).ThrowsNothing();
        await Assert.That(manager.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AllWindows_ReturnsReadOnlyList()
    {
        // 安排
        var manager = new WindowManager(null);

        // 操作
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W1" });

        // 断言
        await Assert.That(manager.AllWindows).Count().IsEqualTo(1);
    }

    [Test]
    public async Task OnCreate_RegistersCallback_InvokedOnWindowCreation()
    {
        // 安排
        var manager = new WindowManager(null);
        var createdWindows = new List<WebviewWindow>();
        manager.OnCreate(w => createdWindows.Add(w));

        // 操作
        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "OnCreateWin" });

        // 断言
        await Assert.That(createdWindows.Count).IsEqualTo(1);
        await Assert.That(createdWindows[0].ID).IsEqualTo(id);
        await Assert.That(createdWindows[0].Name).IsEqualTo("OnCreateWin");
    }

    [Test]
    public async Task OnCreate_ReturnsUnsubscribeAction_StopsReceivingCallbacks()
    {
        // 安排
        var manager = new WindowManager(null);
        var callCount = 0;
        var unsubscribe = manager.OnCreate(_ => callCount++);

        // 操作 - 先创建一个窗口（应触发回调）
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W1" });

        // 取消订阅
        unsubscribe();

        // 再创建一个窗口（不应触发回调）
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W2" });

        // 断言
        await Assert.That(callCount).IsEqualTo(1);
    }

    [Test]
    public async Task OnCreate_MultipleCallbacks_AllInvoked()
    {
        // 安排
        var manager = new WindowManager(null);
        var callCount1 = 0;
        var callCount2 = 0;
        manager.OnCreate(_ => callCount1++);
        manager.OnCreate(_ => callCount2++);

        // 操作
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W1" });

        // 断言 - 两个回调都被触发
        await Assert.That(callCount1).IsEqualTo(1);
        await Assert.That(callCount2).IsEqualTo(1);
    }

    [Test]
    public async Task OnCreate_CallbackReceivesCreatedWindow()
    {
        // 安排
        var manager = new WindowManager(null);
        WebviewWindow? receivedWindow = null;
        manager.OnCreate(w => receivedWindow = w);

        // 操作
        var id = manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "ReceivedWin" });

        // 断言 - 回调参数是创建的窗口实例
        await Assert.That(receivedWindow).IsNotNull();
        await Assert.That(receivedWindow!.ID).IsEqualTo(id);
        await Assert.That(receivedWindow.Name).IsEqualTo("ReceivedWin");
    }

    [Test]
    public async Task OnCreate_DoesNotInvokeForExistingWindows()
    {
        // 安排 - 先创建窗口
        var manager = new WindowManager(null);
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "Existing" });

        // 操作 - 注册回调后再创建新窗口
        var callCount = 0;
        manager.OnCreate(_ => callCount++);
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "New" });

        // 断言 - 只对新窗口触发，对已存在窗口不触发
        await Assert.That(callCount).IsEqualTo(1);
    }

    [Test]
    public async Task OnCreate_UnsubscribeOneCallback_OthersStillWork()
    {
        // 安排
        var manager = new WindowManager(null);
        var callCount1 = 0;
        var callCount2 = 0;
        var unsubscribe1 = manager.OnCreate(_ => callCount1++);
        manager.OnCreate(_ => callCount2++);

        // 取消订阅第一个回调
        unsubscribe1();

        // 操作
        manager.CreateWebviewWindow(new WebviewWindowOptions { Name = "W1" });

        // 断言 - 只有第二个回调被触发
        await Assert.That(callCount1).IsEqualTo(0);
        await Assert.That(callCount2).IsEqualTo(1);
    }
}

/// <summary>
/// 用于测试的 IPlatformApp 假实现，记录方法调用。
/// </summary>
internal sealed class FakePlatformApp : IPlatformApp
{
    public string Name => "FakeApp";

    public ConcurrentQueue<(uint id, WebviewWindowOptions options)> CreateWebviewWindowCalls { get; } = new();
    public ConcurrentQueue<(string title, string message, DialogStyle style, string[] buttons)> ShowMessageDialogCalls { get; } = new();
    public ConcurrentQueue<OpenFileDialogOptions> OpenFileDialogCalls { get; } = new();
    public ConcurrentQueue<SaveFileDialogOptions> SaveFileDialogCalls { get; } = new();
    public ConcurrentQueue<OpenFileDialogOptions> OpenMultipleFilesDialogCalls { get; } = new();

    public int ShowMessageDialogResult { get; set; } = 0;
    public string? OpenFileDialogResult { get; set; } = null;
    public string? SaveFileDialogResult { get; set; } = null;
    public string[]? OpenMultipleFilesDialogResult { get; set; } = null;

    public int Run() { return 0; }
    public void Destroy() { }
    public void SetApplicationMenu(Menu? menu) { }
    public uint GetCurrentWindowId() => 0;
    public void ShowAboutDialog(string name, string description, byte[]? icon) { }
    public void SetIcon(byte[]? icon) { }
    public void On(uint id) { }
    public void DispatchOnMainThread(uint id) { }
    public void Hide() { }
    public void Show() { }
    public Screen? GetPrimaryScreen() => null;
    public Screen[] GetScreens() => Array.Empty<Screen>();
    public Dictionary<string, object?> GetFlags(ApplicationOptions options) => new();
    public bool IsOnMainThread() => true;
    public bool IsDarkMode() => false;
    public string GetAccentColor() => "#000000";
    public void DispatchOnMainThread(Action action) => action();

    public void CreateWebviewWindow(uint id, WebviewWindowOptions options)
    {
        CreateWebviewWindowCalls.Enqueue((id, options));
    }

    public Task<int> ShowMessageDialog(string title, string message, DialogStyle style, string[] buttons)
    {
        ShowMessageDialogCalls.Enqueue((title, message, style, buttons));
        return Task.FromResult(ShowMessageDialogResult);
    }

    public Task<string?> OpenFileDialog(OpenFileDialogOptions options)
    {
        OpenFileDialogCalls.Enqueue(options);
        return Task.FromResult(OpenFileDialogResult);
    }

    public Task<string?> SaveFileDialog(SaveFileDialogOptions options)
    {
        SaveFileDialogCalls.Enqueue(options);
        return Task.FromResult(SaveFileDialogResult);
    }

    public Task<string[]?> OpenMultipleFilesDialog(OpenFileDialogOptions options)
    {
        OpenMultipleFilesDialogCalls.Enqueue(options);
        return Task.FromResult(OpenMultipleFilesDialogResult);
    }
}
