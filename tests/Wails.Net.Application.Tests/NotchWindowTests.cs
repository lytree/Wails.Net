using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Tests;

/// <summary>
/// notch 窗口选项映射与降级行为测试。
/// 对应 Wails v3 Go 版本 <c>notch_window.go</c> 的
/// <c>normaliseNotchWindowOptions</c> / <c>webviewOptionsForNotchWindow</c>。
/// </summary>
[NotInParallel]
public sealed class NotchWindowTests
{
    [Test]
    public async Task BuildNotchWindowOptions_Defaults_AddsNativeInsets()
    {
        // 操作
        var options = Application.BuildNotchWindowOptions(new NotchWindowOptions());

        // 断言：默认 660x92 内容区 + 左右各 30pt、底部 12pt 原生边距
        await Assert.That(options.Width).IsEqualTo(660 + 60);
        await Assert.That(options.Height).IsEqualTo(92 + 12);
        await Assert.That(options.Frameless).IsTrue();
        await Assert.That(options.Resizable).IsFalse();
        await Assert.That(options.BackgroundType).IsEqualTo("transparent");
        await Assert.That(options.A).IsEqualTo((byte)0);
        await Assert.That(options.Mac).IsNotNull();
        await Assert.That(options.Mac!.Notch).IsNotNull();
        await Assert.That(options.Mac.Notch!.ContentWidth).IsEqualTo(660);
        await Assert.That(options.Mac.Notch.ContentHeight).IsEqualTo(92);
        await Assert.That(options.Mac.Notch.AnimationSpeedMilliseconds).IsEqualTo(420);
    }

    [Test]
    public async Task BuildNotchWindowOptions_CustomSizeAndScreen_MapsNotchInfo()
    {
        // 安排
        var notchOptions = new NotchWindowOptions
        {
            Width = 400,
            Height = 60,
            Animated = true,
            AnimationSpeed = TimeSpan.FromMilliseconds(250),
            Screen = new Screen { Id = "77" },
        };

        // 操作
        var options = Application.BuildNotchWindowOptions(notchOptions);

        // 断言
        await Assert.That(options.Width).IsEqualTo(460);
        await Assert.That(options.Height).IsEqualTo(72);
        await Assert.That(options.Mac!.Notch!.Animated).IsTrue();
        await Assert.That(options.Mac.Notch.AnimationSpeedMilliseconds).IsEqualTo(250);
        await Assert.That(options.Mac.Notch.ScreenId).IsEqualTo("77");
        await Assert.That(options.Mac.WindowClass).IsEqualTo(1);
        await Assert.That(options.Mac.PanelPreferences!.NonActivating).IsTrue();
        await Assert.That(options.Mac.PanelPreferences.FloatingPanel).IsTrue();
        await Assert.That(options.Mac.WindowLevel).IsEqualTo(6);
        await Assert.That(options.Mac.CollectionBehavior).IsEqualTo(1 | 256 | 16 | 64);
    }

    [Test]
    public async Task BuildNotchWindowOptions_PreservesUserWebContent()
    {
        // 安排
        var webOptions = new WebviewWindowOptions
        {
            Name = "notch",
            URL = "wails://localhost/notch",
        };

        // 操作
        var options = Application.BuildNotchWindowOptions(new NotchWindowOptions
        {
            WindowOptions = webOptions,
        });

        // 断言：内容与名称保留，几何/外观被接管
        await Assert.That(options).IsSameReferenceAs(webOptions);
        await Assert.That(options.Name).IsEqualTo("notch");
        await Assert.That(options.URL).IsEqualTo("wails://localhost/notch");
    }

    [Test]
    public async Task CreateNotchWindow_NullOptions_Throws()
    {
        var app = new Application(new ApplicationOptions());

        await Assert.That(() => app.CreateNotchWindow(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task CreateNotchWindow_NonMacOS_ReturnsEmptyWrapper()
    {
        // 非 macOS 平台降级（对应 Go 侧 notchWindowsSupported=false）。
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        var app = new Application(new ApplicationOptions());
        var notch = app.CreateNotchWindow(new NotchWindowOptions());

        await Assert.That(notch.Window).IsNull();
        await Assert.That(notch.Visibility).IsFalse();

        // 空包装上的生命周期调用应为 no-op，不抛异常。
        notch.Show();
        notch.Hide();
        notch.Close();
    }
}
