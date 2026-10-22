using TUnit.Core;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// <see cref="LinuxX11GlobalHotkeys"/> 的纯逻辑测试：accelerator → X11 keysym/修饰掩码映射。
/// 不触达原生 Xlib 调用，可在任意平台执行。
/// </summary>
[NotInParallel]
public sealed class LinuxX11GlobalHotkeysTests
{
    [Test]
    public async Task TryParseAccelerator_CtrlShiftK_MapsToKeySymAndMasks()
    {
        var ok = LinuxX11GlobalHotkeys.TryParseAccelerator(
            "Ctrl+Shift+K", out var keysym, out var modifiers);

        await Assert.That(ok).IsTrue();
        await Assert.That(keysym).IsEqualTo("k");
        await Assert.That(modifiers).IsEqualTo(LinuxX11GlobalHotkeys.ControlMask | LinuxX11GlobalHotkeys.ShiftMask);
    }

    [Test]
    public async Task TryParseAccelerator_CmdOrCtrlAlt_OnLinux_UsesControlAndMod1()
    {
        var ok = LinuxX11GlobalHotkeys.TryParseAccelerator(
            "CmdOrCtrl+Alt+Space", out var keysym, out var modifiers);

        await Assert.That(ok).IsTrue();
        await Assert.That(keysym).IsEqualTo("space");
        await Assert.That(modifiers).IsEqualTo(LinuxX11GlobalHotkeys.ControlMask | LinuxX11GlobalHotkeys.Mod1Mask);
    }

    [Test]
    public async Task TryParseAccelerator_SuperKey_UsesMod4()
    {
        var ok = LinuxX11GlobalHotkeys.TryParseAccelerator(
            "Super+F5", out var keysym, out var modifiers);

        await Assert.That(ok).IsTrue();
        await Assert.That(keysym).IsEqualTo("F5");
        await Assert.That(modifiers).IsEqualTo(LinuxX11GlobalHotkeys.Mod4Mask);
    }

    [Test]
    public async Task TryParseAccelerator_OnlyModifiers_ReturnsFalse()
    {
        var ok = LinuxX11GlobalHotkeys.TryParseAccelerator("Ctrl+Shift", out _, out _);

        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task TryParseAccelerator_Empty_ReturnsFalse()
    {
        var ok = LinuxX11GlobalHotkeys.TryParseAccelerator("   ", out _, out _);

        await Assert.That(ok).IsFalse();
    }

    [Test]
    public async Task TranslateKey_NamedKeys_MapToX11Names()
    {
        await Assert.That(LinuxX11GlobalHotkeys.TranslateKey("Enter")).IsEqualTo("Return");
        await Assert.That(LinuxX11GlobalHotkeys.TranslateKey("Backspace")).IsEqualTo("BackSpace");
        await Assert.That(LinuxX11GlobalHotkeys.TranslateKey("PageUp")).IsEqualTo("Prior");
        await Assert.That(LinuxX11GlobalHotkeys.TranslateKey("pageDown")).IsEqualTo("Next");
        await Assert.That(LinuxX11GlobalHotkeys.TranslateKey("ArrowLeft")).IsEqualTo("Left");
        await Assert.That(LinuxX11GlobalHotkeys.TranslateKey("A")).IsEqualTo("a");
        await Assert.That(LinuxX11GlobalHotkeys.TranslateKey("f12")).IsEqualTo("F12");
    }

    [Test]
    public async Task TranslateKey_OutOfRangeFunctionKey_FallsBackToLowercase()
    {
        await Assert.That(LinuxX11GlobalHotkeys.TranslateKey("F25")).IsEqualTo("f25");
    }
}
