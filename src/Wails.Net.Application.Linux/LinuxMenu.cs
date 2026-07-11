using Gtk;
using Wails.Net.Application.Menus;
using Menu = Wails.Net.Application.Menus.Menu;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 平台菜单实现，基于 GMenu 模型与 Gtk.PopoverMenuBar。
/// 对应 Go 版 application_linux_gtk3.go 中的菜单构建逻辑。
/// GTK4 使用 GMenu 模型驱动菜单，通过 PopoverMenuBar 渲染应用菜单栏。
/// </summary>
public sealed class LinuxMenu : IMenuImpl, IDisposable
{
    /// <summary>
    /// 关联的源菜单实例。
    /// </summary>
    private readonly Menu _menu;

    /// <summary>
    /// GMenu 模型实例，用于驱动 PopoverMenuBar。
    /// </summary>
    private Gio.Menu? _menuModel;

    /// <summary>
    /// PopoverMenuBar 实例，渲染应用菜单栏。
    /// </summary>
    private PopoverMenuBar? _menuBar;

    /// <summary>
    /// 菜单项回调字典，按菜单项 ID 索引，用于 action 激活时调用。
    /// </summary>
    private readonly Dictionary<uint, Action?> _callbacks = new();

    /// <summary>
    /// 构造 LinuxMenu 实例并构建 GMenu 模型。
    /// </summary>
    /// <param name="menu">源菜单实例。</param>
    public LinuxMenu(Menu menu)
    {
        _menu = menu;
        RebuildModel();
    }

    /// <inheritdoc />
    public void Show()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // 构建 PopoverMenuBar 并设置可见。
        if (_menuModel is not null && _menuBar is null)
        {
            _menuBar = PopoverMenuBar.NewFromModel(_menuModel);
        }

        _menuBar?.SetVisible(true);
    }

    /// <inheritdoc />
    public void Hide()
    {
        _menuBar?.SetVisible(false);
    }

    /// <inheritdoc />
    public void AddMenuItem(MenuItem item, int position)
    {
        // GMenu 不支持局部更新，整体重建模型。
        if (item.Callback is not null)
        {
            _callbacks[item.ID] = item.Callback;
        }

        RebuildModel();
    }

    /// <inheritdoc />
    public void RemoveMenuItem(MenuItem item)
    {
        _callbacks.Remove(item.ID);
        RebuildModel();
    }

    /// <inheritdoc />
    public void UpdateMenuItem(MenuItem item)
    {
        // 更新回调并重建模型。
        if (item.Callback is not null)
        {
            _callbacks[item.ID] = item.Callback;
        }

        RebuildModel();
    }

    /// <inheritdoc />
    public void AddSubmenu(Menu submenu, int position)
    {
        // 子菜单作为菜单项的一种，重建模型时自动包含。
        RebuildModel();
    }

    /// <inheritdoc />
    public void Destroy()
    {
        // GTK4 widget 无 Destroy 方法，通过 Unparent 解除关联并释放引用。
        _menuBar?.Unparent();
        _menuBar = null;
        _menuModel = null;
        _callbacks.Clear();
    }

    /// <inheritdoc />
    public void SetLabel(string label)
    {
        // GMenu 模型中标签在重建时更新。
        _menu.Label = label;
        RebuildModel();
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        // GTK4 中通过 action 的 enabled 属性控制，简化实现暂存状态。
        _menu.IsDisabled = !enabled;
    }

    /// <inheritdoc />
    public void SetChecked(bool @checked)
    {
        // 复选状态通过 GMenuItem attribute 控制，简化实现暂存状态。
        _menu.Checked = @checked;
    }

    /// <inheritdoc />
    public void SetAccelerator(string accelerator)
    {
        // 快捷键通过 GMenuItem attribute 设置，简化实现暂存状态。
        _menu.Accelerator = accelerator;
    }

    /// <inheritdoc />
    public void SetBitmap(byte[]? bitmap)
    {
        // GTK4 菜单项图标通过 Gio.Icon 设置，简化实现暂存状态。
        _menu.Bitmap = bitmap;
    }

    /// <summary>
    /// 重建 GMenu 模型，遍历菜单项并附加到新模型。
    /// </summary>
    private void RebuildModel()
    {
        _menuModel = Gio.Menu.New();
        BuildMenuModel(_menuModel, _menu);

        // 若已存在 menuBar，更新其模型。
        if (_menuBar is not null)
        {
            _menuBar.SetMenuModel(_menuModel);
        }
    }

    /// <summary>
    /// 递归构建 GMenu 模型。
    /// </summary>
    /// <param name="model">目标 GMenu 模型。</param>
    /// <param name="menu">源菜单实例。</param>
    private static void BuildMenuModel(Gio.Menu model, Menu menu)
    {
        foreach (var item in menu.Items)
        {
            if (item.IsSeparator)
            {
                var section = Gio.Menu.New();
                model.AppendSection(null, section);
                continue;
            }

            var label = item.Label ?? string.Empty;

            if (item.Items.Count > 0)
            {
                var submenu = Gio.Menu.New();
                BuildMenuModel(submenu, item);
                model.AppendSubmenu(label, submenu);
            }
            else
            {
                var actionName = $"app.item{item.ID}";
                model.Append(label, actionName);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Destroy();
    }
}
