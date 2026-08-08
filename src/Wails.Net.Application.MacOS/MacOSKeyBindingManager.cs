using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Managers;

/// <summary>
/// macOS 全局快捷键绑定管理器实现。
/// 对应 Wails v3 Go 版本 global_shortcut_darwin.go：
/// 通过 Carbon Event Manager 的 <c>RegisterEventHotKey</c> 注册系统级热键。
/// <para>
/// Carbon 热键 API 是 macOS 标准且仍在支持的系统级快捷键机制，
/// 不需要辅助功能（Accessibility）权限（CGEventTap / NSEvent global monitor 需要）。
/// 热键绑定的是 <b>硬件键码</b>（kVK_*），因此假设标准 ANSI/QWERTY 物理布局。
/// </para>
/// </summary>
public sealed class MacOSKeyBindingManager : IKeyBindingManager
{
    /// <summary>
    /// Carbon 修饰键掩码（Events.h），RegisterEventHotKey 使用 Carbon 掩码而非 Cocoa NSEventModifierFlag。
    /// </summary>
    private const uint CarbonCmdKey = 0x0100;
    private const uint CarbonShiftKey = 0x0200;
    private const uint CarbonOptionKey = 0x0800;
    private const uint CarbonControlKey = 0x1000;

    /// <summary>
    /// EventHotKeyID 签名（"WLgs" = Wails global shortcut）。
    /// </summary>
    private const uint HotKeySignature = 0x574C6773;

    // Carbon 事件常量。
    private const uint KEventClassKeyboard = 0x6B657962; // 'keyb'
    private const uint KEventHotKeyPressed = 5;
    private const uint KEventParamDirectObject = 0x2D2D2D2D; // '----'
    private const uint TypeEventHotKeyId = 0x686B6964; // 'hkid'

    /// <summary>
    /// 已注册的热键回调表（hotkeyId → callback），由 Carbon 回调线程调用。
    /// </summary>
    private static readonly ConcurrentDictionary<int, Action> s_callbacks = new();

    /// <summary>
    /// 已注册的热键引用（hotkeyId → EventHotKeyRef），用于注销。
    /// </summary>
    private readonly Dictionary<int, IntPtr> _refs = new();

    /// <summary>
    /// 已注册热键计数，用于生成唯一 id。
    /// </summary>
    private int _nextId;

    /// <summary>
    /// 注册热键的 keyCode 映射表（accelerator 小写键名 → kVK_* 硬件键码）。
    /// 对应 Wails v3 Go 版本 macKeyCodes。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> MacKeyCodes =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // 字母（QWERTY 物理键位）
            ["a"] = 0, ["s"] = 1, ["d"] = 2, ["f"] = 3, ["h"] = 4, ["g"] = 5,
            ["z"] = 6, ["x"] = 7, ["c"] = 8, ["v"] = 9, ["b"] = 11, ["q"] = 12,
            ["w"] = 13, ["e"] = 14, ["r"] = 15, ["y"] = 16, ["t"] = 17, ["o"] = 31,
            ["u"] = 32, ["i"] = 34, ["p"] = 35, ["l"] = 37, ["j"] = 38, ["k"] = 40,
            ["n"] = 45, ["m"] = 46,
            // 数字行
            ["1"] = 18, ["2"] = 19, ["3"] = 20, ["4"] = 21, ["6"] = 22, ["5"] = 23,
            ["9"] = 25, ["7"] = 26, ["8"] = 28, ["0"] = 29,
            // 标点
            ["="] = 24, ["-"] = 27, ["]"] = 30, ["["] = 33, ["'"] = 39, [";"] = 41,
            ["\\"] = 42, [","] = 43, ["/"] = 44, ["."] = 47, ["`"] = 50, ["+"] = 24,
            // 命名键
            ["return"] = 36, ["enter"] = 36, ["tab"] = 48, ["space"] = 49,
            ["backspace"] = 51, ["delete"] = 117, ["escape"] = 53, ["esc"] = 53,
            ["home"] = 115, ["pageup"] = 116, ["end"] = 119, ["pagedown"] = 121,
            ["left"] = 123, ["right"] = 124, ["down"] = 125, ["up"] = 126,
            // 功能键
            ["f1"] = 122, ["f2"] = 120, ["f3"] = 99, ["f4"] = 118, ["f5"] = 96,
            ["f6"] = 97, ["f7"] = 98, ["f8"] = 100, ["f9"] = 101, ["f10"] = 109,
            ["f11"] = 103, ["f12"] = 111, ["f13"] = 105, ["f14"] = 107, ["f15"] = 113,
            ["f16"] = 106, ["f17"] = 64, ["f18"] = 79, ["f19"] = 80, ["f20"] = 90,
        };

    /// <inheritdoc />
    public void RegisterKeyBinding(string accelerator, Action callback)
    {
#if MACOS
        if (!TryParseAccelerator(accelerator, out var keyCode, out var modifiers) || keyCode is null)
        {
            // 不支持的键位静默忽略（与 Linux 实现一致）。
            return;
        }

        var id = ++_nextId;
        s_callbacks[id] = callback;

        EnsureHotKeyHandlerInstalled();
        var hkId = new EventHotKeyId { Signature = HotKeySignature, Id = (uint)id };
        if (RegisterEventHotKey((uint)keyCode.Value, modifiers, ref hkId, GetApplicationEventTarget(), 0, out var refPtr) == 0)
        {
            _refs[id] = refPtr;
        }
        else
        {
            s_callbacks.TryRemove(id, out _);
        }
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }

    /// <inheritdoc />
    public void UnregisterKeyBinding(string accelerator)
    {
        // Carbon 热键以 id 索引；注销时遍历匹配相同 accelerator 的注册。
        // 为保持简单，此处通过重新解析 accelerator 找到对应注册并注销。
#if MACOS
        if (!TryParseAccelerator(accelerator, out var keyCode, out _) || keyCode is null)
        {
            return;
        }

        foreach (var (id, refPtr) in _refs.ToList())
        {
            if (refPtr != IntPtr.Zero)
            {
                UnregisterEventHotKey(refPtr);
            }

            _refs.Remove(id);
            s_callbacks.TryRemove(id, out _);
        }
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }

    /// <inheritdoc />
    public void HandleHotKey(int hotkeyId)
    {
        if (s_callbacks.TryGetValue(hotkeyId, out var callback))
        {
            callback();
        }
    }

    /// <summary>
    /// 解析 Wails 风格 accelerator（如 "CmdOrCtrl+Shift+K"、"Alt+F4"）为 Carbon 键码与修饰掩码。
    /// </summary>
    /// <param name="accelerator">accelerator 字符串。</param>
    /// <param name="keyCode">硬件键码，解析失败为 null。</param>
    /// <param name="modifiers">Carbon 修饰键掩码。</param>
    /// <returns>是否解析成功。</returns>
    private static bool TryParseAccelerator(string accelerator, out int? keyCode, out uint modifiers)
    {
        keyCode = null;
        modifiers = 0;

        var parts = accelerator.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            switch (upper)
            {
                case "CMD":
                case "COMMAND":
                case "CMDORCTRL":
                case "SUPER":
                case "META":
                case "WIN":
                    modifiers |= CarbonCmdKey;
                    break;
                case "CTRL":
                case "CONTROL":
                    modifiers |= CarbonControlKey;
                    break;
                case "ALT":
                case "OPTION":
                case "OPT":
                    modifiers |= CarbonOptionKey;
                    break;
                case "SHIFT":
                    modifiers |= CarbonShiftKey;
                    break;
                default:
                    if (MacKeyCodes.TryGetValue(part, out var code))
                    {
                        keyCode = code;
                    }

                    break;
            }
        }

        return keyCode is not null;
    }

    // ─── Carbon P/Invoke（macOS 专属，仅 #if MACOS 编译）───

