using Gtk;
using Wails.Net.Application.Menus;
using Menu = Wails.Net.Application.Menus.Menu;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 平台菜单实现，基于 GMenu 模型与 Gtk.PopoverMenuBar。
/// 对应 Go 版 application_linux_gtk3.go 中的菜单构建逻辑。
/// GTK4 使用 GMenu 模型驱动菜单，通过 PopoverMenuBar 渲染应用菜单栏。
/// 每个叶子菜单项对应一个 Gio.SimpleAction，action 名为 item{ID}，
/// 通过 GMenu 的 app.item{ID} 动作引用关联，激活时调用对应的回调。
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
    /// 已创建的 SimpleAction 字典，按菜单项 ID 索引，用于实时更新 action 状态。
    /// </summary>
    private readonly Dictionary<uint, Gio.SimpleAction> _actions = new();

    /// <summary>
    /// 包含所有菜单项 action 的 SimpleActionGroup。
    /// 调用方通过 InsertActionGroup("app", ActionGroup) 将其添加到窗口或应用，
    /// 使 GMenu 中的 app.item{ID} 引用能够解析到对应的 action。
    /// </summary>
    private Gio.SimpleActionGroup? _actionGroup;

    /// <summary>
    /// 构造 LinuxMenu 实例并构建 GMenu 模型与 action group。
    /// </summary>
    /// <param name="menu">源菜单实例。</param>
    public LinuxMenu(Menu menu)
    {
        _menu = menu;
        RebuildModel();
    }

    /// <summary>
    /// 获取包含所有菜单项 action 的 ActionGroup。
    /// 调用方应通过 Gtk.Widget.InsertActionGroup("app", ActionGroup) 将其添加到窗口或应用。
    /// </summary>
    public Gio.SimpleActionGroup? ActionGroup => _actionGroup;

    /// <summary>
    /// 获取 GMenu 模型实例，用于驱动 PopoverMenuBar 或应用菜单栏。
    /// 模型中的菜单项通过 app.item{ID} 引用关联到 ActionGroup 中的 SimpleAction。
    /// </summary>
    public Gio.Menu? MenuModel => _menuModel;

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
        _actions.Clear();
        _actionGroup = null;
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
        // 更新菜单项状态并实时更新对应 action 的启用状态。
        _menu.IsDisabled = !enabled;
        if (_menu is MenuItem menuItem && _actions.TryGetValue(menuItem.ID, out var action))
        {
            action.SetEnabled(enabled);
        }
    }

    /// <inheritdoc />
    public void SetChecked(bool @checked)
    {
        // 更新菜单项状态并实时更新对应 action 的状态。
        _menu.Checked = @checked;
        if (_menu is MenuItem menuItem && _actions.TryGetValue(menuItem.ID, out var action))
        {
            action.SetState(GLib.Variant.NewBoolean(@checked));
        }
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
    /// 重建 GMenu 模型与 action group，遍历菜单项并附加到新模型。
    /// 为每个叶子菜单项创建 SimpleAction 并连接 OnActivate 回调。
    /// </summary>
    private void RebuildModel()
    {
        _actions.Clear();
        _actionGroup = Gio.SimpleActionGroup.New();

        _menuModel = Gio.Menu.New();
        BuildMenuModel(_menuModel, _menu, _actionGroup);

        // 若已存在 menuBar，更新其模型。
        if (_menuBar is not null)
        {
            _menuBar.SetMenuModel(_menuModel);
        }
    }

    /// <summary>
    /// 递归构建 GMenu 模型，同时为每个叶子菜单项创建 SimpleAction。
    /// action 名为 item{ID}，与 GMenu 的 app.item{ID} 引用匹配。
    /// </summary>
    /// <param name="model">目标 GMenu 模型。</param>
    /// <param name="menu">源菜单实例。</param>
    /// <param name="actionGroup">目标 action group，用于添加创建的 SimpleAction。</param>
    private void BuildMenuModel(Gio.Menu model, Menu menu, Gio.SimpleActionGroup actionGroup)
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
                BuildMenuModel(submenu, item, actionGroup);
                model.AppendSubmenu(label, submenu);
            }
            else
            {
                var actionName = $"item{item.ID}";
                model.Append(label, $"app.{actionName}");

                // 为每个叶子菜单项创建 SimpleAction 并连接回调。
                // 复选框和单选框使用带 boolean 状态的 stateful action。
                Gio.SimpleAction action;
                if (item.IsCheckbox || item.IsRadio)
                {
                    action = Gio.SimpleAction.NewStateful(actionName, null, GLib.Variant.NewBoolean(item.Checked));
                }
                else
                {
                    action = Gio.SimpleAction.New(actionName, null);
                }

                action.SetEnabled(!item.IsDisabled);

                // 连接 OnActivate 事件到回调，优先从 _callbacks 查找，其次用 item.Callback。
                if (_callbacks.TryGetValue(item.ID, out var callback) && callback is not null)
                {
                    var cb = callback;
                    action.OnActivate += (_, _) => cb();
                }
                else if (item.Callback is not null)
                {
                    var cb = item.Callback;
                    action.OnActivate += (_, _) => cb();
                }

                actionGroup.AddAction(action);
                _actions[item.ID] = action;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Destroy();
    }
}
