using TUnit.Core;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.MacOS.Tests;

/// <summary>
/// <see cref="MacOSKeyBindingManager"/> 的 accelerator 解析测试。
/// <para>
/// <c>TryParseAccelerator</c> 是纯函数（不触碰 Carbon 原生 API），负责把 Wails 风格
/// accelerator（<c>CmdOrCtrl+Shift+K</c>）翻译为 Carbon 硬件键码（kVK_*）与修饰掩码。
/// 该表是 macOS 全局快捷键的唯一映射源，写错一位就会导致快捷键静默失效，
/// 因此在任意宿主上都应被单元测试守住。
/// </para>
/// <para>
/// 修饰掩码常量取自 Carbon <c>Events.h</c>：Cmd=0x0100、Shift=0x0200、Option=0x0800、Control=0x1000。
/// </para>
/// </summary>
public sealed class MacOSKeyBindingManagerTests
{
    /// <summary>Carbon Command 键掩码（cmdKey）。</summary>
    private const uint CarbonCmd = 0x0100;

    /// <summary>Carbon Shift 键掩码（shiftKey）。</summary>
    private const uint CarbonShift = 0x0200;

    /// <summary>Carbon Option（Alt）键掩码（optionKey）。</summary>
    private const uint CarbonOption = 0x0800;

    /// <summary>Carbon Control 键掩码（controlKey）。</summary>
    private const uint CarbonControl = 0x1000;

    /// <summary>
    /// 典型组合键：CmdOrCtrl+Shift+K 应解析为 kVK_ANSI_K（40）并同时置位 Cmd 与 Shift。
    /// </summary>
    [Test]
    public async Task TryParseAccelerator_CmdOrCtrlShiftK_ResolvesKeyCodeAndModifiers()
    {
        var parsed = MacOSKeyBindingManager.TryParseAccelerator(
            "CmdOrCtrl+Shift+K", out var keyCode, out var modifiers);

        await Assert.That(parsed).IsTrue();
        await Assert.That(keyCode).IsEqualTo(40);
        await Assert.That(modifiers).IsEqualTo(CarbonCmd | CarbonShift);
    }

    /// <summary>
    /// 仅修饰键时应解析失败并返回 null 键码（不能把修饰键本身当作触发键）。
    /// </summary>
    [Test]
    [Arguments("CmdOrCtrl")]
    [Arguments("Shift")]
    [Arguments("Alt+Shift")]
    public async Task TryParseAccelerator_ModifiersOnly_FailsAndReturnsNullKeyCode(string accelerator)
    {
        var parsed = MacOSKeyBindingManager.TryParseAccelerator(accelerator, out var keyCode, out var modifiers);

        await Assert.That(parsed).IsFalse();
        await Assert.That(keyCode).IsNull();
        await Assert.That(modifiers).IsNotEqualTo(0u);
    }

    /// <summary>
    /// 未收录的键名（拼写错误 / 非 QWERTY 符号）应解析失败，由调用方静默忽略。
    /// </summary>
    [Test]
    [Arguments("CmdOrCtrl+Foo")]
    [Arguments("CmdOrCtrl+")]
    [Arguments("")]
    [Arguments("+++")]
    public async Task TryParseAccelerator_UnsupportedInput_Fails(string accelerator)
    {
        var parsed = MacOSKeyBindingManager.TryParseAccelerator(accelerator, out var keyCode, out _);

        await Assert.That(parsed).IsFalse();
        await Assert.That(keyCode).IsNull();
    }

