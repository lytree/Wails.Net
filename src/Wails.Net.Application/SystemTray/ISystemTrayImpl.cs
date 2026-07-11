using Wails.Net.Application.Menus;

namespace Wails.Net.Application.SystemTray;

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
}
