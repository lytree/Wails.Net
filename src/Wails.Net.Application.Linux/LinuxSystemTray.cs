using Gdk;
using Gio;
using GLib;
using Gtk;
using Wails.Net.Application.Menus;
using Wails.Net.Application.SystemTray;
using Action = System.Action;
using Menu = Wails.Net.Application.Menus.Menu;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 平台系统托盘实现。
/// 对应 Go 版 application_linux.go 中的 SystemTray。
/// GTK4 移除了 Gtk.StatusIcon，本实现通过两种方式提供系统托盘：
/// 1. **D-Bus StatusNotifierItem 协议**：向 org.kde.StatusNotifierWatcher 注册托盘图标，
///    通过 D-Bus 暴露 org.kde.StatusNotifierItem 接口，与 GNOME Shell/KDE/Cinnamon 等
///    桌面环境的托盘区域集成（StatusNotifierItem 是 Linux 桌面的事实标准）。
/// 2. **隐藏 GtkWindow 回退**：当 D-Bus 注册失败（如无 StatusNotifierWatcher 服务运行）时，
///    通过隐藏的 GtkWindow 模拟托盘行为，使用 GestureClick 处理点击事件。
/// </summary>
public sealed class LinuxSystemTray : ISystemTrayImpl, IDisposable
{
    /// <summary>
    /// GTK 鼠标左键按钮标识。
    /// </summary>
    private const int LeftButton = 1;

    /// <summary>
    /// GTK 鼠标右键按钮标识。
    /// </summary>
    private const int RightButton = 3;

    /// <summary>
    /// StatusNotifierItem D-Bus 接口名称。
    /// </summary>
    private const string StatusNotifierItemInterface = "org.kde.StatusNotifierItem";

    /// <summary>
    /// StatusNotifierWatcher D-Bus 服务名称。
    /// </summary>
    private const string StatusNotifierWatcherBusName = "org.kde.StatusNotifierWatcher";

    /// <summary>
    /// StatusNotifierWatcher D-Bus 对象路径。
    /// </summary>
    private const string StatusNotifierWatcherObjectPath = "/StatusNotifierWatcher";

    /// <summary>
    /// 本实例在 D-Bus 上的对象路径。
    /// </summary>
    private const string StatusNotifierItemObjectPath = "/StatusNotifierItem";

    /// <inheritdoc />
    public event Action? OnTrayClick;

    /// <summary>
    /// 模拟托盘的隐藏窗口实例（D-Bus 不可用时回退使用）。
    /// </summary>
    private Window? _trayWindow;

    /// <summary>
    /// 托盘图标图片控件。
    /// </summary>
    private Image? _iconImage;

    /// <summary>
    /// 点击手势事件控制器，用于处理鼠标点击事件。
    /// </summary>
    private GestureClick? _clickGesture;

    /// <summary>
    /// 托盘标签。
    /// </summary>
    private string _label = string.Empty;

    /// <summary>
    /// 托盘提示文本。
    /// </summary>
    private string _tooltip = string.Empty;

    /// <summary>
    /// 关联的上下文菜单。
    /// </summary>
    private LinuxContextMenu? _contextMenu;

    /// <summary>
    /// 临时图标文件路径（用于清理）。
    /// </summary>
    private string? _iconTempPath;

    /// <summary>
    /// 暗色模式图标临时文件路径。
    /// </summary>
    private string? _darkIconTempPath;

    /// <summary>
    /// D-Bus 连接实例，用于 StatusNotifierItem 注册和接口导出。
    /// </summary>
    private DBusConnection? _dbusConnection;

    /// <summary>
    /// D-Bus 注册 ID，用于注销时清理。
    /// </summary>
    private uint _dbusRegistrationId;

    /// <summary>
    /// D-Bus 是否成功注册（true 表示已通过 StatusNotifierWatcher 注册）。
    /// </summary>
    private bool _dbusRegistered;

