using Wails.Net.Application.Menus;

namespace Wails.Net.Application.Menus;

/// <summary>
/// 菜单角色辅助工具。提供默认 Label、默认 Accelerator 与角色命令执行的跨平台共享逻辑。
/// 各平台 <see cref="IMenuImpl"/> 实现可调用此类的静态方法完成公共逻辑，
/// 仅 <see cref="ExecuteRole"/> 中的窗口操作与编辑命令需由平台实现注入。
/// </summary>
internal static class MenuRoleHelper
{
    /// <summary>
    /// 获取角色的默认本地化标签文本（中文）。
    /// </summary>
    /// <param name="role">菜单角色。</param>
    /// <returns>默认标签；若角色无默认标签返回 null。</returns>
    public static string? GetDefaultLabel(MenuRole role) => role switch
    {
        MenuRole.Copy => "复制",
        MenuRole.Cut => "剪切",
        MenuRole.Paste => "粘贴",
        MenuRole.SelectAll => "全选",
        MenuRole.Undo => "撤销",
        MenuRole.Redo => "重做",
        MenuRole.Minimize => "最小化",
        MenuRole.Maximize => "最大化",
        MenuRole.Fullscreen => "切换全屏",
        MenuRole.CloseWindow => "关闭窗口",
        MenuRole.Quit => "退出",
        MenuRole.About => "关于",
        MenuRole.Zoom => "缩放",
        MenuRole.Hide => "隐藏",
        MenuRole.HideOthers => "隐藏其他",
        MenuRole.ShowAll => "全部显示",
        MenuRole.Services => "服务",
        MenuRole.BringAllToFront => "全部置于前台",
        MenuRole.ToggleFullScreen => "切换全屏",
        _ => null,
    };

    /// <summary>
    /// 获取角色的默认加速键字符串（Wails 风格，如 "Ctrl+C"）。
    /// </summary>
    /// <param name="role">菜单角色。</param>
    /// <returns>加速键字符串；若角色无默认加速键返回 null。</returns>
    public static string? GetDefaultAccelerator(MenuRole role) => role switch
    {
        MenuRole.Copy => "Ctrl+C",
        MenuRole.Cut => "Ctrl+X",
        MenuRole.Paste => "Ctrl+V",
        MenuRole.SelectAll => "Ctrl+A",
        MenuRole.Undo => "Ctrl+Z",
        MenuRole.Redo => "Ctrl+Y",
        MenuRole.Minimize => "Ctrl+M",
        MenuRole.CloseWindow => "Ctrl+W",
        MenuRole.Quit => "Ctrl+Q",
        _ => null,
    };

    /// <summary>
    /// 判断指定角色是否为 macOS 专属（在其他平台应静默 no-op）。
    /// </summary>
    /// <param name="role">菜单角色。</param>
    /// <returns>true 表示 macOS 专属；false 表示跨平台支持。</returns>
    public static bool IsMacOSExclusive(MenuRole role) => role switch
    {
        MenuRole.Hide
        or MenuRole.HideOthers
        or MenuRole.ShowAll
        or MenuRole.Services
        or MenuRole.BringAllToFront
        or MenuRole.Zoom
        or MenuRole.ToggleFullScreen => true,
        _ => false,
    };

    /// <summary>
    /// 准备菜单项：填充默认 Label 与 Accelerator，设置 Callback 为角色命令执行器。
    /// 应在平台 <see cref="IMenuImpl.ApplyRole"/> 实现的开头调用。
    /// </summary>
    /// <param name="item">要准备的菜单项。</param>
    /// <param name="window">关联窗口。</param>
    /// <param name="executeCallback">角色命令执行器，由平台实现提供。</param>
    public static void PrepareRoleItem(
        MenuItem item,
        Windows.IWebviewWindowImpl? window,
        Action<MenuRole, Windows.IWebviewWindowImpl?, AboutMetadata?> executeCallback)
    {
        if (item.Role == MenuRole.None)
        {
            return;
        }

        // 填充默认 Label
        if (string.IsNullOrEmpty(item.Label))
        {
            var defaultLabel = GetDefaultLabel(item.Role);
            if (defaultLabel is not null)
            {
                item.Label = defaultLabel;
            }
        }

        // 绑定默认 Accelerator
        var accelerator = GetDefaultAccelerator(item.Role);
        if (accelerator is not null && string.IsNullOrEmpty(item.Accelerator))
        {
            item.Accelerator = accelerator;
        }

        // 设置 Callback 为角色命令执行器
        var metadata = item.AboutMetadata;
        var role = item.Role;
        item.Callback = () => executeCallback(role, window, metadata);
    }
}
