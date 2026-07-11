using System.Runtime.InteropServices;
using Wails.Net.Application.Managers;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Win32 平台快捷键绑定管理器实现。
/// 对应 Wails v3 Go 版本 pkg/services/keys/ 模块。
/// 通过 CsWin32 源生成器调用 RegisterHotKey/UnregisterHotKey Win32 API 注册全局热键，
/// 在窗口过程收到 WM_HOTKEY 消息时由 HandleHotKey 分发到对应回调。
/// </summary>
public sealed class Win32KeyBindingManager : IKeyBindingManager, IDisposable
{
    /// <summary>
    /// MOD_ALT 修饰键常量（0x0001）。
    /// </summary>
    private const HOT_KEY_MODIFIERS ModAlt = (HOT_KEY_MODIFIERS)0x0001;

    /// <summary>
    /// MOD_CONTROL 修饰键常量（0x0002）。
    /// </summary>
    private const HOT_KEY_MODIFIERS ModControl = (HOT_KEY_MODIFIERS)0x0002;

    /// <summary>
    /// MOD_SHIFT 修饰键常量（0x0004）。
    /// </summary>
    private const HOT_KEY_MODIFIERS ModShift = (HOT_KEY_MODIFIERS)0x0004;

    /// <summary>
    /// MOD_WIN 修饰键常量（0x0008）。
    /// </summary>
    private const HOT_KEY_MODIFIERS ModWin = (HOT_KEY_MODIFIERS)0x0008;

    /// <summary>
    /// MOD_NOREPEAT 修饰键常量（0x4000），防止按键按住时重复触发。
    /// </summary>
    private const HOT_KEY_MODIFIERS ModNorepeat = (HOT_KEY_MODIFIERS)0x4000;

    /// <summary>
    /// VK_F1 虚拟键码基值（0x70），F1-F24 为 0x70-0x87。
    /// </summary>
    private const uint VkF1 = 0x70;

    /// <summary>
    /// 用于生成唯一热键 ID 的静态计数器，起始值为 0，首次自增后返回 1。
    /// </summary>
    private static int s_nextHotKeyId = 0;

    /// <summary>
    /// 注册热键所用的 Win32 窗口句柄。
    /// </summary>
    private readonly HWND _hwnd;

    /// <summary>
    /// 热键 ID 到回调的映射表。
    /// </summary>
    private readonly Dictionary<int, Action> _hotkeys = new();

    /// <summary>
    /// accelerator 字符串到热键 ID 的映射表（忽略大小写）。
    /// </summary>
    private readonly Dictionary<string, int> _acceleratorToId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 保护共享状态的锁对象。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 标记是否已释放。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 构造 Win32KeyBindingManager 实例。
    /// </summary>
    /// <param name="hwnd">用于注册热键的窗口句柄。</param>
    public Win32KeyBindingManager(nint hwnd)
    {
        _hwnd = new HWND(hwnd);
    }

    /// <summary>
    /// 构造无窗口句柄的 Win32KeyBindingManager 实例。
    /// 使用线程级热键注册，WM_HOTKEY 消息投递到线程消息队列，
    /// 由 WindowsPlatformApp.Run() 的消息循环处理。
    /// </summary>
    public Win32KeyBindingManager()
    {
        _hwnd = HWND.Null;
    }

