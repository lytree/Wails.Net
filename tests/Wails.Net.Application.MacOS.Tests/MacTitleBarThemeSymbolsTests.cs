using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.MacOS.Tests;

/// <summary>
/// 原生标题栏主题按钮（macOS，Wails.Net 扩展）的映射测试。
/// <para>
/// 按钮本体是 AppKit 控件，只能在 macOS 上运行；这里覆盖不依赖 AppKit 的
/// SF Symbol 名称与文字兜底映射，保证三种模式都有可辨识图标。
/// </para>
/// </summary>
public sealed class MacTitleBarThemeSymbolsTests
{
    /// <summary>
    /// 三种模式映射到不同的 SF Symbol。
    /// </summary>
    [Test]
    public async Task For_ReturnsDistinctSymbolPerMode()
    {
        await Assert.That(MacTitleBarThemeSymbols.For(ThemeMode.System))
            .IsEqualTo("circle.lefthalf.filled");
        await Assert.That(MacTitleBarThemeSymbols.For(ThemeMode.Dark))
            .IsEqualTo("moon.fill");
        await Assert.That(MacTitleBarThemeSymbols.For(ThemeMode.Light))
            .IsEqualTo("sun.max.fill");
    }

    /// <summary>
    /// SF Symbol 不可用时的文字兜底：三种模式各一个字符，且互不相同。
    /// </summary>
    [Test]
    public async Task Glyph_ReturnsSingleDistinctCharacterPerMode()
    {
        var glyphs = new[]
        {
            MacTitleBarThemeSymbols.Glyph(ThemeMode.System),
            MacTitleBarThemeSymbols.Glyph(ThemeMode.Dark),
            MacTitleBarThemeSymbols.Glyph(ThemeMode.Light),
        };

        foreach (var glyph in glyphs)
        {
            await Assert.That(glyph.Length).IsEqualTo(1);
        }

        await Assert.That(glyphs.Distinct().Count()).IsEqualTo(3);
    }
}
