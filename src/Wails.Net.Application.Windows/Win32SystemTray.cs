using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Windows;
using Wails.Net.Events;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using Menu = Wails.Net.Application.Menus.Menu;
using Rect = Wails.Net.Application.Screens.Rect;
using Screen = Wails.Net.Application.Screens.Screen;
using WailsApplication = Wails.Net.Application.Application;

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
    /// <c>TaskbarCreated</c> 消息 ID（由 <c>RegisterWindowMessage</c> 分配的动态消息）。
    /// 0 表示尚未注册。
    /// 对应 Wails v3 <c>v3.1.0</c> PR #6386：Explorer 重启后系统广播该消息，托盘需重新注册图标。
    /// </summary>
    private static uint s_taskbarCreatedMessage;

    /// <summary>
    /// 获取 <c>TaskbarCreated</c> 消息 ID；首次访问时注册（幂等）。
    /// </summary>
    internal static uint TaskbarCreatedMessage
    {
        get
        {
            if (s_taskbarCreatedMessage == 0)
            {
                s_taskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");
            }

            return s_taskbarCreatedMessage;
        }
    }

    /// <summary>
    /// 托盘图标重建去抖调度器（#6386），首次需要时创建。
    /// </summary>
    private TrayIconRebuildScheduler? _rebuildScheduler;

    /// <summary>
    /// 托盘消息窗口句柄。
    /// </summary>
    private HWND _hwnd;

    /// <summary>
    /// 亮色模式图标句柄。
    /// 对应 Go systemtray_windows.go 中 windowsSystemTray.lightModeIcon。
    /// </summary>
    private HICON _lightModeIcon;

    /// <summary>
    /// 亮色模式图标句柄是否由本实例创建（拥有所有权，可 DestroyIcon）。
    /// 共享句柄（如系统默认图标）为 false，不得销毁。
    /// 对应 Go systemtray_windows.go 中 windowsSystemTray.lightModeIconOwned。
    /// </summary>
    private bool _lightModeIconOwned;

    /// <summary>
    /// 暗色模式图标句柄。
    /// 对应 Go systemtray_windows.go 中 windowsSystemTray.darkModeIcon。
    /// </summary>
    private HICON _darkModeIcon;

    /// <summary>
    /// 暗色模式图标句柄是否由本实例创建（拥有所有权，可 DestroyIcon）。
    /// 对应 Go systemtray_windows.go 中 windowsSystemTray.darkModeIconOwned。
    /// </summary>
    private bool _darkModeIconOwned;

    /// <summary>
    /// 无主题监听器标记。对应 Go systemtray_windows.go 中 <c>cancelTheme</c> 为 nil 的状态。
    /// </summary>
    private const int NoThemeListener = -1;

    /// <summary>
    /// 主题变化监听器 ID，用于销毁时取消订阅。
    /// 对应 Go systemtray_windows.go 中 windowsSystemTray.cancelTheme。
    /// </summary>
    private int _themeListenerId = NoThemeListener;

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
        SubscribeThemeChanged();
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

        var newIcon = WindowsPlatformApp.LoadIconFromBytes(iconData);
        if (newIcon.IsNull)
        {
            // 加载失败：保持当前图标不变。
            return;
        }

        var oldLight = _lightModeIcon;
        var oldLightOwned = _lightModeIconOwned;

        _lightModeIcon = newIcon;
        _lightModeIconOwned = true;

        // 暗色图标未设置或与旧亮色图标共享句柄时，同步跟随新的亮色图标（不再独立拥有）。
        // 对应 Go systemtray_windows.go setIcon 中 darkModeIcon 的同步逻辑。
        if (_darkModeIcon.IsNull || SameHandle(_darkModeIcon, oldLight))
        {
            _darkModeIcon = newIcon;
            _darkModeIconOwned = false;
        }

        // 先让 shell 切换到新的生效图标（可见时），再释放旧句柄，避免句柄被提前销毁。
        ApplyEffectiveIcon();

        // 仅释放本实例拥有且不再被双图标字段引用的旧句柄。
        ReleaseIcon(oldLight, oldLightOwned, _lightModeIcon, _darkModeIcon);
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

        // 应用可能在本实例构造后才就绪，此处兜底补订阅（幂等，已订阅时为 no-op）。
        SubscribeThemeChanged();

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

        // 先取消尚未触发的 Explorer 重启重建，避免销毁后再次 NIM_ADD。
        _rebuildScheduler?.Dispose();
        _rebuildScheduler = null;

        // 先取消主题订阅，避免销毁期间回调仍触发 ModifyTrayIcon。
        // 对应 Go systemtray_windows.go destroy 中的 cancelTheme。
        UnsubscribeThemeChanged();

        DeleteTrayIcon();

        _contextMenu?.Dispose();
        _contextMenu = null;

        // 释放双图标句柄：仅销毁本实例拥有的句柄（共享句柄只释放一次）。
        // 对应 Go systemtray_windows.go destroy 中的图标句柄清理。
        var lightIcon = _lightModeIcon;
        var lightIconOwned = _lightModeIconOwned;
        var darkIcon = _darkModeIcon;
        var darkIconOwned = _darkModeIconOwned;

        _lightModeIcon = default;
        _lightModeIconOwned = false;
        _darkModeIcon = default;
        _darkModeIconOwned = false;

        ReleaseIcon(lightIcon, lightIconOwned);
        ReleaseIcon(darkIcon, darkIconOwned, lightIcon);

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
        if (iconData is null || iconData.Length == 0)
        {
            return;
        }

        var newIcon = WindowsPlatformApp.LoadIconFromBytes(iconData);
        if (newIcon.IsNull)
        {
            // 加载失败：保持当前图标不变。
            return;
        }

        var oldDark = _darkModeIcon;
        var oldDarkOwned = _darkModeIconOwned;
        var oldLight = _lightModeIcon;
        var oldLightOwned = _lightModeIconOwned;

        _darkModeIcon = newIcon;
        _darkModeIconOwned = true;

        var lightReplaced = false;

        // 亮色图标未设置或与旧暗色图标共享句柄时，同步跟随新的暗色图标（不再独立拥有）。
        // 对应 Go systemtray_windows.go setDarkModeIcon 中 lightModeIcon 的同步逻辑。
        if (_lightModeIcon.IsNull || SameHandle(_lightModeIcon, oldDark))
        {
            _lightModeIcon = newIcon;
            _lightModeIconOwned = false;
            lightReplaced = true;
        }

        // 先让 shell 切换到新的生效图标（可见时），再释放旧句柄。
        ApplyEffectiveIcon();

        // 仅释放本实例拥有且不再被双图标字段引用的旧句柄。
        ReleaseIcon(oldDark, oldDarkOwned, _lightModeIcon, _darkModeIcon);
        if (lightReplaced)
        {
            ReleaseIcon(oldLight, oldLightOwned, _lightModeIcon, _darkModeIcon);
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
    /// 系统广播 <c>TaskbarCreated</c>（Explorer 重启）通知所有托盘实例：去抖 1 秒后重新注册图标。
    /// <para>
    /// 由顶层窗口 <see cref="PlatformMessageWindow"/> 的消息过程调用：托盘的仅消息窗口
    /// 收不到 <c>HWND_BROADCAST</c>，必须由顶层窗口接收后再分发到各托盘实例。
    /// </para>
    /// 对应 Wails v3 <c>v3.1.0</c> PR #6386。
    /// </summary>
    internal static void NotifyTaskbarCreated()
    {
        foreach (var tray in s_traysByHwnd.Values)
        {
            tray.ScheduleTaskbarRebuild();
        }
    }

    /// <summary>
    /// 请求一次托盘图标重建（去抖合并）。仅在托盘可见且未销毁时生效。
    /// </summary>
    private void ScheduleTaskbarRebuild()
    {
        if (_disposed || _hwnd.IsNull || !_visible)
        {
            return;
        }

        _rebuildScheduler ??= new TrayIconRebuildScheduler(RebuildTrayIconAfterExplorerRestart);
        _rebuildScheduler.Request();
    }

    /// <summary>
    /// Explorer 重启后重新注册托盘图标。
    /// <para>
    /// 在去抖计时器线程执行；<c>Shell_NotifyIconW</c> 通过消息窗口句柄向窗口线程投递回调，
    /// 因此跨线程调用安全。重建前再次校验可见性，避免销毁竞态。
    /// </para>
    /// </summary>
    private void RebuildTrayIconAfterExplorerRestart()
    {
        if (_disposed || _hwnd.IsNull || !_visible)
        {
            return;
        }

        AddTrayIcon();
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
    /// hIcon 使用当前主题对应的「生效图标」，而非固定句柄。
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
            hIcon = (IntPtr)GetEffectiveIcon(),
            szTip = _tooltip ?? string.Empty,
        };

        return data;
    }

    /// <summary>
    /// 选择当前主题对应的生效图标。
    /// 依据 <c>Application.IsDarkMode()</c>：暗色模式优先取暗色图标，
    /// 缺失时回退亮色图标；亮色模式反之。两者都缺失时返回 NULL（保持现状，不崩溃）。
    /// 对应 Go systemtray_windows.go updateIcon 中按 IsSystemCurrentlyDarkMode 选取图标。
    /// </summary>
    /// <returns>生效图标句柄；无可用图标时返回 NULL。</returns>
    private HICON GetEffectiveIcon()
    {
        var dark = WailsApplication.Get()?.IsDarkMode() ?? false;

        if (dark)
        {
            if (!_darkModeIcon.IsNull)
            {
                return _darkModeIcon;
            }

            return _lightModeIcon;
        }

        if (!_lightModeIcon.IsNull)
        {
            return _lightModeIcon;
        }

        return _darkModeIcon;
    }

    /// <summary>
    /// 应用当前主题对应的生效图标：仅在托盘可见时向 shell 发送 NIM_MODIFY。
    /// 不可见时不操作，待 Show 时以最新图标注册。
    /// 对应 Go systemtray_windows.go updateIcon（内部仅在图标变化时发送 NIM_MODIFY）。
    /// </summary>
    private void ApplyEffectiveIcon()
    {
        if (_visible)
        {
            ModifyTrayIcon();
        }
    }

    /// <summary>
    /// 判断两个图标句柄是否指向同一句柄。
    /// </summary>
    /// <param name="a">图标句柄 a。</param>
    /// <param name="b">图标句柄 b。</param>
    /// <returns>相同返回 true。</returns>
    private static bool SameHandle(HICON a, HICON b)
    {
        return (IntPtr)a == (IntPtr)b;
    }

    /// <summary>
    /// 释放图标句柄：仅当本实例拥有该句柄、句柄非空且未被其它图标字段引用时才调用 DestroyIcon。
    /// 共享句柄（如 LoadIcon 返回的句柄）不得销毁。
    /// 对应 Go systemtray_windows.go releaseIcon。
    /// </summary>
    /// <param name="handle">待释放的图标句柄。</param>
    /// <param name="owned">是否由本实例拥有该句柄。</param>
    /// <param name="keep1">仍需保留引用的句柄 1（若与之相同则不释放）。</param>
    /// <param name="keep2">仍需保留引用的句柄 2（若与之相同则不释放）。</param>
    private static void ReleaseIcon(HICON handle, bool owned, HICON keep1 = default, HICON keep2 = default)
    {
        if (!owned || handle.IsNull)
        {
            return;
        }

        if (SameHandle(handle, keep1) || SameHandle(handle, keep2))
        {
            return;
        }

        PInvoke.DestroyIcon(handle);
    }

    /// <summary>
    /// 订阅系统主题变化事件，用于在明暗主题切换时更新托盘图标。
    /// 幂等：已订阅时直接返回，避免重复订阅。
    /// 对应 Go systemtray_windows.go run 中的 OnApplicationEvent(SystemThemeChanged)。
    /// </summary>
    private void SubscribeThemeChanged()
    {
        if (_themeListenerId != NoThemeListener)
        {
            return;
        }

        var events = WailsApplication.Get()?.Events;
        if (events is null)
        {
            // 应用尚未就绪时无法订阅；Show 时会重试。
            return;
        }

        _themeListenerId = events.On(KnownEvents.ThemeChanged, _ => ApplyEffectiveIcon());
    }

    /// <summary>
    /// 取消系统主题变化事件订阅。
    /// 对应 Go systemtray_windows.go destroy 中的 cancelTheme。
    /// </summary>
    private void UnsubscribeThemeChanged()
    {
        if (_themeListenerId == NoThemeListener)
        {
            return;
        }

        WailsApplication.Get()?.Events.Off(_themeListenerId);
        _themeListenerId = NoThemeListener;
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
