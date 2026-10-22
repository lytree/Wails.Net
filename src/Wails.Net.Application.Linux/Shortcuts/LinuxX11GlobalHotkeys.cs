using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux X11 系统级全局快捷键（XGrabKey）。
/// <para>
/// 对应 Wails v3 Go 版本 <c>pkg/application/global_shortcut_linux_x11.go</c>：
/// GTK4 本身只提供窗口内快捷键，系统级热键需向 X 服务器注册键码抓取。
/// 仅在 X11 会话（或 XWayland 下可抓取的 DISPLAY）启用；原生 Wayland 会话
/// 没有等价的公开协议（xdg-desktop-portal GlobalShortcuts 需会话与用户确认，
/// 本实现不启用），此时返回 false 交由调用方回退到窗口内 <c>Gtk.ShortcutController</c>。
/// </para>
/// <para>
/// 实现要点：
/// <list type="bullet">
/// <item><c>XInitThreads</c> 使 Xlib 可跨线程使用：主线程注册、后台线程收事件；</item>
/// <item>抓取时临时替换 Xlib 错误处理器并 <c>XSync</c>，避免 BadAccess（热键已被其他程序占用）
///       触发 Xlib 默认处理器终止进程；</item>
/// <item>同时抓取 CapsLock/NumLock 组合，避免锁定键导致热键失效；</item>
/// <item>事件回调经 GLib 主循环 <c>IdleAdd</c> 切回 GTK 主线程。</item>
/// </list>
/// </para>
/// </summary>
internal sealed class LinuxX11GlobalHotkeys : IDisposable
{
    /// <summary>X11 客户端库。</summary>
    private const string LibX11 = "libX11.so.6";

    /// <summary>Shift 修饰掩码。</summary>
    internal const uint ShiftMask = 1;

    /// <summary>CapsLock 修饰掩码（抓取时需兼容）。</summary>
    private const uint LockMask = 1 << 1;

    /// <summary>Control 修饰掩码。</summary>
    internal const uint ControlMask = 1 << 2;

    /// <summary>Alt / Mod1 修饰掩码。</summary>
    internal const uint Mod1Mask = 1 << 3;

    /// <summary>NumLock / Mod2 修饰掩码（抓取时需兼容）。</summary>
    private const uint Mod2Mask = 1 << 4;

    /// <summary>Super / Win / Mod4 修饰掩码。</summary>
    internal const uint Mod4Mask = 1 << 6;

    /// <summary>X KeyPress 事件类型。</summary>
    private const int KeyPressType = 2;

    /// <summary>X GrabModeAsync。</summary>
    private const int GrabModeAsync = 1;

    /// <summary>注册映射：键码 + 修饰掩码 → 回调。</summary>
    private readonly ConcurrentDictionary<(int KeyCode, uint Modifiers), Action> _bindings = new();

    /// <summary>accelerator → 键码 + 修饰掩码，供注销使用。</summary>
    private readonly ConcurrentDictionary<string, (int KeyCode, uint Modifiers)> _accelerators =
        new(StringComparer.Ordinal);

    /// <summary>X 服务器连接。</summary>
    private IntPtr _display;

    /// <summary>根窗口。</summary>
    private IntPtr _root;

    /// <summary>事件循环线程。</summary>
    private Thread? _eventThread;

    /// <summary>事件循环运行标志。</summary>
    private volatile bool _running;

    /// <summary>是否已释放。</summary>
    private bool _disposed;

