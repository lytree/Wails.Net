using Wails.Net.Application.Menus;
using Wails.Net.Application.SystemTray;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Platform;

/// <summary>
/// macOS 系统托盘实现，基于 NSStatusItem。
/// 对应 Wails v3 Go 版本 systemtray_darwin.go。
/// <para>
/// 左键/右键/双击事件通过 <see cref="AppKit.NSStatusBarButton.Activated"/> 事件分发，
/// 依据 <see cref="AppKit.NSApplication.SharedApplication.CurrentEvent"/> 判断按键与点击次数。
/// 托盘菜单复用 <see cref="MacOSMenu"/>（NSMenu）构建。
/// </para>
/// <para>
/// 非 macOS 目标（<c>#if !MACOS</c>）保留 no-op 骨架保证任意宿主编译。
/// </para>
/// </summary>
public sealed class MacOSSystemTray : ISystemTrayImpl
{
    /// <summary>
    /// 托盘实例 ID。
    /// </summary>
    private readonly uint _id;

    /// <summary>
    /// 托盘标签文本。
    /// </summary>
    private string _label = string.Empty;

    /// <summary>
    /// 托盘图标字节数据。
    /// </summary>
    private byte[]? _icon;

    /// <summary>
    /// 是否模板图标（自动适配明暗模式）。
    /// </summary>
    private bool _isTemplateIcon;

#if MACOS
    /// <summary>
    /// 原生 NSStatusItem 实例。
    /// </summary>
    private AppKit.NSStatusItem? _statusItem;

    /// <summary>
    /// 原生菜单实例（由 MacOSMenu 构建）。
    /// </summary>
    private AppKit.NSMenu? _nativeMenu;
#endif

    /// <summary>
    /// 托盘菜单。
    /// </summary>
    private Menu? _menu;

    /// <summary>
    /// 构造 MacOSSystemTray 实例。
    /// </summary>
    /// <param name="id">托盘 ID。</param>
    public MacOSSystemTray(uint id)
    {
        _id = id;
    }

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

    /// <inheritdoc />
    public void SetIcon(byte[] iconData)
    {
        _icon = iconData;
#if MACOS
        if (_statusItem?.Button is null)
        {
            return;
        }

        using var data = Foundation.NSData.FromArray(iconData);
        var image = AppKit.NSImage.FromData(data);
        if (image is null)
        {
            return;
        }

        image.Size = new CoreGraphics.CGSize(18, 18);
        image.Template = _isTemplateIcon;
        _statusItem.Button.Image = image;
#endif
    }

    /// <inheritdoc />
    public void SetLabel(string label)
    {
        _label = label;
#if MACOS
        if (_statusItem?.Button is not null)
        {
            _statusItem.Button.Title = label;
        }
#endif
    }

    /// <inheritdoc />
    public void SetMenu(Menu? menu)
    {
        _menu = menu;
#if MACOS
        if (_statusItem is null)
        {
            return;
        }

        if (menu is null)
        {
            _statusItem.Menu = null;
            _nativeMenu = null;
            return;
        }

        // 复用 MacOSMenu 构建 NSMenu（整树重建）。
        var macMenu = new MacOSMenu(menu);
        _nativeMenu = macMenu.NativeMenu;
        _statusItem.Menu = _nativeMenu;
#endif
    }

    /// <inheritdoc />
    public void Show()
    {
#if MACOS
        EnsureStatusItem();
        if (_statusItem is not null)
        {
            _statusItem.Visible = true;
        }
#endif
    }

    /// <inheritdoc />
    public void Hide()
    {
#if MACOS
        if (_statusItem is not null)
        {
            _statusItem.Visible = false;
        }
#endif
    }

    /// <inheritdoc />
    public void Destroy()
    {
#if MACOS
        if (_statusItem is not null)
        {
            AppKit.NSStatusBar.SystemStatusBar.RemoveStatusItem(_statusItem);
            _statusItem = null;
        }

        _nativeMenu = null;
#endif
    }

    /// <inheritdoc />
    public void SetTooltip(string tooltip)
    {
        // macOS NSStatusItem 不支持 tooltip，no-op。
    }

    /// <inheritdoc />
    public void SetDarkModeIcon(byte[] iconData)
    {
        // macOS 通过模板图标自动适配明暗模式；非模板图标时直接替换。
        SetIcon(iconData);
    }

    /// <inheritdoc />
    public void SetTemplateIcon(byte[] iconData)
    {
        _isTemplateIcon = true;
        SetIcon(iconData);
    }

#if MACOS
    /// <summary>
    /// 惰性创建 NSStatusItem 并挂接点击事件。
    /// </summary>
    private void EnsureStatusItem()
    {
        if (_statusItem is not null)
        {
            return;
        }

        var statusItem = AppKit.NSStatusBar.SystemStatusBar.CreateStatusItem(AppKit.NSStatusItemLength.Variable);
        statusItem.Button!.SendActionOn(
            AppKit.NSEventMask.LeftMouseDown | AppKit.NSEventMask.RightMouseDown);
        statusItem.Button.Activated += OnButtonActivated;

        _statusItem = statusItem;

        if (!string.IsNullOrEmpty(_label))
        {
            statusItem.Button.Title = _label;
        }

        if (_icon is not null)
        {
            SetIcon(_icon);
        }

        if (_menu is not null)
        {
            SetMenu(_menu);
        }
    }

    /// <summary>
    /// 托盘按钮激活事件处理器：依据当前事件区分左右键与单击/双击。
    /// </summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnButtonActivated(object? sender, EventArgs e)
    {
        var currentEvent = AppKit.NSApplication.SharedApplication.CurrentEvent;
        if (currentEvent is null)
        {
            OnTrayClick?.Invoke();
            return;
        }

        var isRight = currentEvent.Type is AppKit.NSEventType.RightMouseDown
            or AppKit.NSEventType.RightMouseUp
            || currentEvent.ButtonNumber == 1;
        var clickCount = currentEvent.ClickCount;

        if (isRight)
        {
            if (clickCount >= 2)
            {
                OnTrayRightDoubleClick?.Invoke();
            }
            else
            {
                OnTrayRightClick?.Invoke();
            }
        }
        else
        {
            if (clickCount >= 2)
            {
                OnTrayDoubleClick?.Invoke();
            }
            else
            {
                OnTrayClick?.Invoke();
            }
        }
    }
#endif
}
