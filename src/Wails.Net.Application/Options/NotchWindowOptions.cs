using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Options;

/// <summary>
/// notch 窗口配置：挂接在 macOS 显示器顶部摄像头刘海区域的独立窗口。
/// 对应 Wails v3 Go 版本 <c>notch_window.go</c> 中的 <c>NotchWindowOptions</c>。
/// <para>
/// <see cref="Width"/> / <see cref="Height"/> 描述可用的 WebView 区域，
/// Wails 会在其四周补充原生形状边距后创建实际窗口。
/// 仅在 macOS 上创建真实窗口；其他平台返回空窗口包装（与 Go 侧
/// <c>notchWindowsSupported</c> 语义一致）。
/// </para>
/// </summary>
public class NotchWindowOptions
{
    /// <summary>
    /// 原生形状边距内的可用 WebView 宽度，默认 660。
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 原生形状边距内的可用 WebView 高度，默认 92。
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 显示时向下滑入、隐藏时向上滑出的动画开关。
    /// </summary>
    public bool Animated { get; set; }

    /// <summary>
    /// 显示动画时长；零值使用默认 420ms（隐藏使用其 2/3）。
    /// </summary>
    public TimeSpan AnimationSpeed { get; set; }

    /// <summary>
    /// 承载窗口的显示器；null 时使用主显示器。
    /// </summary>
    public Screen? Screen { get; set; }

    /// <summary>
    /// Web 内容与可选的标准窗口设置。
    /// 原生几何与外观字段由 <c>CreateNotchWindow</c> 接管。
    /// </summary>
    public WebviewWindowOptions WindowOptions { get; set; } = new();
}