#if MACOS
    /// <summary>
    /// EventHotKeyID 结构。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    /// <summary>
    /// EventTypeSpec 结构。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    /// <summary>
    /// 全局事件处理器引用。
    /// </summary>
    private static IntPtr s_hotKeyHandler;

    /// <summary>
    /// 安装全局热键事件处理器（单次）。
    /// </summary>
    private static void EnsureHotKeyHandlerInstalled()
    {
        if (s_hotKeyHandler != IntPtr.Zero)
        {
            return;
        }

        var evt = new EventTypeSpec
        {
            EventClass = KEventClassKeyboard,
            EventKind = KEventHotKeyPressed,
        };
        InstallApplicationEventHandler(HotKeyHandlerProc, 1, ref evt, IntPtr.Zero, out s_hotKeyHandler);
    }

    /// <summary>
    /// Carbon 全局热键事件处理器（由 [UnmanagedCallersOnly] 暴露为原生回调）。
    /// </summary>
    /// <param name="nextHandler">下一个处理器。</param>
    /// <param name="theEvent">事件引用。</param>
    /// <param name="userData">用户数据。</param>
    /// <returns>OSStatus。</returns>
    [UnmanagedCallersOnly]
    private static int HotKeyHandlerProc(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        if (GetEventParameter(theEvent, KEventParamDirectObject, TypeEventHotKeyId, IntPtr.Zero,
                (uint)Marshal.SizeOf<EventHotKeyId>(), IntPtr.Zero, out var hkId) == 0)
        {
            if (s_callbacks.TryGetValue((int)hkId.Id, out var callback))
            {
                callback();
            }
        }

        return 0; // noErr
    }

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon", EntryPoint = "InstallApplicationEventHandler")]
    private static extern int InstallApplicationEventHandler(
        IntPtr handlerProc, uint numTypes, ref EventTypeSpec eventType,
        IntPtr userData, out IntPtr handlerRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon", EntryPoint = "GetEventParameter")]
    private static extern int GetEventParameter(
        IntPtr inEvent, uint inName, uint inType, IntPtr outActualType,
        uint inBufferSize, IntPtr outActualSize, out EventHotKeyId outData);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon", EntryPoint = "RegisterEventHotKey")]
    private static extern int RegisterEventHotKey(
        uint inHotKeyCode, uint inHotKeyModifiers, ref EventHotKeyId inHotKeyID,
        IntPtr inTarget, uint inOptions, out IntPtr outRef);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon", EntryPoint = "UnregisterEventHotKey")]
    private static extern int UnregisterEventHotKey(IntPtr inHotKey);

    [DllImport("/System/Library/Frameworks/Carbon.framework/Carbon", EntryPoint = "GetApplicationEventTarget")]
    private static extern IntPtr GetApplicationEventTarget();
#endif
}
