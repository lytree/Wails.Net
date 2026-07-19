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
    /// </summary>
    public Action? Callback { get; set; }

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
