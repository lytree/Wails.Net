using Wails.Net.Application.Menus;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.SystemTray;

/// <summary>
/// 系统托盘图标位置枚举。
/// 对应 Wails v3 Go 版本 systemtray.go 中的 IconPosition。
/// </summary>
public enum TrayIconPosition
{
    /// <summary>
    /// 默认位置。
    /// </summary>
    Default = 0,

    /// <summary>
    /// 左对齐。
    /// </summary>
    Left = 1,

    /// <summary>
    /// 右对齐。
    /// </summary>
    Right = 2
}

/// <summary>
/// 系统托盘平台实现接口。
/// </summary>
public interface ISystemTrayImpl
{
    /// <summary>
    /// 托盘左键点击事件。
    /// 对应 Wails v3 Go 版本 SystemTray.OnTrayClick。
    /// </summary>
    event Action? OnTrayClick;

    /// <summary>
    /// 托盘右键点击事件。
    /// 对应 Wails v3 Go 版本 SystemTray.OnRightClick。
    /// </summary>
    event Action? OnTrayRightClick;

    /// <summary>
    /// 托盘左键双击事件。
    /// 对应 Wails v3 Go 版本 SystemTray.OnDoubleClick。
    /// </summary>
    event Action? OnTrayDoubleClick;

    /// <summary>
    /// 托盘右键双击事件。
    /// 对应 Wails v3 Go 版本 SystemTray.OnRightDoubleClick。
    /// </summary>
    event Action? OnTrayRightDoubleClick;

    /// <summary>
    /// 鼠标进入托盘事件。
    /// 对应 Wails v3 Go 版本 SystemTray.OnMouseEnter。
    /// </summary>
    event Action? OnTrayMouseEnter;

    /// <summary>
    /// 鼠标离开托盘事件。
    /// 对应 Wails v3 Go 版本 SystemTray.OnMouseLeave。
    /// </summary>
    event Action? OnTrayMouseLeave;

    /// <summary>
    /// 设置托盘图标。
    /// </summary>
    /// <param name="iconData">图标字节数据。</param>
    void SetIcon(byte[] iconData);

    /// <summary>
    /// 设置托盘标签。
    /// </summary>
    /// <param name="label">标签文本。</param>
    void SetLabel(string label);

    /// <summary>
    /// 设置托盘菜单。
    /// </summary>
    /// <param name="menu">菜单实例，可为 null。</param>
    void SetMenu(Menu? menu);

    /// <summary>
    /// 显示托盘图标。
    /// </summary>
    void Show();

    /// <summary>
    /// 隐藏托盘图标。
    /// </summary>
    void Hide();

    /// <summary>
    /// 销毁托盘。
    /// </summary>
    void Destroy();

    /// <summary>
    /// 设置托盘提示文本。
    /// </summary>
    /// <param name="tooltip">提示文本。</param>
    void SetTooltip(string tooltip);

    /// <summary>
    /// 设置暗色模式图标。
    /// </summary>
    /// <param name="iconData">图标字节数据。</param>
    void SetDarkModeIcon(byte[] iconData);

    /// <summary>
    /// 设置图标位置（部分平台支持）。
    /// 对应 Wails v3 Go 版本 <c>SystemTray.SetIconPosition</c>。
    /// 默认空实现。
    /// </summary>
    /// <param name="position">图标位置。</param>
    void SetIconPosition(TrayIconPosition position)
    {
        // 默认空实现，平台可重写。
    }

    /// <summary>
    /// 设置模板图标（macOS 专属，其他平台为 no-op）。
    /// 对应 Wails v3 Go 版本 <c>SystemTray.SetTemplateIcon</c>。
    /// 默认空实现。
    /// </summary>
    /// <param name="iconData">图标字节数据。</param>
    void SetTemplateIcon(byte[] iconData)
    {
        // 默认空实现，仅 macOS 需要。
    }

    /// <summary>
    /// 关联窗口到托盘，使托盘可控制该窗口的显示/隐藏。
    /// 对应 Wails v3 Go 版本 <c>SystemTray.AttachWindow</c>。
    /// 默认空实现。
    /// </summary>
    /// <param name="window">要关联的窗口。</param>
    void AttachWindow(WebviewWindow window)
    {
        // 默认空实现，平台可重写。
    }

    /// <summary>
    /// 设置窗口位置偏移量（像素），用于 PositionWindow 计算。
    /// 对应 Wails v3 Go 版本 <c>SystemTray.WindowOffset</c>。
    /// 默认空实现。
    /// </summary>
    /// <param name="offset">偏移量（像素）。</param>
    void WindowOffset(int offset)
    {
        // 默认空实现，平台可重写。
    }

    /// <summary>
    /// 设置窗口显示去抖动时间（毫秒）。
    /// 对应 Wails v3 Go 版本 <c>SystemTray.WindowDebounce</c>。
    /// 默认空实现。
    /// </summary>
    /// <param name="debounceMs">去抖动时间（毫秒）。</param>
    void WindowDebounce(int debounceMs)
    {
        // 默认空实现，平台可重写。
    }

    /// <summary>
    /// 将关联的窗口定位到托盘附近（默认显示在托盘图标的屏幕上）。
    /// 对应 Wails v3 Go 版本 <c>SystemTray.PositionWindow</c>。
    /// 默认空实现。
    /// </summary>
    /// <param name="window">要定位的窗口。</param>
    /// <param name="offset">位置偏移量（像素）。</param>
    void PositionWindow(WebviewWindow window, int offset)
    {
        // 默认空实现，平台可重写。
    }

    /// <summary>
    /// 切换关联窗口的显示状态（显示↔隐藏）。
    /// 对应 Wails v3 Go 版本 <c>SystemTray.ToggleWindow</c>。
    /// 默认空实现。
    /// </summary>
    void ToggleWindow()
    {
        // 默认空实现，平台可重写。
    }

    /// <summary>
    /// 显示关联的窗口。
    /// 对应 Wails v3 Go 版本 <c>SystemTray.ShowWindow</c>。
    /// 默认空实现。
    /// </summary>
    void ShowWindow()
    {
        // 默认空实现，平台可重写。
    }

    /// <summary>
    /// 隐藏关联的窗口。
    /// 对应 Wails v3 Go 版本 <c>SystemTray.HideWindow</c>。
    /// 默认空实现。
    /// </summary>
    void HideWindow()
    {
        // 默认空实现，平台可重写。
    }

    /// <summary>
    /// 显示托盘菜单（左键点击场景）。
    /// 对应 Wails v3 Go 版本 <c>SystemTray.ShowMenu</c>。
    /// 默认空实现。
    /// </summary>
    void ShowMenu()
    {
        // 默认空实现，平台可重写。
    }

    /// <summary>
    /// 打开托盘菜单（与 ShowMenu 等效，部分平台实现不同）。
    /// 对应 Wails v3 Go 版本 <c>SystemTray.OpenMenu</c>。
    /// 默认空实现。
    /// </summary>
    void OpenMenu()
    {
        // 默认空实现，平台可重写。
    }

    /// <summary>
    /// 获取托盘图标的屏幕边界矩形。
    /// 对应 Wails v3 Go 版本 <c>SystemTray.bounds</c>。
    /// 默认实现返回 null（不支持的平台）。
    /// </summary>
    /// <returns>托盘边界矩形，不支持时返回 null。</returns>
    Screens.Rect? GetBounds()
    {
        // 默认实现：不支持。
        return null;
    }
}
