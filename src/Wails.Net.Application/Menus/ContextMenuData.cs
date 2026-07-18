namespace Wails.Net.Application.Menus;

/// <summary>
/// 上下文菜单数据，承载前端右键菜单请求的全部信息（P1-4）。
/// <para>
/// 对应 Wails v3 Go 版本 <c>messageprocessor_contextmenu.go</c> 中的 <c>ContextMenuData</c> 结构。
/// 前端在 <c>contextmenu</c> 事件中读取 CSS 变量 <c>--custom-contextmenu</c> 命中后，
/// 通过 RPC 将此数据发送到后端，由 <see cref="Wails.Net.Application.Transport.MessageProcessor"/>
/// 解析并调用 <c>WebviewWindow.OpenContextMenu(ContextMenuData)</c>。
/// </para>
/// <para>
/// <c>Data</c> 字段来自前端 CSS 变量 <c>--custom-contextmenu-data</c>，
/// 用于将任意字符串透传到菜单项的点击回调（如点击目标的标识）。
/// </para>
/// </summary>
public sealed class ContextMenuData
{
    /// <summary>
    /// 已注册的上下文菜单 ID。
    /// 后端通过此 ID 从 <see cref="Managers.IMenuManager"/> 查找对应的 <see cref="ContextMenu"/> 实例。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 鼠标 X 坐标（clientX，相对于浏览器视口）。
    /// 平台实现负责将其转换为屏幕坐标后调用原生弹出菜单 API。
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// 鼠标 Y 坐标（clientY，相对于浏览器视口）。
    /// 平台实现负责将其转换为屏幕坐标后调用原生弹出菜单 API。
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// 来自前端 CSS 变量 <c>--custom-contextmenu-data</c> 的额外数据字符串。
    /// 透传到菜单项点击回调，可用于携带触发元素的上下文信息。
    /// 可为 null 或空字符串。
    /// </summary>
    public string? Data { get; set; }
}
