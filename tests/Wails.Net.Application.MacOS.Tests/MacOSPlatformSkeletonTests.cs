using Wails.Net.Application.Browser;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.MacOS.Tests;

/// <summary>
/// macOS 平台骨架（net10.0 目标，#else 分支）行为测试。
/// 验证非 macOS 目标下平台实现降级为 no-op / 默认值且不抛异常。
/// </summary>
public class MacOSPlatformSkeletonTests
{
    [Test]
    public async Task PlatformApp_Skeleton_Constructs()
    {
        var app = new MacOSPlatformApp(new Options.ApplicationOptions { Name = "Test" });

        await Assert.That(app.Name).IsEqualTo("Test");
    }

    [Test]
    public async Task PlatformApp_Skeleton_ScreensEmpty()
    {
        var app = new MacOSPlatformApp(new Options.ApplicationOptions { Name = "Test" });

        var screens = app.GetScreens();
        await Assert.That(screens).IsNotNull();
        await Assert.That(screens.Length).IsEqualTo(0);
        await Assert.That(app.GetPrimaryScreen()).IsNull();
    }

    [Test]
    public async Task PlatformApp_Skeleton_Defaults()
    {
        var app = new MacOSPlatformApp(new Options.ApplicationOptions { Name = "Test" });

        await Assert.That(app.IsOnMainThread()).IsTrue();
        await Assert.That(app.IsDarkMode()).IsFalse();
        await Assert.That(app.GetCurrentWindowId()).IsEqualTo(0u);
        await Assert.That(app.Capabilities.HasNativeDrag).IsFalse();
    }

    [Test]
    public async Task PlatformApp_Skeleton_Dialogs_ReturnDefaults()
    {
        var app = new MacOSPlatformApp(new Options.ApplicationOptions { Name = "Test" });

        var message = await app.ShowMessageDialog("t", "m", Dialogs.DialogStyle.Info, new[] { "OK" });
        await Assert.That(message).IsEqualTo(0);

        var opened = await app.OpenFileDialog(new Dialogs.OpenFileDialogOptions());
        await Assert.That(opened).IsNull();

        var saved = await app.SaveFileDialog(new Dialogs.SaveFileDialogOptions());
        await Assert.That(saved).IsNull();

        var multi = await app.OpenMultipleFilesDialog(new Dialogs.OpenFileDialogOptions());
        await Assert.That(multi).IsNull();
    }

    [Test]
    public async Task PlatformApp_Skeleton_NoOpMethods()
    {
        var app = new MacOSPlatformApp(new Options.ApplicationOptions { Name = "Test" });

        // 不应抛异常。
        app.Hide();
        app.Show();
        app.Destroy();
        app.SetParent(IntPtr.Zero);
        app.SetApplicationMenu(null);
        app.On(1);
        app.DispatchOnMainThread(() => { });
    }

    [Test]
    public async Task Clipboard_Skeleton_ReturnsEmpty()
    {
        var clipboard = new MacOSClipboard();

        clipboard.SetText("hello");
        await Assert.That(clipboard.GetText()).IsEqualTo(string.Empty);
        await Assert.That(clipboard.GetHTML()).IsEqualTo(string.Empty);
        await Assert.That(clipboard.GetImage()).IsNull();
        await Assert.That(clipboard.GetFiles()).IsEmpty();
        clipboard.Clear(); // 不应抛异常
    }

    [Test]
    public async Task EnvironmentManager_ReportsMacos()
    {
        var env = new MacOSEnvironmentManager("MyApp");

        await Assert.That(env.GetOS()).IsEqualTo("macos");
        await Assert.That(env.GetArch()).IsNotEmpty();
        await Assert.That(env.GetHomeDir()).IsNotEmpty();
        await Assert.That(env.GetDataDir()).IsNotEmpty();
        await Assert.That(env.IsDarkMode()).IsFalse();
        await Assert.That(env.HasFocusFollowsMouse()).IsFalse();
        await Assert.That(env.Info().OS).IsEqualTo("macos");
    }

    [Test]
    public async Task AutostartManager_Skeleton_Safe()
    {
        var manager = new MacOSAutostartManager("MyApp");

        await Assert.That(manager.IsEnabled()).IsFalse();
        manager.Enable(); // 不应抛异常（非 macOS 目标 plist 写入被系统忽略或失败静默）
        manager.Disable();
    }

    [Test]
    public async Task Menu_Skeleton_NoOp()
    {
        var menu = new Menus.Menu("Test");
        var impl = new MacOSMenu(menu);

        impl.Show();
        impl.Hide();
        impl.AddMenuItem(new Menus.MenuItem("item"), 0);
        impl.SetLabel("New");
        impl.SetEnabled(true);
        impl.SetChecked(true);
        impl.SetAccelerator("CmdOrCtrl+K");
        impl.SetBitmap(null);
        impl.Destroy();
    }

    [Test]
    public async Task SystemTray_Skeleton_NoOp()
    {
        var tray = new MacOSSystemTray(1);

        tray.SetIcon(new byte[] { 1, 2, 3 });
        tray.SetLabel("label");
        tray.SetMenu(null);
        tray.SetTooltip("tip");
        tray.SetDarkModeIcon(new byte[] { 1 });
        tray.SetTemplateIcon(new byte[] { 1 });
        tray.Show();
        tray.Hide();
        tray.Destroy();
    }

    [Test]
    public async Task KeyBindingManager_Skeleton_HandleHotKeySafe()
    {
        var manager = new MacOSKeyBindingManager();

        manager.RegisterKeyBinding("CmdOrCtrl+Shift+K", () => { });
        manager.HandleHotKey(1); // 不应抛异常
        manager.UnregisterKeyBinding("CmdOrCtrl+Shift+K");
    }

    [Test]
    public async Task BrowserManager_Skeleton_NoOp()
    {
        var browser = new MacOSBrowserManager();

        browser.OpenURL("https://example.com");
        browser.OpenURLInDefaultBrowser("https://example.com");
    }

    [Test]
    public async Task Keychain_Skeleton_ReturnsFalse()
    {
        var keychain = new MacOSKeychain();

        await Assert.That(keychain.SetPassword("svc", "acct", "pwd")).IsFalse();
        await Assert.That(keychain.GetPassword("svc", "acct")).IsNull();
        await Assert.That(keychain.DeletePassword("svc", "acct")).IsFalse();
    }
}
