using Wails.Net.Application.Options;

namespace Wails.Net.Application.Platform;

/// <summary>
/// macOS 标题栏主题按钮的图标与文案映射（Wails.Net 扩展）。
/// <para>
/// 纯函数、不依赖 AppKit，便于在任意宿主上做单元测试；
/// 实际把名称转成 <c>NSImage</c> 的工作由
/// <see cref="MacOSTitleBarThemeButton"/> 在 <c>#if MACOS</c> 分支内完成。
/// </para>
/// </summary>
internal static class MacTitleBarThemeSymbols
{
    /// <summary>
    /// 按系统：半填充圆（SF Symbols 2 起提供）。
    /// </summary>
    internal const string SystemSymbol = "circle.lefthalf.filled";

    /// <summary>
    /// 暗黑：实心弯月。
    /// </summary>
    internal const string DarkSymbol = "moon.fill";

    /// <summary>
    /// 明亮：实心太阳。
    /// </summary>
    internal const string LightSymbol = "sun.max.fill";

    /// <summary>
    /// 取模式对应的 SF Symbol 名称。
    /// </summary>
    /// <param name="mode">主题模式。</param>
    /// <returns>SF Symbol 名称。</returns>
    internal static string For(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => DarkSymbol,
        ThemeMode.Light => LightSymbol,
        _ => SystemSymbol,
    };

    /// <summary>
    /// 取模式对应的文本兜底图标（SF Symbol 不可用，如系统版本过低时使用）。
    /// </summary>
    /// <param name="mode">主题模式。</param>
    /// <returns>可直接作为按钮标题的字符。</returns>
    internal static string Glyph(ThemeMode mode) => mode switch
    {
        ThemeMode.Dark => "\u263E",   // ☾
        ThemeMode.Light => "\u2600",  // ☀
        _ => "\u25D0",                // ◐
    };
}
