using Gdk;
using Gtk;
using Wails.Net.Application.Menus;
using Wails.Net.Application.SystemTray;
using Menu = Wails.Net.Application.Menus.Menu;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 平台系统托盘实现。
/// 对应 Go 版 application_linux.go 中的 SystemTray。
/// GTK4 移除了 Gtk.StatusIcon，本实现通过隐藏的 GtkWindow 模拟托盘行为：
/// 创建无边框小窗口作为托盘容器，通过图标和弹出菜单提供交互。
/// 使用 GestureClick 事件控制器处理鼠标点击事件：
/// 左键点击触发窗口呈现（模拟托盘点击），右键点击弹出上下文菜单。
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
    /// 模拟托盘的隐藏窗口实例。
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
    /// 构造 LinuxSystemTray 实例并创建模拟托盘窗口。
    /// </summary>
    public LinuxSystemTray()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // 创建无边框隐藏窗口作为托盘容器。
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
            // 左键点击：呈现窗口，模拟托盘点击回调。
            _trayWindow.Present();
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