    /// <summary>
    /// 各修饰键别名均应映射到对应 Carbon 掩码（大小写不敏感）。
    /// </summary>
    [Test]
    [Arguments("CMD", CarbonCmd)]
    [Arguments("COMMAND", CarbonCmd)]
    [Arguments("CMDORCTRL", CarbonCmd)]
    [Arguments("SUPER", CarbonCmd)]
    [Arguments("META", CarbonCmd)]
    [Arguments("WIN", CarbonCmd)]
    [Arguments("CTRL", CarbonControl)]
    [Arguments("CONTROL", CarbonControl)]
    [Arguments("ALT", CarbonOption)]
    [Arguments("OPTION", CarbonOption)]
    [Arguments("OPT", CarbonOption)]
    [Arguments("SHIFT", CarbonShift)]
    public async Task TryParseAccelerator_ModifierAliases_MapToCarbonMask(string modifier, uint expectedMask)
    {
        var parsed = MacOSKeyBindingManager.TryParseAccelerator($"{modifier}+K", out _, out var modifiers);

        await Assert.That(parsed).IsTrue();
        await Assert.That(modifiers).IsEqualTo(expectedMask);
    }

    /// <summary>
    /// 多个修饰键应按位或累加，且与书写顺序无关。
    /// </summary>
    [Test]
    public async Task TryParseAccelerator_MultipleModifiers_AreBitwiseCombined()
    {
        MacOSKeyBindingManager.TryParseAccelerator("Ctrl+Alt+Shift+K", out _, out var inOrder);
        MacOSKeyBindingManager.TryParseAccelerator("Shift+Ctrl+Alt+K", out _, out var reordered);

        await Assert.That(inOrder).IsEqualTo(CarbonControl | CarbonOption | CarbonShift);
        await Assert.That(reordered).IsEqualTo(inOrder);
    }

    /// <summary>
    /// accelerator 应大小写不敏感，且容忍加号两侧空白。
    /// </summary>
    [Test]
    [Arguments("cmdorctrl+shift+k")]
    [Arguments("CmdOrCtrl + Shift + K")]
    public async Task TryParseAccelerator_IsCaseInsensitiveAndTrimsWhitespace(string accelerator)
    {
        var parsed = MacOSKeyBindingManager.TryParseAccelerator(accelerator, out var keyCode, out var modifiers);

        await Assert.That(parsed).IsTrue();
        await Assert.That(keyCode).IsEqualTo(40);
        await Assert.That(modifiers).IsEqualTo(CarbonCmd | CarbonShift);
    }

    /// <summary>
    /// 字母键应映射到 QWERTY 物理键位（kVK_ANSI_*），而非按字母顺序排列。
    /// </summary>
    [Test]
    [Arguments("a", 0)]
    [Arguments("s", 1)]
    [Arguments("d", 2)]
    [Arguments("f", 3)]
    [Arguments("h", 4)]
    [Arguments("g", 5)]
    [Arguments("z", 6)]
    [Arguments("x", 7)]
    [Arguments("c", 8)]
    [Arguments("v", 9)]
    [Arguments("b", 11)]
    [Arguments("q", 12)]
    [Arguments("w", 13)]
    [Arguments("e", 14)]
    [Arguments("r", 15)]
    [Arguments("y", 16)]
    [Arguments("t", 17)]
    [Arguments("o", 31)]
    [Arguments("u", 32)]
    [Arguments("i", 34)]
    [Arguments("p", 35)]
    [Arguments("l", 37)]
    [Arguments("j", 38)]
    [Arguments("k", 40)]
    [Arguments("n", 45)]
    [Arguments("m", 46)]
    public async Task TryParseAccelerator_LetterKeys_MapToPhysicalKeyCodes(string key, int expected)
    {
        var parsed = MacOSKeyBindingManager.TryParseAccelerator($"Cmd+{key}", out var keyCode, out _);

        await Assert.That(parsed).IsTrue();
        await Assert.That(keyCode).IsEqualTo(expected);
    }

    /// <summary>
    /// 数字行键应映射到 kVK_ANSI_0..9 的物理键位。
    /// </summary>
    [Test]
    [Arguments("1", 18)]
    [Arguments("2", 19)]
    [Arguments("3", 20)]
    [Arguments("4", 21)]
    [Arguments("5", 23)]
    [Arguments("6", 22)]
    [Arguments("7", 26)]
    [Arguments("8", 28)]
    [Arguments("9", 25)]
    [Arguments("0", 29)]
    public async Task TryParseAccelerator_DigitKeys_MapToPhysicalKeyCodes(string key, int expected)
    {
        var parsed = MacOSKeyBindingManager.TryParseAccelerator($"Cmd+{key}", out var keyCode, out _);

        await Assert.That(parsed).IsTrue();
        await Assert.That(keyCode).IsEqualTo(expected);
    }

