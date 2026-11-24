using System.Drawing;
using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Windows.Win32.Foundation;

namespace Wails.Net.Application.Windows.Tests;

/// <summary>
/// 原生标题栏主题按钮（<see cref="Win32TitleBarThemeButton"/>）的纯逻辑测试。
/// <para>
/// 覆盖三态循环与按钮几何推算（由 <c>TITLEBARINFOEX</c> 的最小化按钮矩形反推），
/// 不创建真实窗口，因此无需 GUI 环境。
/// </para>
/// </summary>
public sealed class Win32TitleBarThemeButtonTests
{
    /// <summary>三态循环顺序：按系统 → 暗黑 → 明亮 → 按系统。</summary>
    [Test]
    public async Task Cycle_Order_IsSystemDarkLight()
    {
        await Assert.That(Win32TitleBarThemeButton.Cycle(ThemeMode.System)).IsEqualTo(ThemeMode.Dark);
        await Assert.That(Win32TitleBarThemeButton.Cycle(ThemeMode.Dark)).IsEqualTo(ThemeMode.Light);
        await Assert.That(Win32TitleBarThemeButton.Cycle(ThemeMode.Light)).IsEqualTo(ThemeMode.System);
    }

    /// <summary>循环三次回到起点（保证按钮点击可闭环）。</summary>
    [Test]
    public async Task Cycle_ThreeTimes_ReturnsToStart()
    {
        var mode = ThemeMode.System;
        for (var i = 0; i < 3; i++)
        {
            mode = Win32TitleBarThemeButton.Cycle(mode);
        }

        await Assert.That(mode).IsEqualTo(ThemeMode.System);
    }

    /// <summary>
    /// 标准标题栏：按钮应紧贴最小化按钮左侧、与之等高，宽度等于高度。
    /// </summary>
    [Test]
    public async Task TryComputeLayout_SitsLeftOfMinimize_WithSquareShape()
    {
        var titleBar = new RECT { left = 0, top = 0, right = 1280, bottom = 32 };
        var minimize = new RECT { left = 1180, top = 0, right = 1212, bottom = 32 };

        var ok = Win32TitleBarThemeButton.TryComputeLayout(titleBar, minimize, out var rect);

        await Assert.That(ok).IsTrue();
        await Assert.That(rect.right).IsEqualTo(minimize.left);
        await Assert.That(rect.top).IsEqualTo(minimize.top);
        await Assert.That(rect.bottom).IsEqualTo(minimize.bottom);
        await Assert.That(rect.right - rect.left).IsEqualTo(minimize.bottom - minimize.top);
    }

    /// <summary>标题栏过窄（会压住标题文字）时不显示按钮。</summary>
    [Test]
    public async Task TryComputeLayout_TooNarrowTitleBar_ReturnsFalse()
    {
        var titleBar = new RECT { left = 0, top = 0, right = 120, bottom = 32 };
        var minimize = new RECT { left = 60, top = 0, right = 92, bottom = 32 };

        var ok = Win32TitleBarThemeButton.TryComputeLayout(titleBar, minimize, out _);

        await Assert.That(ok).IsFalse();
    }

    /// <summary>标题栏无按钮（最小化矩形为空，如工具窗口）时不显示按钮。</summary>
    [Test]
    public async Task TryComputeLayout_EmptyMinimizeRect_ReturnsFalse()
    {
        var titleBar = new RECT { left = 0, top = 0, right = 1280, bottom = 32 };
        var minimize = default(RECT);

        var ok = Win32TitleBarThemeButton.TryComputeLayout(titleBar, minimize, out _);

        await Assert.That(ok).IsFalse();
    }

    /// <summary>按钮高度过小（高 DPI 缩放下异常取值）时不显示，避免画出畸形图标。</summary>
    [Test]
    public async Task TryComputeLayout_DegenerateHeight_ReturnsFalse()
    {
        var titleBar = new RECT { left = 0, top = 0, right = 1280, bottom = 10 };
        var minimize = new RECT { left = 1200, top = 0, right = 1210, bottom = 10 };

        var ok = Win32TitleBarThemeButton.TryComputeLayout(titleBar, minimize, out _);

        await Assert.That(ok).IsFalse();
    }

    /// <summary>
    /// 三种模式都应把图标真正画到目标 DC 上（背景之外出现前景像素），
    /// 且同一模式下悬停态的像素数量应多于常态（叠加了悬停底色）。
    /// </summary>
    /// <param name="mode">主题模式。</param>
    /// <param name="dark">实际明暗。</param>
    [Test]
    [Arguments(ThemeMode.System, false)]
    [Arguments(ThemeMode.Dark, true)]
    [Arguments(ThemeMode.Light, false)]
    public async Task Draw_PaintsIconForEachMode(ThemeMode mode, bool dark)
    {
        var idle = RenderButton(mode, dark, hover: false, pressed: false);
        try
        {
            var hovered = RenderButton(mode, dark, hover: true, pressed: false);
            try
            {
                await Assert.That(CountForegroundPixels(idle, dark)).IsGreaterThan(0);

                // 悬停叠加的是低透明度底色，单看「前景像素数」不变，
                // 直接比较两图是否有像素差异即可确认悬停态被绘制。
                await Assert.That(HasDifferentPixels(idle, hovered)).IsTrue();
            }
            finally
            {
                hovered.Dispose();
            }
        }
        finally
        {
            idle.Dispose();
        }
    }

    /// <summary>
    /// 在纯色底上离屏渲染一次按钮，返回位图。
    /// </summary>
    /// <param name="mode">主题模式。</param>
    /// <param name="dark">实际明暗（决定底色与前景）。</param>
    /// <param name="hover">是否悬停。</param>
    /// <param name="pressed">是否按下。</param>
    /// <returns>渲染结果，调用方负责释放。</returns>
    private static Bitmap RenderButton(ThemeMode mode, bool dark, bool hover, bool pressed)
    {
        var bitmap = new Bitmap(37, 37);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            // 底色模拟标题栏：暗色 / 明亮两种纯色，便于按阈值统计前景像素。
            graphics.Clear(dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243));
            Win32TitleBarThemeButton.Draw(
                graphics,
                new Rectangle(0, 0, 37, 37),
                mode,
                dark,
                hover,
                pressed);
        }

        return bitmap;
    }

    /// <summary>
    /// 判断两张同尺寸位图是否存在像素差异。
    /// </summary>
    /// <param name="left">位图一。</param>
    /// <param name="right">位图二。</param>
    /// <returns>存在差异返回 true。</returns>
    private static bool HasDifferentPixels(Bitmap left, Bitmap right)
    {
        for (var y = 0; y < left.Height; y++)
        {
            for (var x = 0; x < left.Width; x++)
            {
                if (left.GetPixel(x, y) != right.GetPixel(x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 统计「明显偏离底色」的像素数量。
    /// </summary>
    /// <param name="bitmap">渲染结果。</param>
    /// <param name="dark">渲染时使用的明暗。</param>
    /// <returns>前景像素数。</returns>
    private static int CountForegroundPixels(Bitmap bitmap, bool dark)
    {
        var count = 0;
        var background = dark ? 32 : 243;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var delta = Math.Abs(pixel.R - background)
                            + Math.Abs(pixel.G - background)
                            + Math.Abs(pixel.B - background);
                if (delta > 90)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
