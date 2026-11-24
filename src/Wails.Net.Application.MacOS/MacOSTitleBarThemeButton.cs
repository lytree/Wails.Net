#if MACOS
using AppKit;
using CoreGraphics;
using Wails.Net.Application.Options;

namespace Wails.Net.Application.Platform;

/// <summary>
/// macOS 原生标题栏主题切换按钮（Wails.Net 扩展）：以标题栏附件视图
/// （<see cref="NSTitlebarAccessoryViewController"/>）的形式放在窗口红绿灯按钮右侧，
/// 点击在「按系统 → 暗黑 → 明亮」三态间循环，并驱动应用级 <see cref="ThemeMode"/>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么用附件视图</b>：AppKit 没有「向标题栏插入自定义按钮」的公开 API，
/// 官方支持的做法就是 <c>NSWindow.addTitlebarAccessoryViewController(_:)</c>；
/// 配合 <see cref="NSLayoutAttribute.Left"/>，视图会被排在红绿灯按钮之后、
/// 标题文字之前，与「最小化旁边」的位置语义一致。
/// </para>
/// <para>
/// <b>图标</b>：优先使用 SF Symbols（<c>circle.lefthalf.filled</c> / <c>moon.fill</c> /
/// <c>sun.max.fill</c>），符号图会随系统外观自动反色；取不到时退回 Unicode 文字字形，
/// 保证任何系统版本下按钮都有可辨识的图形。
/// </para>
/// </remarks>
internal sealed class MacOSTitleBarThemeButton
{
    /// <summary>
    /// 按钮尺寸（点）：标题栏高度约 28pt，按钮略小以免挤压标题。
    /// </summary>
    private static readonly CGRect ButtonFrame = new(0, 0, 28, 22);

    private readonly NSWindow _window;
    private readonly NSButton _button;
    private NSTitlebarAccessoryViewController? _controller;

    /// <summary>
    /// 构造主题按钮（尚未挂到标题栏，需再调用 <see cref="Attach"/>）。
    /// </summary>
    /// <param name="window">目标窗口。</param>
    /// <param name="activated">点击回调（在主线程触发）。</param>
    public MacOSTitleBarThemeButton(NSWindow window, Action activated)
    {
        _window = window;

        _button = new NSButton(ButtonFrame)
        {
            Bordered = false,
            ImagePosition = NSCellImagePosition.ImageOnly,
            ImageScaling = NSImageScale.ProportionallyDown,
        };

        _button.Activated += (_, _) => activated();
    }

    /// <summary>
    /// 把按钮挂到窗口标题栏。重复调用无副作用。
    /// </summary>
    /// <returns>挂载成功返回 true。</returns>
    public bool Attach()
    {
        if (_controller is not null)
        {
            return true;
        }

        try
        {
            _controller = new NSTitlebarAccessoryViewController
            {
                View = _button,

                // Left：紧邻红绿灯按钮右侧（macOS 窗口控制按钮在左侧）。
                LayoutAttribute = NSLayoutAttribute.Left,
            };

            _window.AddTitlebarAccessoryViewController(_controller);
            return true;
        }
        catch (Exception ex)
        {
            // 无标题栏的窗口类型（无边框 / 部分 NSPanel）不支持附件视图，退化为不显示按钮。
            System.Diagnostics.Debug.WriteLine($"[Wails.Net] macOS 主题按钮挂载失败：{ex.Message}");
            _controller = null;
            return false;
        }
    }

    /// <summary>
    /// 按当前模式刷新图标与提示文案。
    /// </summary>
    /// <param name="mode">主题模式，决定图标形状。</param>
    public void Update(ThemeMode mode)
    {
        // NSButton.Image 在绑定中允许为 null（清空图片改用文字），
        // 但可空标注随绑定版本变化，这里显式关闭可空检查避免 CS8601 / CS8625。
#nullable disable
        var image = NSImage.GetSystemSymbol(MacTitleBarThemeSymbols.For(mode), null);
        if (image is not null)
        {
            _button.Image = image;
            _button.Title = string.Empty;
        }
        else
        {
            // SF Symbol 不可用时退回文字字形，避免出现空白按钮。
            _button.Image = null;
            _button.Title = MacTitleBarThemeSymbols.Glyph(mode);
            _button.Font = NSFont.SystemFontOfSize(13);
        }

        _button.ToolTip = ThemeModeCycle.Describe(mode);
#nullable restore
    }
}
#endif
