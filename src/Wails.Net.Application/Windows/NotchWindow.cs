using Wails.Net.Application.Options;

namespace Wails.Net.Application.Windows;

/// <summary>
/// notch 窗口（相机刘海窗口）的窄生命周期包装。
/// 对应 Wails v3 Go 版本 <c>notch_window.go</c> 中的 <c>NotchWindow</c>。
/// <para>
/// 原生定位、几何与动画由平台层接管；调用方仅通过
/// <see cref="Show"/> / <see cref="Hide"/> / <see cref="Visibility"/> / <see cref="Close"/>
/// 控制可见性。不支持的平台上 <see cref="Window"/> 为 null，所有方法为 no-op。
/// </para>
/// </summary>
public sealed class NotchWindow
{
    private readonly WebviewWindow? _window;

    /// <summary>
    /// 初始化 notch 窗口包装。
    /// </summary>
    /// <param name="window">底层窗口；不支持的平台为 null。</param>
    internal NotchWindow(WebviewWindow? window)
    {
        _window = window;
    }

    /// <summary>
    /// 获取底层 Webview 窗口；平台不支持时为 null。
    /// </summary>
    public WebviewWindow? Window => _window;

    /// <summary>
    /// 显示窗口；启用动画时按配置的时长滑入。
    /// </summary>
    public void Show() => _window?.Show();

    /// <summary>
    /// 隐藏窗口；启用动画时按配置的时长滑出。
    /// </summary>
    public void Hide() => _window?.Hide();

    /// <summary>
    /// 当前是否可见。
    /// </summary>
    public bool Visibility => _window?.IsVisible() ?? false;

    /// <summary>
    /// 永久销毁原生窗口。
    /// </summary>
    public void Close() => _window?.Close();
}