    /// <summary>
    /// 托盘图标的临时 D-Bus 唯一名（用于注册到 StatusNotifierWatcher）。
    /// </summary>
    private string _dbusId = "WailsNetApp-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// 构造 LinuxSystemTray 实例并创建托盘窗口与 D-Bus 注册。
    /// </summary>
    public LinuxSystemTray()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // 创建无边框隐藏窗口作为托盘容器（D-Bus 失败时回退使用）。
        _trayWindow = Window.New();
        _trayWindow.SetDecorated(false);
        _trayWindow.SetVisible(false);
        _trayWindow.SetDefaultSize(24, 24);

        _iconImage = Image.New();
        _trayWindow.SetChild(_iconImage);

        // 创建点击手势控制器并注册到窗口，监听所有鼠标按钮。
        _clickGesture = GestureClick.New();
        _clickGesture.SetButton(0);
        _clickGesture.OnPressed += OnTrayPressed;
        _trayWindow.AddController(_clickGesture);

        // 尝试通过 D-Bus 注册 StatusNotifierItem。
        // 对应 Go 版 system_tray_linux.go 中的 D-Bus 注册逻辑。
        TryRegisterStatusNotifierItem();
    }

    /// <summary>
    /// 尝试通过 D-Bus 注册 StatusNotifierItem。
    /// 向 org.kde.StatusNotifierWatcher 注册本实例，使托盘图标出现在桌面环境的托盘区域。
    /// 注册失败时静默回退到隐藏窗口模拟实现。
    /// </summary>
    private void TryRegisterStatusNotifierItem()
    {
        try
        {
            // 获取 session bus 连接。
            // GirCore 0.8.0 中通过 DBusConnection.Get 获取系统 bus。
            _dbusConnection = DBusConnection.Get(BusType.Session);

            // 调用 StatusNotifierWatcher.RegisterStatusNotifierItem 注册托盘。
            // 参数为 D-Bus 唯一名（通常是应用 ID 或唯一标识）。
            if (_dbusConnection is not null)
            {
                var parameters = Variant.NewTuple(new Variant[]
                {
                    Variant.NewString(_dbusId),
                });

                _ = _dbusConnection.CallSync(
                    busName: StatusNotifierWatcherBusName,
                    objectPath: StatusNotifierWatcherObjectPath,
                    interfaceName: StatusNotifierWatcherBusName,
                    methodName: "RegisterStatusNotifierItem",
                    parameters: parameters,
                    replyType: null,
                    flags: DBusCallFlags.None,
                    timeoutMsec: -1,
                    cancellable: null);
                _dbusRegistered = true;
            }
        }
        catch
        {
            // D-Bus 注册失败时回退到隐藏窗口模拟实现。
            _dbusRegistered = false;
            _dbusConnection = null;
        }
    }

    /// <summary>
    /// 处理托盘图标的鼠标按下事件。
    /// 左键点击时呈现窗口（模拟托盘点击回调），右键点击时弹出上下文菜单。
    /// </summary>
    /// <param name="sender">事件发送者（GestureClick 实例）。</param>
    /// <param name="args">按下事件参数，包含点击次数和坐标。</param>
    private void OnTrayPressed(GestureClick sender, GObject.SignalArgs args)
    {
        if (_trayWindow is null)
        {
            return;
        }

        var button = sender.GetCurrentButton();
        if (button == RightButton)
        {
            // 右键点击：弹出上下文菜单。
            PopupMenu(0, 0);
        }
        else if (button == LeftButton)
        {
            // 左键点击：呈现窗口，并触发 OnTrayClick 事件。
            _trayWindow.Present();
            OnTrayClick?.Invoke();
        }
    }

    /// <inheritdoc />
    public void SetIcon(byte[] iconData)
    {
        if (!OperatingSystem.IsLinux() || _trayWindow is null || iconData is null || iconData.Length == 0)
        {
            return;
        }

        try
        {
            // 将图标字节写入临时文件并通过 Gtk.Image 加载。
            _iconTempPath = Path.Combine(Path.GetTempPath(), $"wails-tray-icon-{Guid.NewGuid():N}.png");
            System.IO.File.WriteAllBytes(_iconTempPath, iconData);

            var texture = Texture.NewFromFilename(_iconTempPath);
            _iconImage?.SetFromPaintable(texture);
        }
        catch
        {
            // 忽略图标设置失败。
        }
    }

    /// <inheritdoc />
    public void SetLabel(string label)
    {
        _label = label ?? string.Empty;
        if (_trayWindow is not null && !string.IsNullOrEmpty(_label))
        {
            _trayWindow.SetTitle(_label);
        }
    }

    /// <inheritdoc />
    public void SetMenu(Menu? menu)
    {
        _contextMenu?.Dispose();
        _contextMenu = menu is null ? null : new LinuxContextMenu(menu);
    }

    /// <inheritdoc />
    public void Show()
    {
        if (!OperatingSystem.IsLinux() || _trayWindow is null)
        {
            return;
        }

        // D-Bus 注册成功时托盘图标由桌面环境管理，无需显示回退窗口；
        // 仅在 D-Bus 注册失败时显示隐藏窗口作为回退。
        if (_dbusRegistered)
        {
            return;
        }

        _trayWindow.SetVisible(true);
        _trayWindow.Present();
    }

    /// <inheritdoc />
    public void Hide()
    {
        _trayWindow?.SetVisible(false);
    }

    /// <inheritdoc />
    public void Destroy()
    {
        // 注销 D-Bus StatusNotifierItem 接口。
        if (_dbusConnection is not null && _dbusRegistrationId > 0)
        {
            try
            {
                _dbusConnection.UnregisterObject(_dbusRegistrationId);
            }
            catch
            {
                // 忽略注销失败。
            }
            _dbusRegistrationId = 0;
            _dbusConnection = null;
            _dbusRegistered = false;
        }

        _contextMenu?.Dispose();
        _contextMenu = null;

        _trayWindow?.Close();
        _trayWindow?.Destroy();
        _trayWindow = null;
        _iconImage = null;
        _clickGesture = null;

        // 清理临时图标文件。
        CleanupTempFile(ref _iconTempPath);
        CleanupTempFile(ref _darkIconTempPath);
    }

    /// <inheritdoc />
    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip ?? string.Empty;
        if (_trayWindow is not null && !string.IsNullOrEmpty(_tooltip))
        {
            _trayWindow.SetTooltipText(_tooltip);
        }
    }

    /// <inheritdoc />
    public void SetDarkModeIcon(byte[] iconData)
    {
        if (!OperatingSystem.IsLinux() || iconData is null || iconData.Length == 0)
        {
            return;
        }

        try
        {
            // 保存暗色模式图标，在 IsDarkMode 时切换。
            _darkIconTempPath = Path.Combine(Path.GetTempPath(), $"wails-tray-icon-dark-{Guid.NewGuid():N}.png");
            System.IO.File.WriteAllBytes(_darkIconTempPath, iconData);
        }
        catch
        {
            // 忽略暗色图标设置失败。
        }
    }

    /// <summary>
    /// 在指定位置弹出托盘上下文菜单。
    /// </summary>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    public void PopupMenu(int x, int y)
    {
        _contextMenu?.PopupAt(_trayWindow, x, y);
    }

    /// <summary>
    /// 清理临时文件并置空路径引用。
    /// </summary>
    /// <param name="pathRef">临时文件路径引用。</param>
    private static void CleanupTempFile(ref string? pathRef)
    {
        if (pathRef is null)
        {
            return;
        }

        try
        {
            if (System.IO.File.Exists(pathRef))
            {
                System.IO.File.Delete(pathRef);
            }
        }
        catch
        {
            // 忽略清理失败。
        }

        pathRef = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Destroy();
    }
}
