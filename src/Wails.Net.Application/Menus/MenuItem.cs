namespace Wails.Net.Application.Menus;

/// <summary>
/// 菜单项，继承自 Menu，对应 Wails v3 中的 MenuItem。
/// </summary>
public class MenuItem : Menu
{
    /// <summary>
    /// 全局菜单项 ID 计数器。
    /// </summary>
    private static uint s_nextID = 1;

    /// <summary>
    /// 点击回调。
    /// </summary>
    public Action? Callback { get; set; }

    /// <summary>
    /// 菜单项 ID（自动生成）。
    /// </summary>
    public uint ID { get; set; }

    /// <summary>
    /// 默认构造函数，自动生成 ID。
    /// </summary>
    public MenuItem()
    {
        ID = GenerateID();
    }

    /// <summary>
    /// 使用标签和回调构造菜单项。
    /// </summary>
    /// <param name="label">菜单项标签。</param>
    /// <param name="callback">点击回调，可为 null。</param>
    public MenuItem(string label, Action? callback = null)
    {
        ID = GenerateID();
        Label = label;
        Callback = callback;
    }

    /// <summary>
    /// 使用标签、复选框状态和回调构造菜单项。
    /// </summary>
    /// <param name="label">菜单项标签。</param>
    /// <param name="isCheckbox">是否为复选框。</param>
    /// <param name="isChecked">是否选中。</param>
    /// <param name="callback">点击回调，可为 null。</param>
    public MenuItem(string label, bool isCheckbox, bool isChecked, Action? callback = null)
    {
        ID = GenerateID();
        Label = label;
        IsCheckbox = isCheckbox;
        Checked = isChecked;
        Callback = callback;
    }

    /// <summary>
    /// 生成唯一菜单项 ID。
    /// </summary>
    /// <returns>唯一 ID。</returns>
    public static uint GenerateID()
    {
        return s_nextID++;
    }
}