    /// <summary>
    /// 命名键（回车 / Tab / Escape / 方向键 / 翻页键等）应映射到对应的虚拟键码。
    /// </summary>
    [Test]
    [Arguments("return", 36)]
    [Arguments("enter", 36)]
    [Arguments("tab", 48)]
    [Arguments("space", 49)]
    [Arguments("backspace", 51)]
    [Arguments("delete", 117)]
    [Arguments("escape", 53)]
    [Arguments("esc", 53)]
    [Arguments("home", 115)]
    [Arguments("pageup", 116)]
    [Arguments("end", 119)]
    [Arguments("pagedown", 121)]
    [Arguments("left", 123)]
    [Arguments("right", 124)]
    [Arguments("down", 125)]
    [Arguments("up", 126)]
    public async Task TryParseAccelerator_NamedKeys_MapToVirtualKeyCodes(string key, int expected)
    {
        var parsed = MacOSKeyBindingManager.TryParseAccelerator($"Cmd+{key}", out var keyCode, out _);

        await Assert.That(parsed).IsTrue();
        await Assert.That(keyCode).IsEqualTo(expected);
    }

    /// <summary>
    /// 功能键 f1–f20 应映射到 kVK_F1..F20 的固定键码。
    /// </summary>
    [Test]
    [Arguments("f1", 122)]
    [Arguments("f2", 120)]
    [Arguments("f3", 99)]
    [Arguments("f4", 118)]
    [Arguments("f5", 96)]
    [Arguments("f6", 97)]
    [Arguments("f7", 98)]
    [Arguments("f8", 100)]
    [Arguments("f9", 101)]
    [Arguments("f10", 109)]
    [Arguments("f11", 103)]
    [Arguments("f12", 111)]
    [Arguments("f13", 105)]
    [Arguments("f14", 107)]
    [Arguments("f15", 113)]
    [Arguments("f16", 106)]
    [Arguments("f17", 64)]
    [Arguments("f18", 79)]
    [Arguments("f19", 80)]
    [Arguments("f20", 90)]
    public async Task TryParseAccelerator_FunctionKeys_MapToFunctionKeyCodes(string key, int expected)
    {
        var parsed = MacOSKeyBindingManager.TryParseAccelerator($"Cmd+{key}", out var keyCode, out _);

        await Assert.That(parsed).IsTrue();
        await Assert.That(keyCode).IsEqualTo(expected);
    }

    /// <summary>
    /// 非 macOS 骨架目标下注册/注销/分发快捷键均为 no-op，且不得抛出异常。
    /// </summary>
    [Test]
    public async Task Skeleton_RegisterUnregisterHandle_AreSafeNoOps()
    {
        var manager = new MacOSKeyBindingManager();

        await Assert.That(() => manager.RegisterKeyBinding("CmdOrCtrl+Shift+K", () => { })).ThrowsNothing();
        await Assert.That(() => manager.RegisterKeyBinding("CmdOrCtrl+NotAKey", () => { })).ThrowsNothing();
        await Assert.That(() => manager.HandleHotKey(1)).ThrowsNothing();
        await Assert.That(() => manager.UnregisterKeyBinding("CmdOrCtrl+Shift+K")).ThrowsNothing();
    }

    /// <summary>
    /// 骨架目标下未注册的 hotkey id 不应触发任何回调（回调表仅在 <c>#if MACOS</c> 分支填充）。
    /// </summary>
    [Test]
    public async Task Skeleton_HandleHotKey_DoesNotInvokeCallback()
    {
        var manager = new MacOSKeyBindingManager();
        var invoked = false;

        manager.RegisterKeyBinding("CmdOrCtrl+Shift+K", () => invoked = true);
        manager.HandleHotKey(1);

        await Assert.That(invoked).IsFalse();
    }
}
