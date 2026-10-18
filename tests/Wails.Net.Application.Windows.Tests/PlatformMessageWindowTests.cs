using TUnit.Core;
using Wails.Net.Application.GuiContract.Tests;
using Wails.Net.Application.Options;
using Wails.Net.Events;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// 平台层顶层消息窗口（<see cref="PlatformMessageWindow"/>）的测试。
/// <para>
/// 覆盖 P1-10 补全：应用级消息窗口在收到 <c>WM_SETTINGCHANGE</c>（<c>ImmersiveColorSet</c>）
/// 时发出 <see cref="KnownEvents.ThemeChanged"/>，使无 webview 窗口时托盘也能随系统主题切换。
/// 对应 Wails v3 <c>application_windows.go</c> 中 <c>mainThreadWindow</c> 的处理。
/// </para>
/// <para>
/// 纯逻辑用例默认执行；创建真实原生窗口的用例标记
/// <see cref="RequiresGuiEnvironmentAttribute"/>（默认跳过，设置
/// <c>WAILS_RUN_GUI_TESTS=1</c> 后启用）。
/// </para>
/// </summary>
[NotInParallel]
public sealed class PlatformMessageWindowTests
{
    /// <summary>ImmersiveColorSet 类别应发出 ThemeChanged。</summary>
    [Test]
    public async Task SettingChange_ImmersiveColorSet_EmitsThemeChanged()
    {
        var app = new Application(new ApplicationOptions());
        var received = 0;
        app.Events.On(KnownEvents.ThemeChanged, _ => Interlocked.Increment(ref received));

        PlatformMessageWindow.HandleSettingChangeForTest("ImmersiveColorSet");

        await Assert.That(received).IsEqualTo(1);
    }

    /// <summary>非主题类别不应发出 ThemeChanged。</summary>
    [Test]
    public async Task SettingChange_OtherCategory_DoesNotEmitThemeChanged()
    {
        var app = new Application(new ApplicationOptions());
        var received = 0;
        app.Events.On(KnownEvents.ThemeChanged, _ => Interlocked.Increment(ref received));

        PlatformMessageWindow.HandleSettingChangeForTest("WindowMetrics");

        await Assert.That(received).IsEqualTo(0);
    }

    /// <summary>Create 应产生非空顶层窗口句柄。</summary>
    [Test]
    [RequiresGuiEnvironment]
    public async Task Create_ProducesTopLevelMessageWindow()
    {
        using var window = new PlatformMessageWindow();

        window.Create();

        await Assert.That(window.IsCreated).IsTrue();
        await Assert.That(window.Hwnd.IsNull).IsFalse();
    }

    /// <summary>Dispose 应销毁窗口且可安全重复调用。</summary>
    [Test]
    [RequiresGuiEnvironment]
    public async Task Dispose_DestroysWindow_AndIsIdempotent()
    {
        var window = new PlatformMessageWindow();
        window.Create();
        await Assert.That(window.IsCreated).IsTrue();

        window.Dispose();
        window.Dispose();

        await Assert.That(window.IsCreated).IsFalse();
    }
}
