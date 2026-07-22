using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using Menu = Wails.Net.Application.Menus.Menu;
using Rect = Wails.Net.Application.Screens.Rect;
using Screen = Wails.Net.Application.Screens.Screen;

namespace Wails.Net.Application.SystemTray;

/// <summary>
/// Win32 系统托盘实现。
/// 对应 Go 版 application_windows.go 中的 SystemTray。
/// 通过 Shell_NotifyIconW 注册托盘图标，使用仅消息窗口接收托盘鼠标事件，
/// 右键点击弹出上下文菜单。仅消息窗口与 WebviewWindow 共用同一窗口类，
/// 因此 StaticWindowProc 优先调用 TryHandleTrayMessage 检查托盘回调消息。
/// </summary>
public sealed class Win32SystemTray : ISystemTrayImpl, IDisposable
{
    /// <summary>
    /// 托盘回调消息：WM_APP + 0x1000 = 0x9000。
    /// 避免与 WindowsPlatformApp 的 WM_APP+1/2/3 冲突。
    /// </summary>
    internal const uint WmTrayCallback = WindowsPlatformApp.WmApp + 0x1000;

    /// <inheritdoc />
    public event Action? OnTrayClick;

    /// <inheritdoc />
    public event Action? OnTrayRightClick;

    /// <inheritdoc />
    public event Action? OnTrayDoubleClick;

    /// <inheritdoc />
    public event Action? OnTrayRightDoubleClick;

    /// <inheritdoc />
    public event Action? OnTrayMouseEnter;

    /// <inheritdoc />
    public event Action? OnTrayMouseLeave;

    /// <summary>
    /// 托盘图标 ID。
    /// </summary>
    private const uint TrayIconId = 1;

    /// <summary>
    /// szTip 缓冲区长度（128 字符）。
    /// </summary>
    private const int TipBufferLength = 128;

    /// <summary>
    /// WM_LBUTTONUP 消息（0x0202）。
    /// </summary>
    private const uint WmLButtonUp = 0x0202;

    /// <summary>
    /// WM_RBUTTONUP 消息（0x0205）。
    /// </summary>
    private const uint WmRButtonUp = 0x0205;

    /// <summary>
    /// WM_LBUTTONDBLCLK 消息（0x0203）。
    /// </summary>
    private const uint WmLButtonDblClk = 0x0203;

    /// <summary>
    /// WM_RBUTTONDBLCLK 消息（0x0206）。
    /// </summary>
    private const uint WmRButtonDblClk = 0x0206;

    /// <summary>
    /// NIN_POPUPOPEN 消息（0x0404），鼠标进入托盘图标弹出区域。
    /// 仅在 NOTIFYICON_VERSION_4 下发送。
    /// </summary>
    private const uint NinPopupOpen = 0x0404;

    /// <summary>
    /// NIN_POPUPCLOSE 消息（0x0405），鼠标离开托盘图标弹出区域。
    /// 仅在 NOTIFYICON_VERSION_4 下发送。
    /// </summary>
    private const uint NinPopupClose = 0x0405;

    /// <summary>
    /// NIM_ADD：添加托盘图标。
    /// </summary>
    private const uint NimAdd = 0x00000000;

    /// <summary>
    /// NIM_MODIFY：修改托盘图标。
    /// </summary>
    private const uint NimModify = 0x00000001;

    /// <summary>
    /// NIM_DELETE：删除托盘图标。
    /// </summary>
    private const uint NimDelete = 0x00000002;

    /// <summary>
    /// NIF_MESSAGE：设置回调消息。
    /// </summary>
    private const uint NifMessage = 0x00000001;

    /// <summary>
    /// NIF_ICON：设置图标。
    /// </summary>
    private const uint NifIcon = 0x00000002;

    /// <summary>
    /// NIF_TIP：设置提示文本。
    /// </summary>
    private const uint NifTip = 0x00000004;

    /// <summary>
    /// HWND_MESSAGE 常量（(HWND)-3），用于创建仅消息窗口。
    /// </summary>
    private static readonly HWND HwndMessage = (HWND)(nint)(-3);

    /// <summary>
    /// HWND -> Win32SystemTray 实例映射，用于窗口过程查找托盘实例。
    /// </summary>
    private static readonly ConcurrentDictionary<IntPtr, Win32SystemTray> s_traysByHwnd = new();

