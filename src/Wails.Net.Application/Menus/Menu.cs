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
    /// </summary>
    public IMenuImpl? Impl { get; internal set; }

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
        Items.Add(item);
        Impl?.AddMenuItem(item, Items.Count - 1);
    }

    /// <summary>
    /// 移除菜单项。
    /// </summary>
    /// <param name="item">要移除的菜单项。</param>
    public void Remove(MenuItem item)
    {
        Items.Remove(item);
        Impl?.RemoveMenuItem(item);
    }

    /// <summary>
    /// 清空菜单项。
    /// </summary>
    public void Clear()
    {
        foreach (var item in Items)
        {
            Impl?.RemoveMenuItem(item);
        }

        Items.Clear();
    }

    /// <summary>
    /// 更新菜单，通知平台实现刷新。
    /// </summary>
    public void Update()
    {
        foreach (var item in Items)
        {
            Impl?.UpdateMenuItem(item);
        }
    }
}
