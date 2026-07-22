using Wails.Net.Application.Menus.Context;

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
    /// 点击回调。当 <see cref="Role"/> 不为 <see cref="MenuRole.None"/> 时由平台实现自动设置。
    /// <para>
    /// 此回调不接收参数。若需访问菜单点击上下文（被点击的菜单项、复选框状态、上下文菜单数据），
    /// 请使用 <see cref="CallbackWithContext"/>。
    /// </para>
    /// </summary>
    public Action? Callback { get; set; }

    /// <summary>
    /// 带上下文的点击回调。
    /// 对应 Wails v3 Go 版本 menuitem.go 中接收 <c>Context</c> 参数的回调签名。
    /// <para>
    /// 当此回调不为 null 时，平台实现优先调用此回调（而非 <see cref="Callback"/>），
    /// 并传入包含被点击菜单项、复选框状态和上下文菜单数据的 <see cref="MenuContext"/>。
    /// </para>
    /// </summary>
    public Action<MenuContext>? CallbackWithContext { get; set; }

    /// <summary>
    /// 菜单项 ID（自动生成，只读）。
    /// </summary>
    public uint ID { get; }

    /// <summary>
    /// 菜单项预定义角色。非 <see cref="MenuRole.None"/> 时由平台实现自动调用系统命令，
    /// 忽略 <see cref="Callback"/> 字段。对应 Wails v3 Go 版本 MenuItem.Role。
    /// </summary>
    public MenuRole Role { get; set; } = MenuRole.None;

    /// <summary>
    /// 关于对话框元数据，仅当 <see cref="Role"/> == <see cref="MenuRole.About"/> 时生效。
    /// 对应 Tauri v2 AboutMetadata。
    /// </summary>
    public AboutMetadata? AboutMetadata { get; set; }

    /// <summary>
    /// 上下文菜单数据，由 <see cref="Menu.SetContextMenuData"/> 传播设置。
    /// 对应 Wails v3 Go 版本 <c>MenuItem.contextMenuData</c> 字段。
    /// <para>
    /// 当菜单项通过上下文菜单弹出并被点击时，此数据会填充到 <see cref="Context.MenuContext.ContextMenuData"/>，
    /// 供 <see cref="CallbackWithContext"/> 回调读取。
    /// </para>
    /// </summary>
    internal ContextMenuData? ContextMenuData { get; set; }

    /// <summary>
    /// 设置上下文菜单数据并递归传播到子菜单项。
    /// 对应 Wails v3 Go 版本 <c>MenuItem.setContextData</c> 方法。
    /// <para>
    /// 使用 <see cref="new"/> 关键字隐藏基类 <see cref="Menu.SetContextMenuData"/> 方法：
    /// <see cref="MenuItem"/> 需要在设置 <see cref="ContextMenuData"/> 字段的同时递归传播到子菜单项，
    /// 而 <see cref="Menu"/> 版本仅遍历 <see cref="Menu.Items"/> 调用各菜单项的此方法。
    /// </para>
    /// </summary>
    /// <param name="data">上下文菜单数据，可为 null。</param>
    internal new void SetContextMenuData(ContextMenuData? data)
    {
        ContextMenuData = data;
        // 若此菜单项为子菜单，递归传播到子菜单的所有菜单项。
        if (IsSubMenu)
        {
            foreach (var child in Items)
            {
                child.SetContextMenuData(data);
            }
        }
    }

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
    /// 生成唯一菜单项 ID（线程安全）。
    /// </summary>
    /// <returns>唯一 ID。</returns>
    public static uint GenerateID()
    {
        return Interlocked.Increment(ref s_nextID) - 1;
    }

    // ─── 角色工厂方法（参考 Tauri PredefinedMenuItem 工厂 API）───

    /// <summary>
    /// 创建 <see cref="MenuRole.Copy"/> 角色菜单项。
    /// </summary>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <returns>Copy 角色菜单项。</returns>
    public static MenuItem CreateCopy(string? label = null) =>
        new() { Role = MenuRole.Copy, Label = label };

    /// <summary>
    /// 创建 <see cref="MenuRole.Cut"/> 角色菜单项。
    /// </summary>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <returns>Cut 角色菜单项。</returns>
    public static MenuItem CreateCut(string? label = null) =>
        new() { Role = MenuRole.Cut, Label = label };

    /// <summary>
    /// 创建 <see cref="MenuRole.Paste"/> 角色菜单项。
    /// </summary>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <returns>Paste 角色菜单项。</returns>
    public static MenuItem CreatePaste(string? label = null) =>
        new() { Role = MenuRole.Paste, Label = label };

    /// <summary>
    /// 创建 <see cref="MenuRole.SelectAll"/> 角色菜单项。
    /// </summary>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <returns>SelectAll 角色菜单项。</returns>
    public static MenuItem CreateSelectAll(string? label = null) =>
        new() { Role = MenuRole.SelectAll, Label = label };

    /// <summary>
    /// 创建 <see cref="MenuRole.Undo"/> 角色菜单项。
    /// </summary>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <returns>Undo 角色菜单项。</returns>
    public static MenuItem CreateUndo(string? label = null) =>
        new() { Role = MenuRole.Undo, Label = label };

    /// <summary>
    /// 创建 <see cref="MenuRole.Redo"/> 角色菜单项。
    /// </summary>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <returns>Redo 角色菜单项。</returns>
    public static MenuItem CreateRedo(string? label = null) =>
        new() { Role = MenuRole.Redo, Label = label };

    /// <summary>
    /// 创建 <see cref="MenuRole.Separator"/> 角色菜单项（分隔符）。
    /// </summary>
    /// <returns>Separator 角色菜单项。</returns>
    public static MenuItem CreateSeparator() =>
        new() { Role = MenuRole.Separator, IsSeparator = true };

    /// <summary>
    /// 创建 <see cref="MenuRole.Minimize"/> 角色菜单项。
    /// </summary>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <returns>Minimize 角色菜单项。</returns>
    public static MenuItem CreateMinimize(string? label = null) =>
        new() { Role = MenuRole.Minimize, Label = label };

    /// <summary>
    /// 创建 <see cref="MenuRole.Maximize"/> 角色菜单项。
    /// </summary>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <returns>Maximize 角色菜单项。</returns>
    public static MenuItem CreateMaximize(string? label = null) =>
        new() { Role = MenuRole.Maximize, Label = label };

    /// <summary>
    /// 创建 <see cref="MenuRole.Fullscreen"/> 角色菜单项。
    /// </summary>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <returns>Fullscreen 角色菜单项。</returns>
    public static MenuItem CreateFullscreen(string? label = null) =>
        new() { Role = MenuRole.Fullscreen, Label = label };

    /// <summary>
    /// 创建 <see cref="MenuRole.CloseWindow"/> 角色菜单项。
    /// </summary>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <returns>CloseWindow 角色菜单项。</returns>
    public static MenuItem CreateCloseWindow(string? label = null) =>
        new() { Role = MenuRole.CloseWindow, Label = label };

    /// <summary>
    /// 创建 <see cref="MenuRole.Quit"/> 角色菜单项。
    /// </summary>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <returns>Quit 角色菜单项。</returns>
    public static MenuItem CreateQuit(string? label = null) =>
        new() { Role = MenuRole.Quit, Label = label };

    /// <summary>
    /// 创建 <see cref="MenuRole.About"/> 角色菜单项。
    /// </summary>
    /// <param name="label">标签文本，留空时由平台实现提供默认本地化文本。</param>
    /// <param name="metadata">关于对话框元数据，可为 null。</param>
    /// <returns>About 角色菜单项。</returns>
    public static MenuItem CreateAbout(string? label = null, AboutMetadata? metadata = null) =>
        new() { Role = MenuRole.About, Label = label, AboutMetadata = metadata };
}
