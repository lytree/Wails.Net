using Wails.Net.Application.Options;

namespace Wails.Net.Application.MacOS.Tests;

/// <summary>
/// macOS 选项模型测试：验证默认值与属性可写性。
/// </summary>
public class MacOptionsTests
{
    [Test]
    public async Task MacOptions_Defaults_AreSane()
    {
        var options = new MacOptions();

        await Assert.That(options.ActivationPolicy).IsEqualTo(0); // Regular
        await Assert.That(options.ApplicationShouldTerminateAfterLastWindowClosed).IsFalse();
        await Assert.That(options.Backdrop).IsEqualTo(0); // Normal
        await Assert.That(options.CornerType).IsEqualTo(0);
        await Assert.That(options.WebviewPreferences).IsNull();
    }

    [Test]
    public async Task MacOptions_TitleBar_Defaults()
    {
        var titleBar = new MacTitleBarOptions();

        await Assert.That(titleBar.AppearsTransparent).IsFalse();
        await Assert.That(titleBar.Hide).IsFalse();
        await Assert.That(titleBar.HideTitle).IsFalse();
        await Assert.That(titleBar.FullSizeContent).IsFalse();
        await Assert.That(titleBar.UseToolbar).IsFalse();
    }

    [Test]
    public async Task MacOptions_WebviewPreferences_NullableProperties()
    {
        var prefs = new MacWebviewPreferences();

        await Assert.That(prefs.TabFocusesLinks).IsNull();
        await Assert.That(prefs.AllowsMagnification).IsNull();
        await Assert.That(prefs.MinimumFontSize).IsNull();
        await Assert.That(prefs.EnableAutoplayWithoutUserAction).IsNull();
        await Assert.That(prefs.ApplicationNameForUserAgent).IsNull();

        prefs.TabFocusesLinks = true;
        prefs.EnableAutoplayWithoutUserAction = false;
        prefs.ApplicationNameForUserAgent = "MyApp/1.0";

        await Assert.That(prefs.TabFocusesLinks).IsTrue();
        await Assert.That(prefs.EnableAutoplayWithoutUserAction).IsFalse();
        await Assert.That(prefs.ApplicationNameForUserAgent).IsEqualTo("MyApp/1.0");
    }

    [Test]
    public async Task ApplicationOptions_Mac_IsNullByDefault()
    {
        var options = new ApplicationOptions();
        await Assert.That(options.Mac).IsNull();

        options.Mac = new MacOptions { ActivationPolicy = 1 };
        await Assert.That(options.Mac.ActivationPolicy).IsEqualTo(1);
    }

    [Test]
    public async Task WebviewWindowOptions_Mac_IsNullByDefault()
    {
        var options = new WebviewWindowOptions();
        await Assert.That(options.Mac).IsNull();

        options.Mac = new WebviewWindowMacOptions
        {
            DisableShadow = true,
            Appearance = "NSAppearanceNameDarkAqua",
            InvisibleTitleBarHeight = 28,
        };

        await Assert.That(options.Mac.DisableShadow).IsTrue();
        await Assert.That(options.Mac.Appearance).IsEqualTo("NSAppearanceNameDarkAqua");
        await Assert.That(options.Mac.InvisibleTitleBarHeight).IsEqualTo(28u);
    }

    [Test]
    public async Task WebviewWindowMacOptions_Defaults()
    {
        var options = new WebviewWindowMacOptions();

        await Assert.That(options.CornerType).IsEqualTo(0);
        await Assert.That(options.Backdrop).IsEqualTo(0);
        await Assert.That(options.WindowLevel).IsEqualTo(0);
        await Assert.That(options.DisableEscapeExitsFullscreen).IsFalse();
        await Assert.That(options.TitleBar).IsNull();
    }
}