    /// <summary>
    /// 托盘消息窗口句柄。
    /// </summary>
    private HWND _hwnd;

    /// <summary>
    /// 当前托盘图标句柄。
    /// </summary>
    private HICON _hicon;

    /// <summary>
    /// 关联的上下文菜单。
    /// </summary>
    private Win32ContextMenu? _contextMenu;

    /// <summary>
    /// 工具提示文本。
    /// </summary>
    private string _tooltip = string.Empty;

    /// <summary>
    /// 标签文本。
    /// </summary>
    private string _label = string.Empty;

    /// <summary>
    /// 是否已添加到任务栏通知区域。
    /// </summary>
    private bool _visible;

    /// <summary>
    /// 获取托盘是否可见。
    /// </summary>
    public bool IsVisible => _visible;

    /// <summary>
    /// 是否已释放。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 通过 AttachWindow 关联的窗口，托盘点击时切换其显示状态。
    /// 对应 Wails v3 SystemTray.attachedWindow.Window。
    /// </summary>
    private WebviewWindow? _attachedWindow;

    /// <summary>
    /// PositionWindow 使用的窗口偏移量（像素）。
    /// 对应 Wails v3 SystemTray.attachedWindow.Offset。
    /// </summary>
    private int _windowOffset;

    /// <summary>
    /// 窗口切换去抖动时间（毫秒）。Windows 平台用于避免点击托盘后窗口被立即隐藏再显示。
    /// 对应 Wails v3 SystemTray.attachedWindow.Debounce，默认 200ms。
    /// </summary>
    private int _windowDebounceMs = 200;

    /// <summary>
    /// 关联窗口是否已被显示过一次，用于 ToggleWindow 首次状态判定。
    /// 对应 Wails v3 SystemTray.attachedWindow.hasBeenShown。
    /// </summary>
    private bool _hasBeenShown;

    /// <summary>
    /// 构造 Win32SystemTray 实例，确保窗口类已注册并创建仅消息窗口。
    /// </summary>
    public Win32SystemTray()
    {
        // 确保共享窗口类已注册（与 WebviewWindow 共用同一 WndProc）。
        Win32WebviewWindow.EnsureWindowClassRegistered();
        CreateTrayWindow();
    }

    /// <summary>
    /// 创建仅消息窗口以接收托盘回调消息。
    /// </summary>
    private unsafe void CreateTrayWindow()
    {
        _hwnd = PInvoke.CreateWindowEx(
            dwExStyle: 0,
            lpClassName: Win32WebviewWindow.WindowClassName,
            lpWindowName: "WailsNetTray",
            dwStyle: 0,
            X: 0, Y: 0, nWidth: 0, nHeight: 0,
            hWndParent: HwndMessage,
            hMenu: default,
            hInstance: default,
            lpParam: null);

        if (!_hwnd.IsNull)
        {
            s_traysByHwnd[(IntPtr)_hwnd] = this;
        }
    }

    /// <inheritdoc />
    public void SetIcon(byte[] iconData)
    {
        if (iconData is null || iconData.Length == 0)
        {
            return;
        }

        // 销毁旧图标。
        if (!_hicon.IsNull)
        {
            PInvoke.DestroyIcon(_hicon);
            _hicon = default;
        }

        _hicon = WindowsPlatformApp.LoadIconFromBytes(iconData);

        // 若已可见，更新托盘图标。
        if (_visible)
        {
            ModifyTrayIcon();
        }
    }

    /// <inheritdoc />
    public void SetLabel(string label)
    {
        _label = label ?? string.Empty;
    }

    /// <inheritdoc />
    public void SetMenu(Menu? menu)
    {
        _contextMenu?.Dispose();
        _contextMenu = menu is null ? null : new Win32ContextMenu(menu);
    }

    /// <inheritdoc />
    public void Show()
    {
        if (_hwnd.IsNull || _visible)
        {
            return;
        }

        _visible = true;
        AddTrayIcon();
    }

    /// <inheritdoc />
    public void Hide()
    {
        if (_hwnd.IsNull || !_visible)
        {
            return;
        }

        _visible = false;
        DeleteTrayIcon();
    }

    /// <inheritdoc />
    public void Destroy()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        DeleteTrayIcon();

