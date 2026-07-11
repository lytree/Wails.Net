using Gtk;
using Wails.Net.Application.Menus;
using Menu = Wails.Net.Application.Menus.Menu;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 平台上下文菜单助手，基于 Gtk.PopoverMenu 实现。
/// 对应 Go 版 webview_window_linux.go 中的上下文菜单弹出逻辑。
/// GTK4 移除了传统的 Gtk.Menu，改用 PopoverMenu 展示上下文菜单。
/// 每个叶子菜单项对应一个 Gio.SimpleAction，通过 InsertActionGroup 添加到父窗口，
/// 使 GMenu 的 app.item{ID} 引用能够解析并触发回调。
/// </summary>
public sealed class LinuxContextMenu : IDisposable
{
    /// <summary>
    /// 源菜单实例。
    /// </summary>
    private readonly Menu _menu;

    /// <summary>
    /// 当前弹出的 PopoverMenu 实例，弹出后持有引用以便关闭。
    /// </summary>
    private PopoverMenu? _popover;

    /// <summary>
    /// 上下文菜单的 action group，包含所有菜单项的 SimpleAction。
    /// 通过 InsertActionGroup("app", ...) 添加到父窗口，使 GMenu 引用生效。
    /// </summary>
    private Gio.SimpleActionGroup? _actionGroup;

    /// <summary>
    /// 构造 LinuxContextMenu 实例。
    /// </summary>
    /// <param name="menu">源菜单实例。</param>
    public LinuxContextMenu(Menu menu)
    {
        _menu = menu;
    }

    /// <summary>
    /// 在指定窗口的指定坐标弹出上下文菜单。
    /// 为每个叶子菜单项创建 SimpleAction 并通过 InsertActionGroup 添加到父窗口，
    /// 使 GMenu 的 app.item{ID} 引用能够解析到对应的 action。
    /// </summary>
    /// <param name="window">父窗口实例，可为 null。</param>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    public void PopupAt(Window? window, int x, int y)
    {
        if (!OperatingSystem.IsLinux() || window is null)
        {
            return;
        }

        // 关闭并释放前一个弹出的菜单。
        _popover?.Popdown();
        _popover = null;

        // 创建 action group 并为每个叶子菜单项创建 SimpleAction。
        _actionGroup = Gio.SimpleActionGroup.New();

        // 构建 GMenu 模型并填充 action group。
        var model = Gio.Menu.New();
        BuildMenuModel(model, _menu, _actionGroup);

        // 将 action group 添加到父窗口，使 GMenu 的 app.item{ID} 引用生效。
        // InsertActionGroup 会替换同名的 action group，无需先移除旧的。
        window.InsertActionGroup("app", _actionGroup);

        // 创建并定位 PopoverMenu。
        _popover = PopoverMenu.NewFromModel(model);
        _popover.SetParent(window);

        // 设置弹出位置偏移（GTK4 PopoverMenu 通过 SetOffset 定位）。
        _popover.SetOffset(x, y);

        _popover.Popup();
    }

    /// <summary>
    /// 递归构建 GMenu 模型，同时为每个叶子菜单项创建 SimpleAction 并连接回调。
    /// action 名为 item{ID}，与 GMenu 的 app.item{ID} 引用匹配。
    /// </summary>
    /// <param name="model">目标 GMenu 模型。</param>
    /// <param name="menu">源菜单实例。</param>
    /// <param name="actionGroup">目标 action group，用于添加创建的 SimpleAction。</param>
    private static void BuildMenuModel(Gio.Menu model, Menu menu, Gio.SimpleActionGroup actionGroup)
    {
        foreach (var item in menu.Items)
        {
            if (item.IsSeparator)
            {
                // 分隔符作为一个新的段。
                var section = Gio.Menu.New();
                model.AppendSection(null, section);
                continue;
            }

            var label = item.Label ?? string.Empty;

            if (item.Items.Count > 0)
            {
                // 包含子项则作为子菜单附加。
                var submenu = Gio.Menu.New();
                BuildMenuModel(submenu, item, actionGroup);
                model.AppendSubmenu(label, submenu);
            }
            else
            {
                // 普通菜单项，action 名以菜单项 ID 派生。
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

                // 连接 OnActivate 事件到回调。
                if (item.Callback is not null)
                {
                    var cb = item.Callback;
                    action.OnActivate += (_, _) => cb();
                }

                actionGroup.AddAction(action);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // GTK4 widget 无 Destroy 方法，通过 Popdown 关闭弹出并释放引用。
        _popover?.Popdown();
        _popover = null;
        _actionGroup = null;
    }
}
