namespace Wails.Net.Application.Menus;

/// <summary>
/// 菜单平台实现接口。
/// </summary>
public interface IMenuImpl
{
    /// <summary>
    /// 显示菜单。
    /// </summary>
    void Show();

    /// <summary>
    /// 隐藏菜单。
    /// </summary>
    void Hide();

    /// <summary>
    /// 在指定位置添加菜单项。
    /// </summary>
    /// <param name="item">要添加的菜单项。</param>
    /// <param name="position">插入位置。</param>
    void AddMenuItem(MenuItem item, int position);

    /// <summary>
    /// 移除指定菜单项。
    /// </summary>
    /// <param name="item">要移除的菜单项。</param>
    void RemoveMenuItem(MenuItem item);

    /// <summary>
    /// 更新指定菜单项。
    /// </summary>
    /// <param name="item">要更新的菜单项。</param>
    void UpdateMenuItem(MenuItem item);

    /// <summary>
    /// 在指定位置添加子菜单。
    /// </summary>
    /// <param name="submenu">子菜单实例。</param>
    /// <param name="position">插入位置。</param>
    void AddSubmenu(Menu submenu, int position);

    /// <summary>
    /// 销毁菜单。
    /// </summary>
    void Destroy();

    /// <summary>
    /// 设置菜单标签。
    /// </summary>
    /// <param name="label">标签文本。</param>
    void SetLabel(string label);

    /// <summary>
    /// 设置菜单是否启用。
    /// </summary>
    /// <param name="enabled">是否启用。</param>
    void SetEnabled(bool enabled);

    /// <summary>
    /// 设置菜单是否选中。
    /// </summary>
    /// <param name="checked">是否选中。</param>
    void SetChecked(bool @checked);

    /// <summary>
    /// 设置菜单快捷键。
    /// </summary>
    /// <param name="accelerator">快捷键字符串。</param>
    void SetAccelerator(string accelerator);

    /// <summary>
    /// 设置菜单位图。
    /// </summary>
    /// <param name="bitmap">位图字节数据，可为 null。</param>
    void SetBitmap(byte[]? bitmap);
}
