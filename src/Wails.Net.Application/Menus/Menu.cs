using System.Text.Json.Serialization;

namespace Wails.Net.Application.Menus;

/// <summary>
/// 表示一个菜单，对应 Wails v3 中的 Menu。
/// <para>
/// 启用多态 JSON 序列化，确保派生类型（如 <see cref="ContextMenu"/>）在 JSON 往返后保留类型信息。
/// 这是 <c>menu.setContextMenu</c> 等命令正确反序列化为 <see cref="ContextMenu"/> 而非基类 <see cref="Menu"/> 的前提。
/// 默认配置下：未携带类型鉴别符时回退到基类 <see cref="Menu"/>（<see cref="JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor"/>）。
/// </para>
/// </summary>
[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(ContextMenu), "contextMenu")]
public class Menu
{
    /// <summary>
    /// 菜单标签文本。
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// 菜单位图数据。
    /// </summary>
    public byte[]? Bitmap { get; set; }

    /// <summary>
    /// 是否为子菜单。
    /// </summary>
    public bool IsSubMenu { get; set; }

    /// <summary>
    /// 是否为复选框菜单项。
    /// </summary>
    public bool IsCheckbox { get; set; }

    /// <summary>
    /// 是否为单选菜单项。
    /// </summary>
    public bool IsRadio { get; set; }

    /// <summary>
    /// 是否为分隔符。
    /// </summary>
    public bool IsSeparator { get; set; }

    /// <summary>
    /// 是否禁用。
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// 是否选中。
    /// </summary>
    public bool Checked { get; set; }

    /// <summary>
    /// 快捷键。
    /// </summary>
    public string? Accelerator { get; set; }

    /// <summary>
    /// 子菜单项列表。
    /// </summary>
    public List<MenuItem> Items { get; set; } = new();

    /// <summary>
    /// 平台实现实例。
    /// <para>
    /// 属于运行时状态，不参与 JSON 序列化（平台实现不可序列化）。
    /// </para>
    /// </summary>
    [JsonIgnore]
    public IMenuImpl? Impl { get; internal set; }

    /// <summary>
    /// 所属父菜单（容器）。顶层菜单为 null。
    /// <para>
    /// 叶子菜单项自身没有独立的原生句柄，其属性更新（label/enabled/checked/accelerator）
    /// 必须通过容器实现下发到平台。调用方应优先使用 <see cref="OwningImpl"/>，
    /// 它会沿此链路上溯到最近的已挂载实现。
    /// </para>
    /// <para>
    /// 属于运行时关系，不参与 JSON 序列化。
    /// </para>
    /// </summary>
    [JsonIgnore]
    public Menu? Parent { get; internal set; }

