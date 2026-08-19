using Wails.Net.Application.Browser;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.MacOS.Tests;

/// <summary>
/// macOS 平台骨架（net10.0 目标，<c>#else</c> 分支）行为测试。
/// <para>
/// 验证在非 macOS 主机（例如 Windows / Linux 开发机）上编译 net10.0 目标时，
/// 平台实现降级为 no-op / 默认值路径且不抛异常，
/// 以保证跨平台源码兼容性与 ServerMode 回退可用性。
/// </para>
/// <para>
/// <b>双 TFM 策略背景</b>（与 <c>src/Wails.Net.Application.MacOS/Wails.Net.Application.MacOS.csproj</c> 一致）：
/// <list type="bullet">
/// <item><term>net10.0</term><description>任意宿主可编译的骨架目标。AppKit/WebKit 代码由 <c>#if MACOS</c> 保护，此处仅编译 <c>#else</c> 分支的 ServerMode 降级实现（构造默认 / 方法 no-op / 集合空 / 标志位 false 等）。</description></item>
/// <item><term>net10.0-macos</term><description>完整 AppKit + WebKit（WKWebView）实现，仅在 macOS 主机自动启用；可通过 <c>-p:WailsNetEnableMacOS=true</c> 强制启用（需 macOS 工作负载与 SDK）。该目标下的真实 GUI 行为不在本测试套件覆盖范围内，须在 macOS 主机上端到端验证（参考 AGENTS.md §4.4）。</description></item>
/// </list>
/// </para>
/// <para>
/// <b>为什么不参与 GuiContract 契约测试：</b>
/// <list type="bullet">
/// <item>本项目无 macOS 测试主机（CI / 开发者均为 Windows / Linux），<c>net10.0-macos</c> TFM 不可在本地编译；</item>
/// <item>net10.0 目标下 <see cref="MacOSWebviewWindow"/> 的实现是 <c>#else</c> 分支的 ServerMode 占位，与 <see cref="Wails.Net.Application.Platform.ServerWebviewWindow"/> 行为等价，契约验证与 <see cref="Wails.Net.Application.Tests.ServerPlatformAppTests"/> 重叠；</item>
/// <item>因此 macOS 在本套件中仅以"骨架行为不抛异常"的形式覆盖，本类 + <see cref="MacOptionsTests"/> 共同构成 macOS 的测试入口。完整的 AppKit 分支测试需在 macOS 上以独立测试项目或 E2E 形式补充（见实施计划阶段 5 / 后续 macOS 工作项）。</description></item>
/// </list>
/// </para>
/// <para>
/// <b>测试组织：</b>每个 <c>[Test]</c> 方法命名遵循 <c>Type_Behavior_ExpectedResult</c> 模式
/// （如 <c>PlatformApp_Skeleton_Constructs</c>、<c>Clipboard_Skeleton_ReturnsEmpty</c>），
/// 测试方法本身不要求 GUI 运行环境，因此不需要 <c>[NotInParallel]</c>，
/// 可与 <see cref="MacOptionsTests"/> 并行执行。
/// </para>
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
