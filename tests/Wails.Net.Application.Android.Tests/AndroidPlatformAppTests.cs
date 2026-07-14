using TUnit.Core;
using Wails.Net.Application.Dialogs;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Android.Tests;

/// <summary>
/// AndroidPlatformApp 的单元测试（TUnit）。
/// 测试应用名称、线程身份判断、AssetServer 引用等。
/// 注意：在非 Android 环境下（如 Windows CI）Looper.MainLooper 返回 null，
/// 部分依赖 Android 主线程的方法会降级到同步执行。
/// </summary>
[NotInParallel]
public sealed class AndroidPlatformAppTests
{
    [Test]
    public async Task Constructor_SetsName()
    {
        // 安排与操作
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 断言
        await Assert.That(app.Name).IsEqualTo("TestApp");
    }

    [Test]
    public async Task AcquireSingleInstanceLock_AlwaysReturnsTrue()
    {
        // 安排：Android 无单实例概念
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言
        await Assert.That(app.AcquireSingleInstanceLock("any-id")).IsTrue();
    }

    [Test]
    public async Task GetFlags_ReturnsEmptyDictionary()
    {
        // 安排
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var flags = app.GetFlags(new ApplicationOptions());

        // 断言：Android 平台无特殊标志位
        await Assert.That(flags).IsNotNull();
        await Assert.That(flags.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetAccentColor_ReturnsEmptyString()
    {
        // 安排：Android 无统一强调色概念
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var color = app.GetAccentColor();

        // 断言：返回空字符串（无统一强调色）
        await Assert.That(color).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetCurrentWindowId_ReturnsZero_WhenNoWindows()
    {
        // 安排：未创建任何窗口
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：无窗口时返回 0
        await Assert.That(app.GetCurrentWindowId()).IsEqualTo(0u);
    }

    [Test]
    public async Task SetAssetServer_DoesNotThrow_WhenNullProvided()
    {
        // 安排
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：传入 null 不应抛异常
        app.SetAssetServer(null);
    }

    [Test]
    public async Task Destroy_TriggersRunExit()
    {
        // 安排：在后台线程启动 Run()，Destroy() 后应唤醒 Run 返回
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作：在后台线程运行 Run，主线程触发 Destroy
        var runTask = Task.Run(() => app.Run());

        // 短暂等待确保 Run 已进入 Wait 状态
        await Task.Delay(50);

        app.Destroy();

        // 断言：Run 应在合理时间内返回 0
        var exitCode = await runTask;
        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task DispatchOnMainThread_ExecutesAction_WhenNoLooper()
    {
        // 安排：非 Android 环境下 _mainLooper 为 null，直接同步执行
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });
        var executed = false;

        // 操作
        app.DispatchOnMainThread(() => executed = true);

        // 断言：应同步执行
        await Assert.That(executed).IsTrue();
    }

    [Test]
    public async Task IsOnMainThread_ReturnsTrue_InNonAndroidEnvironment()
    {
        // 安排：非 Android 环境下 _mainLooper 为 null，回退到线程 ID 比较。
        // 测试在主线程执行，构造时记录的 _mainThreadId 与当前线程一致。
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：同一线程应判定为主线程
        await Assert.That(app.IsOnMainThread()).IsTrue();
    }

    [Test]
    public async Task IsDarkMode_ReturnsFalse_InNonAndroidEnvironment()
    {
        // 安排：非 Android 环境下 Application.Context 为 null
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：Context 为 null 时返回 false
        await Assert.That(app.IsDarkMode()).IsFalse();
    }

    [Test]
    public async Task GetPrimaryScreen_ReturnsNull_InNonAndroidEnvironment()
    {
        // 安排：非 Android 环境下 Application.Context 为 null
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：Context 为 null 时无法获取 DisplayMetrics，返回 null
        await Assert.That(app.GetPrimaryScreen()).IsNull();
    }

    [Test]
    public async Task GetScreens_ReturnsEmptyArray_InNonAndroidEnvironment()
    {
        // 安排：非 Android 环境下 GetPrimaryScreen 返回 null
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var screens = app.GetScreens();

        // 断言：主屏幕为 null 时返回空数组
        await Assert.That(screens).IsNotNull();
        await Assert.That(screens.Length).IsEqualTo(0);
    }

    [Test]
    public async Task SetApplicationMenu_DoesNotThrow_WhenNullMenuProvided()
    {
        // 安排：Android 无应用菜单概念，为 no-op
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：传入 null 不应抛异常
        await Assert.That(() => app.SetApplicationMenu(null)).ThrowsNothing();
    }

    [Test]
    public async Task SetIcon_DoesNotThrow_WhenNullIconProvided()
    {
        // 安排：Android 应用图标由 AndroidManifest 配置，运行时 no-op
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：传入 null 不应抛异常
        await Assert.That(() => app.SetIcon(null)).ThrowsNothing();
    }

    [Test]
    public async Task Hide_DoesNotThrow_InNonAndroidEnvironment()
    {
        // 安排：Android 应用可见性由系统管理，no-op
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言
        await Assert.That(() => app.Hide()).ThrowsNothing();
    }

    [Test]
    public async Task Show_DoesNotThrow_InNonAndroidEnvironment()
    {
        // 安排：Android 应用可见性由系统管理，no-op
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言
        await Assert.That(() => app.Show()).ThrowsNothing();
    }

    [Test]
    public async Task On_DoesNotThrow_WhenEventIdProvided()
    {
        // 安排：非 Android 环境下 _mainLooper 为 null，降级为同步处理（无 Application 时 no-op）
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：传入事件 ID 不应抛异常（无全局 Application 时 HandlePlatformEvent 为 no-op）
        await Assert.That(() => app.On(100u)).ThrowsNothing();
    }

    [Test]
    public async Task DispatchOnMainThread_WithEventId_DoesNotThrow()
    {
        // 安排：非 Android 环境下 _mainLooper 为 null，降级为同步处理
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：传入事件 ID 不应抛异常（无全局 Application 时 HandlePlatformEvent 为 no-op）
        await Assert.That(() => app.DispatchOnMainThread(200u)).ThrowsNothing();
    }

    [Test]
    public async Task ShowAboutDialog_DoesNotThrow_InNonAndroidEnvironment()
    {
        // 安排：非 Android 环境下 Application.Context 为 null，AlertDialog 不会创建
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作与断言：Context 为 null 时 DispatchOnMainThread 回调内提前返回，不抛异常
        await Assert.That(() => app.ShowAboutDialog("TestApp", "Description", null)).ThrowsNothing();
    }

    [Test]
    public async Task ShowMessageDialog_ReturnsZero_WhenEmptyButtonsProvided()
    {
        // 安排：传入空按钮数组
        var app = new AndroidPlatformApp(new ApplicationOptions { Name = "TestApp" });

        // 操作
        var result = await app.ShowMessageDialog("Title", "Message", Dialogs.DialogStyle.Info, System.Array.Empty<string>());

        // 断言：无按钮时直接返回 0
        await Assert.That(result).IsEqualTo(0);
    }
}
