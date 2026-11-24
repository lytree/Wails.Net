using Gtk;
using Wails.Net.Application.Options;
using Button = Gtk.Button;
using Image = Gtk.Image;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux 原生标题栏主题切换按钮（Wails.Net 扩展）：以 <see cref="Button"/> 形式
/// 插入 <see cref="HeaderBar"/> 的末端，位于窗口控制按钮（最小化 / 最大化 / 关闭）左侧，
/// 点击在「按系统 → 暗黑 → 明亮」三态间循环，并驱动应用级 <see cref="ThemeMode"/>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么需要接管标题栏</b>：GTK4 的窗口控制按钮由 <c>GtkHeaderBar</c> 绘制，
/// 不接管标题栏就无法在按钮排里插入自定义控件。因此仅在
/// <see cref="WebviewWindowOptions.TitleBarThemeButton"/> 为 true 时，
/// 窗口才用自带按钮的 HeaderBar 替换默认标题栏；未开启该选项的行为保持不变。
/// </para>
/// <para>
/// <b>图标</b>：使用 Adwaita 主题的 symbolic 图标（<c>weather-clear-symbolic</c> /
/// <c>weather-clear-night-symbolic</c> / <c>night-light-symbolic</c>），
/// symbolic 图标会随 GTK 主题明暗自动反色；图标缺失时按候选列表回退，
/// 仍未命中则用第一个候选名（<c>HasIcon</c> 判定，避免依赖某个发行版的图标集）。
/// </para>
/// </remarks>
internal sealed class LinuxTitleBarThemeButton
{
    /// <summary>
    /// 按系统模式图标候选（按优先级排列）。
    /// </summary>
    private static readonly string[] SystemIcons =
    [
        "night-light-symbolic",
        "display-brightness-symbolic",
        "preferences-desktop-theme-symbolic",
    ];

    /// <summary>
    /// 暗黑模式图标候选。
    /// </summary>
    private static readonly string[] DarkIcons =
    [
        "weather-clear-night-symbolic",
        "night-light-symbolic",
        "display-brightness-symbolic",
    ];

    /// <summary>
    /// 明亮模式图标候选。
    /// </summary>
    private static readonly string[] LightIcons =
    [
        "weather-clear-symbolic",
        "display-brightness-symbolic",
        "preferences-desktop-theme-symbolic",
    ];

    private readonly Gdk.Display? _display;
    private readonly Image _image;

    /// <summary>
    /// 构造按钮（调用方负责把它 <c>PackEnd</c> 到 HeaderBar）。
    /// </summary>
    /// <param name="display">所在显示器，用于查询图标主题；可为 null。</param>
    /// <param name="activated">点击回调（在 GTK 主线程触发）。</param>
    public LinuxTitleBarThemeButton(Gdk.Display? display, Action activated)
    {
        _display = display;
        _image = Image.NewFromIconName(ResolveIconName(display, ThemeMode.System));

        Widget = Button.New();
        Widget.SetChild(_image);
        Widget.SetHasFrame(false);
        Widget.SetTooltipText(ThemeModeCycle.Describe(ThemeMode.System));
        Widget.OnClicked += (_, _) => activated();
    }

    /// <summary>
    /// 按钮控件本体。
    /// </summary>
    public Button Widget { get; }

    /// <summary>
    /// 按当前模式刷新图标与提示文案。
    /// </summary>
    /// <param name="mode">主题模式，决定图标形状。</param>
    public void Update(ThemeMode mode)
    {
        _image.SetFromIconName(ResolveIconName(_display, mode));
        Widget.SetTooltipText(ThemeModeCycle.Describe(mode));
    }

    /// <summary>
    /// 取模式对应的图标候选名（纯函数，便于单元测试）。
    /// </summary>
    /// <param name="mode">主题模式。</param>
    /// <returns>按优先级排列的图标名。</returns>
    internal static IReadOnlyList<string> IconCandidates(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => DarkIcons,
        ThemeMode.Light => LightIcons,
        _ => SystemIcons,
    };

    /// <summary>
    /// 在候选图标中挑出当前图标主题实际提供的一个。
    /// </summary>
    /// <param name="display">显示器（用于取图标主题），可为 null。</param>
    /// <param name="mode">主题模式。</param>
    /// <returns>可用的图标名；全部缺失时返回首个候选。</returns>
    private static string ResolveIconName(Gdk.Display? display, ThemeMode mode)
    {
        var candidates = IconCandidates(mode);

        var theme = display is null ? null : IconTheme.GetForDisplay(display);
        if (theme is not null)
        {
            foreach (var name in candidates)
            {
                if (theme.HasIcon(name))
                {
                    return name;
                }
            }
        }

        return candidates[0];
    }
}
