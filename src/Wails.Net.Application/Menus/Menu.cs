namespace Wails.Net.Application.Menus;

/// <summary>
/// 表示一个菜单，对应 Wails v3 中的 Menu。
/// </summary>
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
            IsSeparator = true
        };
        Append(separator);
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
