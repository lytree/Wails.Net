using Wails.Net.Application.Menus;

namespace Wails.Net.Application.Menus.Context;

/// <summary>
/// 菜单点击上下文，承载菜单项点击时的上下文信息。
/// 对应 Wails v3 Go 版本 context.go 中的 Context 结构。
/// <para>
/// 在菜单项点击回调中传递，允许回调读取：
/// <list type="bullet">
/// <item><see cref="ClickedMenuItem"/>：被点击的菜单项实例。</item>
/// <item><see cref="IsChecked"/>：复选框菜单项的当前选中状态。</item>
/// <item><see cref="ContextMenuData"/>：触发上下文菜单时的前端附加数据。</item>
/// </list>
/// </para>
/// </summary>
public sealed class MenuContext
{
    /// <summary>
    /// 上下文数据字典，内部存储用。
    /// </summary>
    private readonly Dictionary<string, object?> _data = new();

    /// <summary>
    /// 内部键名常量，对应 Wails v3 Go 版本 context.go 中的常量。
    /// </summary>
    internal const string ClickedMenuItemKey = "clickedMenuItem";
    internal const string MenuItemIsCheckedKey = "menuItemIsChecked";
    internal const string ContextMenuDataKey = "contextMenuData";

    /// <summary>
    /// 获取被点击的菜单项。
    /// 对应 Wails v3 Go 版本 <c>Context.ClickedMenuItem</c> 方法。
    /// </summary>
    /// <returns>被点击的菜单项实例，未设置时返回 null。</returns>
    public MenuItem? ClickedMenuItem => _data.TryGetValue(ClickedMenuItemKey, out var value) ? value as MenuItem : null;

    /// <summary>
    /// 获取复选框菜单项的选中状态。
    /// 对应 Wails v3 Go 版本 <c>Context.IsChecked</c> 方法。
    /// </summary>
    /// <returns>选中返回 true，未设置或为 false 时返回 false。</returns>
    public bool IsChecked => _data.TryGetValue(MenuItemIsCheckedKey, out var value) && value is bool b && b;

    /// <summary>
    /// 获取上下文菜单数据字符串。
    /// 对应 Wails v3 Go 版本 <c>Context.ContextMenuData</c> 方法。
    /// </summary>
    /// <returns>数据字符串，未设置或为 null 时返回空字符串。</returns>
    public string ContextMenuData
    {
        get
        {
            if (!_data.TryGetValue(ContextMenuDataKey, out var value) || value is null)
            {
                return string.Empty;
            }

            return value as string ?? string.Empty;
        }
    }

    /// <summary>
    /// 设置被点击的菜单项，返回当前上下文以支持链式调用。
    /// 对应 Wails v3 Go 版本 <c>Context.withClickedMenuItem</c> 方法。
    /// </summary>
    /// <param name="menuItem">被点击的菜单项。</param>
    /// <returns>当前上下文实例，用于链式调用。</returns>
    internal MenuContext WithClickedMenuItem(MenuItem menuItem)
    {
        _data[ClickedMenuItemKey] = menuItem;
        return this;
    }

    /// <summary>
    /// 设置复选框选中状态。
    /// 对应 Wails v3 Go 版本 <c>Context.withChecked</c> 方法。
    /// </summary>
    /// <param name="checked">是否选中。</param>
    internal void WithChecked(bool @checked)
    {
        _data[MenuItemIsCheckedKey] = @checked;
    }

    /// <summary>
    /// 设置上下文菜单数据，返回当前上下文以支持链式调用。
    /// 对应 Wails v3 Go 版本 <c>Context.withContextMenuData</c> 方法。
    /// </summary>
    /// <param name="data">上下文菜单数据，可为 null。</param>
    /// <returns>当前上下文实例，用于链式调用。</returns>
    internal MenuContext WithContextMenuData(ContextMenuData? data)
    {
        if (data is null)
        {
            return this;
        }

        _data[ContextMenuDataKey] = data.Data;
        return this;
    }
}