    /// <summary>
    /// 当前会话是否可能支持 X11 全局抓取。
    /// 原生 Wayland（<c>XDG_SESSION_TYPE=wayland</c> 且无 DISPLAY）返回 false。
    /// </summary>
    internal static bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsLinux())
            {
                return false;
            }

            var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            var display = Environment.GetEnvironmentVariable("DISPLAY");
            if (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(display))
            {
                return false;
            }

            return !string.IsNullOrEmpty(display);
        }
    }

    /// <summary>
    /// 将 Wails 风格 accelerator 解析为 X11 keysym 名称与修饰掩码。
    /// 支持 Ctrl/Alt/Shift/Super 组合与常用命名键（Return/Escape/F1..F24/方向键等）。
    /// </summary>
    /// <param name="accelerator">accelerator 字符串（如 <c>CmdOrCtrl+Shift+K</c>）。</param>
    /// <param name="keysymName">X11 keysym 名称。</param>
    /// <param name="modifiers">X11 修饰掩码。</param>
    /// <returns>解析成功返回 true。</returns>
    internal static bool TryParseAccelerator(string accelerator, out string keysymName, out uint modifiers)
    {
        keysymName = string.Empty;
        modifiers = 0;

        if (string.IsNullOrWhiteSpace(accelerator))
        {
            return false;
        }

        var parts = accelerator.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        string? key = null;
        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            switch (upper)
            {
                case "CTRL":
                case "CONTROL":
                case "CMDORCTRL":
                    // Linux 上 CmdOrCtrl 映射为 Control（与 Go global_shortcut_linux_x11.go 一致）。
                    modifiers |= ControlMask;
                    break;
                case "ALT":
                case "OPTION":
                case "OPT":
                    modifiers |= Mod1Mask;
                    break;
                case "SHIFT":
                    modifiers |= ShiftMask;
                    break;
                case "SUPER":
                case "WIN":
                case "META":
                case "CMD":
                case "COMMAND":
                    modifiers |= Mod4Mask;
                    break;
                default:
                    key = TranslateKey(part);
                    break;
            }
        }

        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        keysymName = key;
        return true;
    }

    /// <summary>
    /// 将 accelerator 键名转换为 X11 keysym 名称。
    /// </summary>
    /// <param name="key">键名。</param>
    /// <returns>keysym 名称；无法识别时返回规范化后的原值。</returns>
    internal static string TranslateKey(string key)
    {
        var lower = key.ToLowerInvariant();
        return lower switch
        {
            "backspace" => "BackSpace",
            "tab" => "Tab",
            "return" or "enter" => "Return",
            "escape" or "esc" => "Escape",
            "space" or "spacebar" => "space",
            "delete" or "del" => "Delete",
            "insert" or "ins" => "Insert",
            "home" => "Home",
            "end" => "End",
            "pageup" or "pgup" => "Prior",
            "pagedown" or "pgdn" => "Next",
            "left" or "arrowleft" => "Left",
            "right" or "arrowright" => "Right",
            "up" or "arrowup" => "Up",
            "down" or "arrowdown" => "Down",
            "minus" => "minus",
            "plus" => "plus",
            "comma" => "comma",
            "period" or "dot" => "period",
            "slash" => "slash",
            "backslash" => "backslash",
            "semicolon" => "semicolon",
            "quote" or "apostrophe" => "apostrophe",
            "backquote" or "grave" => "grave",
            "bracketleft" or "leftbracket" => "bracketleft",
            "bracketright" or "rightbracket" => "bracketright",
            _ => lower.Length is 2 or 3 && lower[0] == 'f' && int.TryParse(lower[1..], out var fn) && fn is >= 1 and <= 24
                ? "F" + fn
                : lower,
        };
    }

    /// <summary>
    /// 注册系统级快捷键。
    /// </summary>
    /// <param name="accelerator">accelerator 字符串。</param>
    /// <param name="callback">触发回调。</param>
    /// <returns>抓取成功返回 true；不可用/已被占用/解析失败返回 false。</returns>
    internal bool TryRegister(string accelerator, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (_disposed || !IsAvailable
            || !TryParseAccelerator(accelerator, out var keysymName, out var modifiers))
        {
            return false;
        }

        if (!EnsureStarted())
        {
            return false;
        }

        var keysym = XStringToKeysym(keysymName);
        if (keysym == IntPtr.Zero)
        {
            return false;
        }

        var keycode = XKeysymToKeycode(_display, keysym);
        if (keycode == 0)
        {
            return false;
        }

        if (!TryGrabKey(keycode, modifiers))
        {
            return false;
        }

        _bindings[(keycode, modifiers)] = callback;
        _accelerators[accelerator] = (keycode, modifiers);
        return true;
    }

    /// <summary>
    /// 注销系统级快捷键（若由本管理器注册）。
    /// </summary>
    /// <param name="accelerator">accelerator 字符串。</param>
    /// <returns>确实注销返回 true。</returns>
    internal bool TryUnregister(string accelerator)
    {
        if (!_accelerators.TryRemove(accelerator, out var binding) || _display == IntPtr.Zero)
        {
            return false;
        }

        _bindings.TryRemove(binding, out _);
        UngrabKey(binding.KeyCode, binding.Modifiers);
        return true;
    }

    /// <summary>
    /// 初始化 X 连接并启动事件循环线程（幂等）。
    /// </summary>
    /// <returns>初始化成功返回 true。</returns>
    private bool EnsureStarted()
    {
        if (_display != IntPtr.Zero)
        {
            return true;
        }

        lock (_accelerators)
        {
            if (_display != IntPtr.Zero)
            {
                return true;
            }

            try
            {
                // 允许 Xlib 跨线程使用（主线程注册、后台线程收事件）。
                _ = XInitThreads();

                var display = XOpenDisplay(null);
                if (display == IntPtr.Zero)
                {
                    return false;
                }

                _display = display;
                _root = XDefaultRootWindow(display);
                _running = true;

                _eventThread = new Thread(RunEventLoop)
                {
                    IsBackground = true,
                    Name = "WailsNet-X11-GlobalHotkeys",
                };
                _eventThread.Start();
                return true;
            }
            catch
            {
                CloseDisplay();
                return false;
            }
        }
    }

    /// <summary>
    /// 抓取键码（含 CapsLock/NumLock 组合）。
    /// </summary>
    /// <param name="keycode">X11 键码。</param>
    /// <param name="modifiers">修饰掩码。</param>
    /// <returns>抓取成功返回 true。</returns>
    private bool TryGrabKey(int keycode, uint modifiers)
    {
        var ok = true;

        // 临时替换 Xlib 错误处理器：BadAccess 表示键已被其他客户端抓取，
        // 默认处理器会打印错误并退出进程，这里必须静默吞掉。
        var handler = Marshal.GetFunctionPointerForDelegate(NoopErrorHandler);
        var previous = IntPtr.Zero;
        try
        {
            previous = XSetErrorHandler(handler);

            foreach (var extra in new uint[] { 0, LockMask, Mod2Mask, LockMask | Mod2Mask })
            {
                var result = XGrabKey(
                    _display,
                    keycode,
                    modifiers | extra,
                    _root,
                    ownerEvents: 0,
                    pointerMode: GrabModeAsync,
                    keyboardMode: GrabModeAsync);
                ok &= result != 0;
            }

            // 同步以接收 BadAccess 异步错误。
            _ = XSync(_display, 0);
        }
        finally
        {
            if (previous != IntPtr.Zero)
            {
                _ = XSetErrorHandler(previous);
            }
        }

        return ok;
    }

    /// <summary>
    /// 取消抓取键码（含 CapsLock/NumLock 组合）。
    /// </summary>
    /// <param name="keycode">X11 键码。</param>
    /// <param name="modifiers">修饰掩码。</param>
    private void UngrabKey(int keycode, uint modifiers)
    {
        foreach (var extra in new uint[] { 0, LockMask, Mod2Mask, LockMask | Mod2Mask })
        {
            _ = XUngrabKey(_display, keycode, modifiers | extra, _root);
        }

        _ = XFlush(_display);
    }

    /// <summary>
    /// X11 事件循环：轮询 KeyPress 并分发回调。
    /// </summary>
    private void RunEventLoop()
    {
        while (_running)
        {
            try
            {
                while (_running && XPending(_display) > 0)
                {
                    if (XNextEvent(_display, out var evt) != 0)
                    {
                        break;
                    }

                    if (evt.Type != KeyPressType)
                    {
                        continue;
                    }

                    // 归一化锁定键状态，与抓取时使用的组合保持一致。
                    var modifiers = evt.State & ~(LockMask | Mod2Mask);
                    if (_bindings.TryGetValue(((int)evt.KeyCode, modifiers), out var callback))
                    {
                        Dispatch(callback);
                    }
                }
            }
            catch
            {
                // X 连接异常（服务器关闭等）：退出事件循环。
                break;
            }

            Thread.Sleep(20);
        }
    }

    /// <summary>
    /// 将热键回调切回 GTK 主线程执行。
    /// </summary>
    /// <param name="callback">待执行回调。</param>
    private static void Dispatch(Action callback)
    {
        try
        {
            GLib.Functions.IdleAdd(0, () =>
            {
                callback();
                return false;
            });
        }
        catch
        {
            // 主循环不可用（退出阶段）：直接执行以避免丢失一次触发。
            callback();
        }
    }

    /// <summary>
    /// 关闭 X 连接并停止事件线程。
    /// </summary>
    private void CloseDisplay()
    {
        _running = false;

        if (_eventThread is not null && _eventThread.IsAlive)
        {
            try
            {
                _eventThread.Join(500);
            }
            catch
            {
                // 忽略线程结束异常。
            }
        }

        _eventThread = null;

        if (_display != IntPtr.Zero)
        {
            try
            {
                _ = XCloseDisplay(_display);
            }
            catch
            {
                // 忽略关闭异常。
            }

            _display = IntPtr.Zero;
            _root = IntPtr.Zero;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var binding in _accelerators.Values)
        {
            if (_display != IntPtr.Zero)
            {
                UngrabKey(binding.KeyCode, binding.Modifiers);
            }
        }

        _accelerators.Clear();
        _bindings.Clear();
        CloseDisplay();
        _disposed = true;
    }

    /// <summary>
    /// 空错误处理器：吞掉 XGrabKey 的 BadAccess 等异步错误，避免 Xlib 默认处理器终止进程。
    /// </summary>
    private static readonly XErrorHandler NoopErrorHandler = (_, _) => 0;

    /// <summary>
    /// Xlib 错误处理器签名。
    /// </summary>
    /// <param name="display">X 连接。</param>
    /// <param name="errorEvent">XErrorEvent*。</param>
    /// <returns>返回值（忽略）。</returns>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    /// <summary>
    /// XKeyEvent 子集布局；<see cref="Size"/> 以 XEvent 联合体大小（192 字节）保证原生写入安全。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 192)]
    private struct XEvent
    {
        /// <summary>事件类型。</summary>
        public int Type;

        /// <summary>X 连接。</summary>
        public IntPtr Display;

        /// <summary>事件窗口。</summary>
        public IntPtr Window;

        /// <summary>根窗口。</summary>
        public IntPtr Root;

        /// <summary>子窗口。</summary>
        public IntPtr SubWindow;

        /// <summary>时间戳。</summary>
        public IntPtr Time;

        /// <summary>窗口内 X 坐标。</summary>
        public int X;

        /// <summary>窗口内 Y 坐标。</summary>
        public int Y;

        /// <summary>根窗口 X 坐标。</summary>
        public int XRoot;

        /// <summary>根窗口 Y 坐标。</summary>
        public int YRoot;

        /// <summary>修饰键状态。</summary>
        public uint State;

        /// <summary>键码。</summary>
        public uint KeyCode;
    }

    /// <summary>初始化 Xlib 线程安全（必须在打开连接前调用）。</summary>
    /// <returns>成功返回非零。</returns>
    [DllImport(LibX11)]
    private static extern int XInitThreads();

    /// <summary>打开 X 服务器连接。</summary>
    /// <param name="displayName">显示名（null 使用 DISPLAY 环境变量）。</param>
    /// <returns>连接句柄；失败为 IntPtr.Zero。</returns>
    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay([MarshalAs(UnmanagedType.LPUTF8Str)] string? displayName);

    /// <summary>关闭 X 服务器连接。</summary>
    /// <param name="display">连接句柄。</param>
    /// <returns>状态码。</returns>
    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    /// <summary>获取默认根窗口。</summary>
    /// <param name="display">连接句柄。</param>
    /// <returns>根窗口句柄。</returns>
    [DllImport(LibX11)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    /// <summary>将 keysym 名称转换为 keysym。</summary>
    /// <param name="name">keysym 名称。</param>
    /// <returns>keysym；失败为 IntPtr.Zero。</returns>
    [DllImport(LibX11)]
    private static extern IntPtr XStringToKeysym([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    /// <summary>将 keysym 转换为键码。</summary>
    /// <param name="display">连接句柄。</param>
    /// <param name="keysym">keysym。</param>
    /// <returns>键码；失败为 0。</returns>
    [DllImport(LibX11)]
    private static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);

    /// <summary>抓取键码。</summary>
    /// <param name="display">连接句柄。</param>
    /// <param name="keycode">键码。</param>
    /// <param name="modifiers">修饰掩码。</param>
    /// <param name="grabWindow">抓取窗口（根窗口）。</param>
    /// <param name="ownerEvents">owner_events 标志。</param>
    /// <param name="pointerMode">指针模式。</param>
    /// <param name="keyboardMode">键盘模式。</param>
    /// <returns>成功返回非零。</returns>
    [DllImport(LibX11)]
    private static extern int XGrabKey(
        IntPtr display,
        int keycode,
        uint modifiers,
        IntPtr grabWindow,
        int ownerEvents,
        int pointerMode,
        int keyboardMode);

    /// <summary>取消键码抓取。</summary>
    /// <param name="display">连接句柄。</param>
    /// <param name="keycode">键码。</param>
    /// <param name="modifiers">修饰掩码。</param>
    /// <param name="grabWindow">抓取窗口。</param>
    /// <returns>状态码。</returns>
    [DllImport(LibX11)]
    private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);

    /// <summary>查询待处理事件数。</summary>
    /// <param name="display">连接句柄。</param>
    /// <returns>待处理事件数。</returns>
    [DllImport(LibX11)]
    private static extern int XPending(IntPtr display);

    /// <summary>取出下一个事件。</summary>
    /// <param name="display">连接句柄。</param>
    /// <param name="evt">事件缓冲。</param>
    /// <returns>状态码。</returns>
    [DllImport(LibX11)]
    private static extern int XNextEvent(IntPtr display, out XEvent evt);

    /// <summary>设置 Xlib 错误处理器。</summary>
    /// <param name="handler">处理器函数指针。</param>
    /// <returns>原处理器函数指针。</returns>
    [DllImport(LibX11)]
    private static extern IntPtr XSetErrorHandler(IntPtr handler);

    /// <summary>刷新并等待服务器处理。</summary>
    /// <param name="display">连接句柄。</param>
    /// <param name="discard">是否丢弃事件。</param>
    /// <returns>状态码。</returns>
    [DllImport(LibX11)]
    private static extern int XSync(IntPtr display, int discard);

    /// <summary>刷新输出缓冲。</summary>
    /// <param name="display">连接句柄。</param>
    /// <returns>状态码。</returns>
    [DllImport(LibX11)]
    private static extern int XFlush(IntPtr display);
}
