using Wails.Net.Application.Menus;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Menu = Wails.Net.Application.Menus.Menu;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Win32 上下文菜单助手，封装弹出式菜单的创建和显示。
/// 对应 Go 版 webview_window_windows.go 中的上下文菜单弹出逻辑。
/// 使用 CreatePopupMenu + AppendMenuW 构建原生弹出菜单，
/// 通过 TrackPopupMenu 在指定坐标显示并分发命令回调。
/// </summary>
public sealed class Win32ContextMenu : IDisposable
{
    /// <summary>
    /// 内部 Win32Menu 实例（弹出模式），负责构建原生菜单和命令分发。
    /// </summary>
    private readonly Win32Menu _menu;

    /// <summary>
    /// 是否已释放。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 构造 Win32ContextMenu 实例，根据 Menu 数据构建弹出式原生菜单。
    /// </summary>
    /// <param name="menu">源菜单数据。</param>
    public Win32ContextMenu(Menu menu)
    {
        _menu = new Win32Menu(menu, isPopup: true);
        _menu.Build();
    }

    /// <summary>
    /// 在指定窗口的指定坐标弹出上下文菜单。
    /// 使用 TrackPopupMenu 的 TPM_RETURNCMD | TPM_NONOTIFY 模式，
    /// 直接返回选中的命令 ID 并通过 Win32Menu.TryDispatchCommand 分发回调。
    /// </summary>
    /// <param name="owner">拥有弹出菜单的窗口句柄。</param>
    /// <param name="x">屏幕 X 坐标。</param>
    /// <param name="y">屏幕 Y 坐标。</param>
    internal unsafe void PopupAt(HWND owner, int x, int y)
    {
        if (_disposed || owner.IsNull)
        {
            return;
        }

        // 弹出菜单前必须将所有者窗口设为前台，否则菜单可能无法正常关闭。
        PInvoke.SetForegroundWindow(owner);

        var cmd = PInvoke.TrackPopupMenu(
            _menu.Hmenu,
            TRACK_POPUP_MENU_FLAGS.TPM_LEFTALIGN | TRACK_POPUP_MENU_FLAGS.TPM_TOPALIGN |
            TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY,
            x, y, 0, owner, null);

        // TrackPopupMenu 返回 BOOL，但使用 TPM_RETURNCMD 时实际返回命令 ID（int）。
        // 通过指针重新解释获取 int 值。
        var cmdValue = *(int*)&cmd;
        if (cmdValue != 0)
        {
            Win32Menu.TryDispatchCommand((uint)cmdValue);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _menu.Destroy();
        }
    }
}