    /// <inheritdoc />
    public void RegisterKeyBinding(string accelerator, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (string.IsNullOrWhiteSpace(accelerator))
        {
            throw new ArgumentException("快捷键描述不能为空。", nameof(accelerator));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        var (modifiers, vk) = ParseAccelerator(accelerator);
        var id = Interlocked.Increment(ref s_nextHotKeyId);

        lock (_lock)
        {
            // 若该 accelerator 已注册，先注销旧绑定。
            if (_acceleratorToId.TryGetValue(accelerator, out var oldId))
            {
                PInvoke.UnregisterHotKey(_hwnd, oldId);
                _hotkeys.Remove(oldId);
                _acceleratorToId.Remove(accelerator);
            }

            if (!PInvoke.RegisterHotKey(_hwnd, id, modifiers, vk))
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"注册快捷键失败: {accelerator}，错误码: {error}");
            }

            _hotkeys[id] = callback;
            _acceleratorToId[accelerator] = id;
        }
    }

    /// <inheritdoc />
    public void UnregisterKeyBinding(string accelerator)
    {
        if (string.IsNullOrWhiteSpace(accelerator))
        {
            return;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (!_acceleratorToId.TryGetValue(accelerator, out var id))
            {
                return;
            }

            PInvoke.UnregisterHotKey(_hwnd, id);
            _hotkeys.Remove(id);
            _acceleratorToId.Remove(accelerator);
        }
    }

    /// <inheritdoc />
    public void HandleHotKey(int hotkeyId)
    {
        if (_disposed)
        {
            return;
        }

        Action? callback;
        lock (_lock)
        {
            if (!_hotkeys.TryGetValue(hotkeyId, out callback))
            {
                return;
            }
        }

        callback?.Invoke();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var id in _hotkeys.Keys)
            {
                PInvoke.UnregisterHotKey(_hwnd, id);
            }

            _hotkeys.Clear();
            _acceleratorToId.Clear();
        }

        _disposed = true;
    }

    /// <summary>
    /// 解析 accelerator 字符串为修饰键标志和虚拟键码。
    /// 支持 "Ctrl+C"、"Alt+F4"、"Shift+Tab"、"Ctrl+Shift+P" 等格式（用 + 分隔）。
    /// </summary>
    /// <param name="accelerator">快捷键描述字符串。</param>
    /// <returns>修饰键标志和虚拟键码的元组。</returns>
    /// <exception cref="ArgumentException">无法解析的修饰键或主键。</exception>
    private static (HOT_KEY_MODIFIERS Modifiers, uint Vk) ParseAccelerator(string accelerator)
    {
        var parts = accelerator.Split('+');
        if (parts.Length == 0)
        {
            throw new ArgumentException($"无效的快捷键描述: {accelerator}", nameof(accelerator));
        }

        // 默认附加 MOD_NOREPEAT 防止重复触发
        var modifiers = ModNorepeat;

        // 除最后一部分外均为修饰键
        for (var i = 0; i < parts.Length - 1; i++)
        {
            modifiers |= ParseModifier(parts[i].Trim());
        }

        // 最后一部分为主键
        var keyPart = parts[^1].Trim();
        var vk = ParseVirtualKey(keyPart);

        return (modifiers, vk);
    }

    /// <summary>
    /// 解析修饰键字符串为对应的修饰键标志。
    /// </summary>
    /// <param name="modifier">修饰键字符串（如 "Ctrl"、"Alt"、"Shift"、"Win"）。</param>
    /// <returns>修饰键标志。</returns>
    /// <exception cref="ArgumentException">未知的修饰键。</exception>
    private static HOT_KEY_MODIFIERS ParseModifier(string modifier)
    {
        return modifier.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => ModControl,
            "ALT" or "OPTION" or "OPT" => ModAlt,
            "SHIFT" => ModShift,
            "WIN" or "SUPER" or "META" => ModWin,
            _ => throw new ArgumentException($"未知的修饰键: {modifier}", nameof(modifier))
        };
    }

    /// <summary>
    /// 解析主键字符串为对应的虚拟键码。
    /// 支持 A-Z（0x41-0x5A）、0-9（0x30-0x39）、F1-F24（0x70-0x87）及常用特殊键。
    /// </summary>
    /// <param name="key">主键字符串。</param>
    /// <returns>虚拟键码。</returns>
    /// <exception cref="ArgumentException">无法解析的主键。</exception>
    private static uint ParseVirtualKey(string key)
    {
        // 单字符键：A-Z 或 0-9
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z')
            {
                return (uint)c; // 0x41-0x5A
            }

            if (c >= '0' && c <= '9')
            {
                return (uint)c; // 0x30-0x39
            }

            throw new ArgumentException($"无法解析的按键: {key}", nameof(key));
        }

        // 功能键 F1-F24
        if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase))
        {
            var numStr = key[1..];
            if (int.TryParse(numStr, out var n) && n >= 1 && n <= 24)
            {
                return VkF1 + (uint)(n - 1);
            }

            throw new ArgumentException($"无效的功能键: {key}", nameof(key));
        }

        // 常用特殊键
        return key.ToUpperInvariant() switch
        {
            "SPACE" or "SPACEBAR" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "BACKSPACE" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            _ => throw new ArgumentException($"无法解析的按键: {key}", nameof(key))
        };
    }
}