        _contextMenu?.Dispose();
        _contextMenu = null;

        if (!_hicon.IsNull)
        {
            PInvoke.DestroyIcon(_hicon);
            _hicon = default;
        }

        if (!_hwnd.IsNull)
        {
            s_traysByHwnd.TryRemove((IntPtr)_hwnd, out _);
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = default;
        }
    }

    /// <inheritdoc />
    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip ?? string.Empty;
        if (_visible)
        {
            ModifyTrayIcon();
        }
    }

    /// <inheritdoc />
    public void SetDarkModeIcon(byte[] iconData)
    {
        // 简化实现：直接覆盖当前图标。完整实现可在主题切换时切换图标。
        if (iconData is not null && iconData.Length > 0)
        {
            SetIcon(iconData);
        }
    }

    /// <summary>
    /// 添加托盘图标到任务栏通知区域。
    /// </summary>
    private void AddTrayIcon()
    {
        var data = CreateNotifyIconData();
        Shell_NotifyIconW(NimAdd, ref data);
    }

    /// <summary>
    /// 修改托盘图标属性。
    /// </summary>
    private void ModifyTrayIcon()
    {
        var data = CreateNotifyIconData();
        Shell_NotifyIconW(NimModify, ref data);
    }

    /// <summary>
    /// 删除托盘图标。
    /// </summary>
    private void DeleteTrayIcon()
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = (IntPtr)_hwnd,
            uID = TrayIconId,
        };

        Shell_NotifyIconW(NimDelete, ref data);
    }

    /// <summary>
    /// 创建并填充 NOTIFYICONDATAW 结构。
    /// </summary>
    private NOTIFYICONDATAW CreateNotifyIconData()
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = (IntPtr)_hwnd,
            uID = TrayIconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTrayCallback,
            hIcon = (IntPtr)_hicon,
            szTip = _tooltip ?? string.Empty,
        };

        return data;
    }

    /// <summary>
    /// 尝试处理托盘消息。由 Win32WebviewWindow.StaticWindowProc 调用，
    /// 因为系统托盘消息窗口共用同一窗口类和 WndProc。
    /// </summary>
    /// <param name="hWnd">消息窗口句柄。</param>
    /// <param name="msg">消息 ID。</param>
    /// <param name="wParam">wParam。</param>
    /// <param name="lParam">lParam。</param>
    /// <param name="result">处理结果。</param>
    /// <returns>若消息已处理返回 true，否则 false。</returns>
    internal static bool TryHandleTrayMessage(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam, out LRESULT result)
    {
        result = default;

        if (msg != WmTrayCallback)
        {
            return false;
        }

        if (!s_traysByHwnd.TryGetValue((IntPtr)hWnd, out var tray))
        {
            return false;
        }

        var mouseMsg = (uint)(nint)lParam;

        switch (mouseMsg)
        {
            case WmRButtonUp:
                // 右键点击：触发 OnTrayRightClick 事件，并默认弹出上下文菜单。
                tray.OnTrayRightClick?.Invoke();
                tray.ShowContextMenu();
                result = (LRESULT)1;
                return true;
            case WmLButtonUp:
                // 左键点击：触发 OnTrayClick 事件。
                tray.OnTrayClick?.Invoke();
                result = (LRESULT)1;
                return true;
            case WmLButtonDblClk:
                // 左键双击：触发 OnTrayDoubleClick 事件。
                tray.OnTrayDoubleClick?.Invoke();
                result = (LRESULT)1;
                return true;
            case WmRButtonDblClk:
                // 右键双击：触发 OnTrayRightDoubleClick 事件。
                tray.OnTrayRightDoubleClick?.Invoke();
                result = (LRESULT)1;
                return true;
            case NinPopupOpen:
                // 鼠标进入托盘弹出区域：触发 OnTrayMouseEnter 事件。
                tray.OnTrayMouseEnter?.Invoke();
                result = (LRESULT)1;
                return true;
            case NinPopupClose:
                // 鼠标离开托盘弹出区域：触发 OnTrayMouseLeave 事件。
                tray.OnTrayMouseLeave?.Invoke();
                result = (LRESULT)1;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 在当前鼠标位置弹出上下文菜单。
    /// </summary>
    private void ShowContextMenu()
    {
        if (_contextMenu is null || _hwnd.IsNull)
        {
            return;
        }

        PInvoke.GetCursorPos(out var pt);
        _contextMenu.PopupAt(_hwnd, pt.X, pt.Y);
    }

    /// <inheritdoc />
    public void AttachWindow(WebviewWindow window)
    {
        _attachedWindow = window;
    }

    /// <inheritdoc />
    public void WindowOffset(int offset)
    {
        _windowOffset = offset;
    }

    /// <inheritdoc />
    public void WindowDebounce(int debounceMs)
    {
        _windowDebounceMs = debounceMs > 0 ? debounceMs : 0;
    }

    /// <inheritdoc />
    public void PositionWindow(WebviewWindow window, int offset)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        // 获取托盘所在屏幕的工作区，作为窗口定位参考。
        var screen = GetScreenForHwnd(_hwnd);
        if (screen is null)
        {
            return;
        }

        // 工作区为物理像素，需要换算为 DIP（窗口 API 使用 DIP）。
        var scaleFactor = GetWindowScaleFactor(_hwnd);
        if (scaleFactor <= 0f)
        {
            scaleFactor = 1f;
        }

        var windowBounds = window.GetBounds();

        // 默认将窗口定位到屏幕工作区右下角，并按 offset 偏移。
        var newX = screen.WorkAreaX + (int)(screen.WorkAreaWidth / scaleFactor) - windowBounds.Width - offset;
        var newY = screen.WorkAreaY + (int)(screen.WorkAreaHeight / scaleFactor) - windowBounds.Height - offset;

        // 若能拿到托盘图标位置，则将窗口中心对齐到图标位置。
        var trayBounds = GetBounds();
        if (trayBounds.HasValue)
        {
            var centerAlignX = trayBounds.Value.X + (trayBounds.Value.Width / 2) - (windowBounds.Width / 2);
            var centerAlignY = trayBounds.Value.Y + (trayBounds.Value.Height / 2) - (windowBounds.Height / 2);
            if (centerAlignX <= newX)
            {
                newX = centerAlignX;
            }
            if (centerAlignY <= newY)
            {
                newY = centerAlignY;
            }
        }

        window.SetPosition(newX, newY);
    }

    /// <inheritdoc />
    public void ToggleWindow()
    {
        if (_attachedWindow is null)
        {
            return;
        }

        if (_attachedWindow.Impl is null)
        {
            return;
        }

        // 首次点击时记录初始可见性状态。
        if (!_hasBeenShown)
        {
            _hasBeenShown = _attachedWindow.IsVisible();
        }

        if (_attachedWindow.IsVisible())
        {
            _attachedWindow.Hide();
        }
        else
        {
            _hasBeenShown = true;
            PositionWindow(_attachedWindow, _windowOffset);
            _attachedWindow.Show();
            _attachedWindow.Focus();
        }
    }

    /// <inheritdoc />
    public void ShowWindow()
    {
        if (_attachedWindow is null)
        {
            return;
        }

        _hasBeenShown = true;
        PositionWindow(_attachedWindow, _windowOffset);
        _attachedWindow.Show();
        _attachedWindow.Focus();
    }

    /// <inheritdoc />
    public void HideWindow()
    {
        _attachedWindow?.Hide();
    }

    /// <inheritdoc />
    public void ShowMenu()
    {
        OpenMenu();
    }

    /// <inheritdoc />
    public void OpenMenu()
    {
        if (_contextMenu is null || _hwnd.IsNull)
        {
            return;
        }

        // 优先使用托盘图标边界作为菜单弹出点。
        var bounds = GetBounds();
        if (bounds.HasValue)
        {
            _contextMenu.PopupAt(_hwnd, bounds.Value.X, bounds.Value.Y);
        }
        else
        {
            // 退回到当前鼠标位置。
            PInvoke.GetCursorPos(out var pt);
            _contextMenu.PopupAt(_hwnd, pt.X, pt.Y);
        }
    }

    /// <inheritdoc />
    public Rect? GetBounds()
    {
        if (_hwnd.IsNull)
        {
            return null;
        }

        var identifier = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = (IntPtr)_hwnd,
            uID = TrayIconId,
            guidItem = Guid.Empty,
        };

        if (!Shell_NotifyIconGetRect(ref identifier, out var rect))
        {
            return null;
        }

        return new Rect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
    }

    /// <summary>
    /// 获取指定窗口句柄所在屏幕的监视器信息。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <returns>屏幕信息；若获取失败返回 null。</returns>
    private static Screen? GetScreenForHwnd(HWND hwnd)
    {
        var monitor = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return null;
        }

        var info = new MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<MONITORINFO>(),
        };

        if (!PInvoke.GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var rc = info.rcMonitor;
        var work = info.rcWork;

        return new Screen(
            name: $"Monitor {monitor}",
            x: rc.left,
            y: rc.top,
            width: rc.right - rc.left,
            height: rc.bottom - rc.top,
            workAreaX: work.left,
            workAreaY: work.top,
            workAreaWidth: work.right - work.left,
            workAreaHeight: work.bottom - work.top,
            scaleFactor: GetWindowScaleFactor(hwnd),
            isPrimary: (info.dwFlags & 1u) != 0);
    }

    /// <summary>
    /// 获取窗口的 DPI 缩放因子（96 = 1.0）。
    /// </summary>
    /// <param name="hwnd">窗口句柄。</param>
    /// <returns>缩放因子（96 / DPI）。</returns>
    private static float GetWindowScaleFactor(HWND hwnd)
    {
        var dpi = PInvoke.GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            return 1f;
        }

        return dpi / 96f;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Destroy();
    }

    /// <summary>
    /// Shell_NotifyIconW P/Invoke 声明。
    /// CsWin32 无法为 AnyCPU 生成此架构相关 API，此处手动声明。
    /// </summary>
    /// <param name="dwMessage">操作类型（NIM_ADD/MODIFY/DELETE）。</param>
    /// <param name="lpData">NOTIFYICONDATAW 结构引用。</param>
    /// <returns>成功返回 true，否则 false。</returns>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    /// <summary>
    /// NOTIFYICONDATAW 结构，用于 Shell_NotifyIconW。
    /// 因 CsWin32 无法为 AnyCPU 生成，此处手动定义完整布局。
    /// 对应 Win32 NOTIFYICONDATAW 结构。
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        /// <summary>结构大小（字节）。</summary>
        public uint cbSize;

        /// <summary>接收回调消息的窗口句柄。</summary>
        public IntPtr hWnd;

        /// <summary>应用定义的托盘图标 ID。</summary>
        public uint uID;

        /// <summary>标志位（NIF_* 组合）。</summary>
        public uint uFlags;

        /// <summary>回调消息 ID。</summary>
        public uint uCallbackMessage;

        /// <summary>托盘图标句柄。</summary>
        public IntPtr hIcon;

        /// <summary>工具提示文本（最多 128 字符）。</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = TipBufferLength)]
        public string szTip;

        /// <summary>图标状态（NIS_* 组合）。</summary>
        public uint dwState;

        /// <summary>状态掩码。</summary>
        public uint dwStateMask;

        /// <summary>气泡通知内容（最多 256 字符）。</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        /// <summary>超时或版本号。</summary>
        public uint uTimeoutOrVersion;

        /// <summary>气泡通知标题（最多 64 字符）。</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        /// <summary>气泡通知标志（NIIF_* 组合）。</summary>
        public uint dwInfoFlags;
    }

    /// <summary>
    /// Shell_NotifyIconGetRect P/Invoke 声明。
    /// 用于获取托盘图标的屏幕坐标边界。
    /// </summary>
    /// <param name="identifier">NOTIFYICONIDENTIFIER 引用，标识目标托盘图标。</param>
    /// <param name="iconLocation">输出 RECT，接收托盘图标边界。</param>
    /// <returns>成功返回 true，否则 false。</returns>
    [DllImport("shell32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    /// <summary>
    /// NOTIFYICONIDENTIFIER 结构，用于 Shell_NotifyIconGetRect 标识托盘图标。
    /// 对应 Win32 NOTIFYICONIDENTIFIER 结构。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        /// <summary>结构大小（字节）。</summary>
        public uint cbSize;

        /// <summary>接收回调消息的窗口句柄。</summary>
        public IntPtr hWnd;

        /// <summary>应用定义的托盘图标 ID。</summary>
        public uint uID;

        /// <summary>图标 GUID（未使用 GUID 注册时为 Guid.Empty）。</summary>
        public Guid guidItem;
    }
}
