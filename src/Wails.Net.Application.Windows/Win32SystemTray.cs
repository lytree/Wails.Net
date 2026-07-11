using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Menu = Wails.Net.Application.Menus.Menu;

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
    /// 是否已释放。
    /// </summary>
    private bool _disposed;

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
                tray.ShowContextMenu();
                result = (LRESULT)1;
                return true;
            case WmLButtonUp:
                // 左键点击：未来可触发自定义回调。
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
}