    /// <summary>
    /// 最近的已挂载平台实现：优先自身 <see cref="Impl"/>，否则沿 <see cref="Parent"/> 向上查找。
    /// <para>
    /// 叶子菜单项与 macOS/Linux 的嵌套子菜单项都没有独立的平台对象，
    /// 其属性更新必须交给最近的容器实现处理。调用 <see cref="IMenuImpl.UpdateMenuItem"/>
    /// 前应先通过本属性解析目标实现，否则 <c>item.Impl</c> 为 null 会导致更新被静默丢弃。
    /// </para>
    /// </summary>
    [JsonIgnore]
    public IMenuImpl? OwningImpl
    {
        get
        {
            for (Menu? node = this; node is not null; node = node.Parent)
            {
                if (node.Impl is not null)
                {
                    return node.Impl;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 默认构造函数。
    /// </summary>
    public Menu()
    {
    }

    /// <summary>
    /// 使用指定标签构造菜单。
    /// </summary>
    /// <param name="label">菜单标签文本。</param>
    public Menu(string label)
    {
        Label = label;
    }

    /// <summary>
    /// 挂载平台实现，并把直接子菜单项的 <see cref="Parent"/> 指回当前菜单。
    /// <para>
    /// 由平台实现在构建原生菜单时调用。<see cref="Menu"/> 位于 Application 程序集，
    /// 而各平台实现位于独立程序集，此处通过 <c>InternalsVisibleTo</c> 开放。
    /// </para>
    /// <para>
    /// 挂载后 <see cref="Append"/> / <see cref="Remove"/> / <see cref="Clear"/> / <see cref="Update"/>
    /// 才会真正作用到原生菜单；未挂载时上述操作只修改托管模型。
    /// </para>
    /// </summary>
    /// <param name="impl">平台实现。</param>
    internal void AttachImpl(IMenuImpl impl)
    {
        Impl = impl;

        // 整棵树的父指针都要补齐：反序列化得到的菜单树不会经过 Append，
        // 而 <see cref="OwningImpl"/> 依赖这条链路上溯到容器实现。
        AttachParents(this);
    }

    /// <summary>
    /// 递归设置子项的 <see cref="Parent"/>。
    /// </summary>
    /// <param name="node">当前节点。</param>
    private static void AttachParents(Menu node)
    {
        foreach (var item in node.Items)
        {
            item.Parent = node;

            if (item.Items.Count > 0)
            {
                AttachParents(item);
            }
        }
    }

    /// <summary>
    /// 卸下平台实现（仅当当前挂载的就是该实现时）。
    /// </summary>
    /// <param name="impl">平台实现。</param>
    internal void DetachImpl(IMenuImpl impl)
    {
        if (ReferenceEquals(Impl, impl))
        {
            Impl = null;
        }
    }

    /// <summary>
    /// 添加菜单项。
    /// </summary>
    /// <param name="label">菜单项标签。</param>
    /// <param name="callback">点击回调，可为 null。</param>
    /// <returns>新创建的菜单项。</returns>
    public MenuItem AddMenuItem(string label, Action? callback = null)
    {
        var item = new MenuItem(label, callback);
        Append(item);
        return item;
    }

    /// <summary>
    /// 添加复选框菜单项。
    /// </summary>
    /// <param name="label">菜单项标签。</param>
    /// <param name="checked">是否选中。</param>
    /// <param name="callback">点击回调，可为 null。</param>
    /// <returns>新创建的菜单项。</returns>
    public MenuItem AddCheckboxMenuItem(string label, bool @checked, Action? callback = null)
    {
        var item = new MenuItem(label, true, @checked, callback);
        Append(item);
        return item;
    }

    /// <summary>
    /// 添加单选菜单项。
    /// </summary>
    /// <param name="label">菜单项标签。</param>
    /// <param name="checked">是否选中。</param>
    /// <param name="callback">点击回调，可为 null。</param>
    /// <returns>新创建的菜单项。</returns>
    public MenuItem AddRadioMenuItem(string label, bool @checked, Action? callback = null)
    {
        var item = new MenuItem(label, true, @checked, callback)
        {
            IsRadio = true
        };
        Append(item);
        return item;
    }

    /// <summary>
    /// 添加子菜单。
    /// </summary>
    /// <param name="label">子菜单标签。</param>
    /// <returns>新创建的子菜单。</returns>
    public Menu AddSubmenu(string label)
    {
        var submenu = new MenuItem(label)
        {
            IsSubMenu = true
        };
        Append(submenu);
        return submenu;
    }

    /// <summary>
    /// 添加分隔符。
    /// </summary>
    public void AddSeparator()
    {
        var separator = new MenuItem
        {
            IsSeparator = true,
            Role = MenuRole.Separator
        };
        Append(separator);
    }

    /// <summary>
    /// 添加角色菜单项。对应 Wails v3 的预定义菜单项机制。
    /// </summary>
    /// <param name="role">菜单项角色。</param>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <returns>新创建的角色菜单项。</returns>
    public MenuItem AddRoleItem(MenuRole role, string? label = null)
    {
        var item = new MenuItem
        {
            Role = role,
            Label = label
        };
        if (role == MenuRole.Separator)
        {
            item.IsSeparator = true;
        }
        Append(item);
        return item;
    }

    /// <summary>
    /// 添加标准 Edit 菜单（Undo/Redo/Separator/Cut/Copy/Paste/SelectAll）。
    /// 对应 Wails v3 / Tauri v2 的 Edit 角色组合。
    /// </summary>
    /// <returns>当前菜单实例（便于链式调用）。</returns>
    public Menu AddStandardEditMenu()
    {
        Append(MenuItem.CreateUndo());
        Append(MenuItem.CreateRedo());
        Append(MenuItem.CreateSeparator());
        Append(MenuItem.CreateCut());
        Append(MenuItem.CreateCopy());
        Append(MenuItem.CreatePaste());
        Append(MenuItem.CreateSelectAll());
        return this;
    }

    /// <summary>
    /// 添加标准 Window 菜单（Minimize/Maximize/Separator/CloseWindow）。
    /// </summary>
    /// <returns>当前菜单实例（便于链式调用）。</returns>
    public Menu AddStandardWindowMenu()
    {
        Append(MenuItem.CreateMinimize());
        Append(MenuItem.CreateMaximize());
        Append(MenuItem.CreateSeparator());
        Append(MenuItem.CreateCloseWindow());
        return this;
    }

    /// <summary>
    /// 添加标准 Help 菜单（About）。
    /// </summary>
    /// <param name="metadata">关于对话框元数据，可为 null。</param>
    /// <param name="label">标签文本，留空时使用默认文本。</param>
    /// <returns>当前菜单实例（便于链式调用）。</returns>
    public Menu AddStandardHelpMenu(AboutMetadata? metadata = null, string? label = null)
    {
        Append(MenuItem.CreateAbout(label, metadata));
        return this;
    }

    /// <summary>
    /// 追加菜单项。
    /// </summary>
    /// <param name="item">要追加的菜单项。</param>
    public void Append(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.Parent = this;
        Items.Add(item);
        // 用 OwningImpl 而非 Impl：macOS/Linux 的嵌套子菜单不持有独立平台对象，
        // 追加操作必须交给最近的容器实现，否则原生菜单不会更新。
        OwningImpl?.AddMenuItem(item, Items.Count - 1);
    }

    /// <summary>
    /// 移除菜单项。
    /// </summary>
    /// <param name="item">要移除的菜单项。</param>
    public void Remove(MenuItem item)
    {
        if (!Items.Remove(item))
        {
            return;
        }

        OwningImpl?.RemoveMenuItem(item);

        if (ReferenceEquals(item.Parent, this))
        {
            item.Parent = null;
        }
    }

    /// <summary>
    /// 清空菜单项。
    /// </summary>
    public void Clear()
    {
        foreach (var item in Items)
        {
            OwningImpl?.RemoveMenuItem(item);

            if (ReferenceEquals(item.Parent, this))
            {
                item.Parent = null;
            }
        }

        Items.Clear();
    }

    /// <summary>
    /// 更新菜单，通知平台实现刷新。
    /// <para>
    /// 递归下推到持有独立平台实现的子菜单；没有独立实现的嵌套子菜单
    /// （macOS/Linux 走整树重建）已由祖先实现覆盖，无需重复下推。
    /// </para>
    /// </summary>
    public void Update()
    {
        // macOS/Linux 的嵌套子菜单没有独立实现，需上溯到根实现才能触发整树重建。
        var impl = OwningImpl;
        if (impl is null)
        {
            return;
        }

        foreach (var item in Items)
        {
            impl.UpdateMenuItem(item);

            // 仅在子菜单自己持有平台实现时才需要单独下推（Windows 的每个 popup 各有实例）。
            if (item.Impl is not null)
            {
                item.Update();
            }
        }
    }

    /// <summary>
    /// 递归设置所有菜单项的上下文菜单数据。
    /// 对应 Wails v3 Go 版本通过遍历所有 <c>MenuItem</c> 并调用 <c>setContextData</c> 的传播逻辑。
    /// <para>
    /// 在弹出上下文菜单前调用，使每个菜单项的点击回调能通过 <see cref="Context.MenuContext"/>
    /// 读取到触发上下文菜单的前端附加数据。
    /// </para>
    /// </summary>
    /// <param name="data">上下文菜单数据，可为 null。</param>
    internal void SetContextMenuData(ContextMenuData? data)
    {
        foreach (var item in Items)
        {
            item.SetContextMenuData(data);
        }
    }
}
