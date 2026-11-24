using System.Drawing;
using System.Drawing.Drawing2D;
using Wails.Net.Application.Options;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Wails.Net.Application.Platform;

/// <summary>
/// 原生标题栏主题切换按钮（Wails.Net 扩展）：在系统「最小化」按钮左侧自绘一个方形按钮，
/// 点击在「按系统 → 暗黑 → 明亮」三态间循环，并驱动应用级 <see cref="ThemeMode"/>。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么自绘 NC 区域</b>：Win32 原生标题栏的按钮由 DWM / 主题引擎绘制，没有公开的
/// 「插入自定义按钮」API，因此只能在 <c>WM_NCPAINT</c> 里于系统绘制完成后叠加绘制，
/// 并在 <c>WM_NCLBUTTONDOWN</c> / <c>WM_NCLBUTTONUP</c> 中拦截落在按钮矩形内的点击
/// （不转交 <c>DefWindowProc</c>，避免点击被解释为拖动窗口）。
/// </para>
/// <para>
/// <b>几何来源</b>：按钮位置由 <c>WM_GETTITLEBARINFOEX</c> 返回的 <c>rgrect</c> 精确推断
/// （索引 1 为最小化按钮），而不是用 <c>GetSystemMetrics</c> 手工累加边框宽度 ——
/// 后者在 Windows 10 / 11 及不同 DPI 下取值差异很大。
/// </para>
/// <para>
/// <b>绘制约束</b>：Windows 11 的标题栏可能是 Mica / 亚克力（背景由 DWM 采样壁纸合成），
/// 因此常态只绘制前景图形，不做背景填充；仅悬停 / 按下时叠加一层低透明度矩形，
/// 半透明像素由 GDI+ 与背景混合，避免出现「一块实色补丁」。
/// </para>
/// </remarks>
internal sealed class Win32TitleBarThemeButton
{
    /// <summary>
    /// <c>WM_GETTITLEBARINFOEX</c>（0x033F）：获取标题栏及其按钮矩形。
    /// </summary>
    internal const uint WmGetTitleBarInfoEx = 0x033F;

    /// <summary>
    /// <c>TITLEBARINFOEX</c> 结构大小：cbSize(4) + rcTitleBar(16) + rgstate[6](24) + rgrect[6](96)。
    /// </summary>
    internal const int TitleBarInfoExSize = 140;

    /// <summary>
    /// <c>rgrect</c> 数组相对结构起始的偏移（cbSize + rcTitleBar + rgstate）。
    /// </summary>
    internal const int CaptionRectsOffset = 44;

    /// <summary>
    /// 最小化按钮在 <c>rgrect</c> 中的索引。
    /// <para>
    /// 实测映射（Windows 11，<c>WM_GETTITLEBARINFOEX</c> 返回
    /// <c>[2]</c> 最小化、<c>[3]</c> 最大化、<c>[5]</c> 关闭，
    /// <c>[0]</c> / <c>[1]</c> / <c>[4]</c> 为 0；
    /// 因此不能按「1 = 最小化」的直觉取，否则拿到空矩形导致按钮不显示。
    /// </para>
    /// </summary>
    internal const int MinimizeButtonIndex = 2;

    /// <summary>
    /// 单个 <c>RECT</c> 的字节大小，用于按索引读取 <c>rgrect</c>。
    /// </summary>
    private const int RectSize = 16;

    private readonly HWND _hwnd;

    private bool _hover;
    private bool _pressed;

    /// <summary>
    /// 构造主题按钮。
    /// </summary>
    /// <param name="hwnd">所属窗口句柄。</param>
    /// <param name="enabled">
    /// 是否启用。<c>false</c> 时所有方法均为空操作（无边框窗口、未开启选项、非 Windows 标题栏样式）。
    /// </param>
    public Win32TitleBarThemeButton(HWND hwnd, bool enabled)
    {
        _hwnd = hwnd;
        Enabled = enabled;
    }

    /// <summary>
    /// 按钮是否启用。关闭后不绘制、不响应命中。
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    /// 按主题模式循环切换：按系统 → 暗黑 → 明亮 → 按系统。
    /// </summary>
    /// <param name="mode">当前模式。</param>
    /// <returns>下一模式。</returns>
    /// <remarks>
    /// 顺序由跨平台的 <see cref="ThemeModeCycle"/> 统一维护，三个平台共用同一规则。
    /// </remarks>
    internal static ThemeMode Cycle(ThemeMode mode) => ThemeModeCycle.Next(mode);

    /// <summary>
    /// 由窗口矩形、标题栏矩形、最小化按钮矩形推算本按钮矩形（全部为屏幕坐标）。
    /// </summary>
    /// <param name="titleBarRect">标题栏矩形（<c>rcTitleBar</c>）。</param>
    /// <param name="minimizeButtonRect">最小化按钮矩形（<c>rgrect[1]</c>）。</param>
    /// <param name="rect">推算出的按钮矩形，屏幕坐标。</param>
    /// <returns>可用返回 true；标题栏无按钮或空间不足返回 false。</returns>
    /// <remarks>
    /// 规则：以最小化按钮高度为边长做正方形，紧贴其左侧；
    /// 要求左侧至少还能留出两个按钮宽度的空间（一个给窗口图标、一个给标题文字），
    /// 否则宁可不显示，避免遮挡标题。
    /// </remarks>
    internal static bool TryComputeLayout(RECT titleBarRect, RECT minimizeButtonRect, out RECT rect)
    {
        rect = default;

        var height = minimizeButtonRect.bottom - minimizeButtonRect.top;
        var width = minimizeButtonRect.right - minimizeButtonRect.left;
        if (height <= 0 || width <= 0)
        {
            return false;
        }

        var size = height;
        var left = minimizeButtonRect.left - size;

        // 标题栏太小（窗口被压窄 / 非标准标题栏）时不显示。
        if (left - titleBarRect.left < size * 2)
        {
            return false;
        }

        if (size < 12)
        {
            return false;
        }

        rect = new RECT
        {
            left = left,
            top = minimizeButtonRect.top,
            right = minimizeButtonRect.left,
            bottom = minimizeButtonRect.bottom,
        };
        return true;
    }

    /// <summary>
    /// 在窗口非客户区 DC 上绘制按钮。应在 <c>DefWindowProc(WM_NCPAINT)</c> 之后调用，
    /// 以覆盖系统已绘制的标题栏。
    /// </summary>
    /// <param name="hdc">窗口 DC（<c>GetWindowDC</c> 取得，原点为窗口左上角含边框）。</param>
    /// <param name="mode">当前主题模式，决定图标形状。</param>
    /// <param name="dark">当前实际明暗，决定前景 / 悬停颜色。</param>
    /// <returns>实际绘制返回 true。</returns>
    public bool Paint(HDC hdc, ThemeMode mode, bool dark)
    {
        if (!Enabled || _hwnd.IsNull)
        {
            return false;
        }

        if (!TryGetScreenRect(out var screenRect))
        {
            return false;
        }

        if (!PInvoke.GetWindowRect(_hwnd, out var windowRect))
        {
            return false;
        }

        // 屏幕坐标 → 窗口 DC 坐标（窗口 DC 原点包含非客户区）。
        var rect = new Rectangle(
            screenRect.left - windowRect.left,
            screenRect.top - windowRect.top,
            screenRect.right - screenRect.left,
            screenRect.bottom - screenRect.top);

        if (rect.Width < 12 || rect.Height < 12)
        {
            return false;
        }

        try
        {
            using var graphics = Graphics.FromHdc((IntPtr)hdc);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Draw(graphics, rect, mode, dark, _hover, _pressed);
            return true;
        }
        catch (Exception ex)
        {
            // 绘制失败（DC 失效、GDI+ 资源不足等）不应影响窗口消息处理。
            System.Diagnostics.Debug.WriteLine($"[Wails.Net] 主题按钮绘制失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 判断屏幕坐标点是否落在按钮内。
    /// </summary>
    /// <param name="x">屏幕 X 坐标。</param>
    /// <param name="y">屏幕 Y 坐标。</param>
    /// <returns>命中返回 true。</returns>
    public bool Contains(int x, int y)
    {
        if (!Enabled)
        {
            return false;
        }

        if (!TryGetScreenRect(out var rect))
        {
            return false;
        }

        return x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom;
    }

    /// <summary>
    /// 处理 <c>WM_NCLBUTTONDOWN</c>：命中按钮则进入按下态并消费消息（阻止窗口拖动）。
    /// </summary>
    /// <param name="x">屏幕 X 坐标。</param>
    /// <param name="y">屏幕 Y 坐标。</param>
    /// <returns>已消费返回 true。</returns>
    public bool HandleButtonDown(int x, int y)
    {
        if (!Contains(x, y))
        {
            _pressed = false;
            return false;
        }

        _pressed = true;
        _hover = true;
        return true;
    }

    /// <summary>
    /// 处理 <c>WM_NCLBUTTONUP</c>：在按钮上抬起则切换主题模式。
    /// </summary>
    /// <param name="x">屏幕 X 坐标。</param>
    /// <param name="y">屏幕 Y 坐标。</param>
    /// <returns>已消费返回 true。</returns>
    public bool HandleButtonUp(int x, int y)
    {
        if (!_pressed)
        {
            return false;
        }

        var inside = Contains(x, y);
        _pressed = false;

        if (!inside)
        {
            return false;
        }

        var app = Application.Get();
        if (app is not null)
        {
            app.SetThemeMode(Cycle(app.ThemeMode));
        }

        return true;
    }

    /// <summary>
    /// 处理 <c>WM_NCMOUSEMOVE</c>：刷新悬停态，指针移出后同时清除按下态
    /// （在按钮上按下后拖出再松开不会触发切换）。
    /// </summary>
    /// <param name="x">屏幕 X 坐标。</param>
    /// <param name="y">屏幕 Y 坐标。</param>
    /// <returns>悬停态发生变化返回 true（调用方据此重绘非客户区）。</returns>
    public bool UpdateHover(int x, int y)
    {
        if (!Enabled)
        {
            return false;
        }

        var inside = Contains(x, y);
        if (!inside)
        {
            _pressed = false;
        }

        if (_hover == inside)
        {
            return false;
        }

        _hover = inside;
        return true;
    }

    /// <summary>
    /// 清除悬停 / 按下态（窗口失活、尺寸变化等场景）。
    /// </summary>
    /// <returns>状态发生变化返回 true。</returns>
    public bool ResetState()
    {
        if (!_hover && !_pressed)
        {
            return false;
        }

        _hover = false;
        _pressed = false;
        return true;
    }

    /// <summary>
    /// 取按钮矩形（屏幕坐标）。
    /// </summary>
    /// <param name="rect">按钮矩形。</param>
    /// <returns>可用返回 true。</returns>
    private unsafe bool TryGetScreenRect(out RECT rect)
    {
        rect = default;

        if (_hwnd.IsNull)
        {
            return false;
        }

        var buffer = stackalloc byte[TitleBarInfoExSize];
        new Span<byte>(buffer, TitleBarInfoExSize).Clear();

        // cbSize 必须先填，否则系统直接返回失败。
        *(int*)buffer = TitleBarInfoExSize;

        _ = PInvoke.SendMessage(_hwnd, WmGetTitleBarInfoEx, default, (LPARAM)(nint)buffer);

        var titleBarRect = *(RECT*)(buffer + sizeof(int));
        var minimizeRect = *(RECT*)(buffer + CaptionRectsOffset + (MinimizeButtonIndex * RectSize));

        return TryComputeLayout(titleBarRect, minimizeRect, out rect);
    }

    /// <summary>
    /// 实际绘制：悬停 / 按下背景 + 与当前模式对应的图标。
    /// </summary>
    /// <param name="graphics">窗口 DC 上的绘图上下文。</param>
    /// <param name="rect">按钮矩形（窗口 DC 坐标）。</param>
    /// <param name="mode">主题模式。</param>
    /// <param name="dark">实际明暗。</param>
    /// <param name="hover">是否悬停。</param>
    /// <param name="pressed">是否按下。</param>
    internal static void Draw(Graphics graphics, Rectangle rect, ThemeMode mode, bool dark, bool hover, bool pressed)
    {
        var foreground = dark ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);

        if (pressed || hover)
        {
            // 低透明度叠加：暗色主题用白、明亮主题用黑，与 Mica / 亚克力背景自然混合。
            var alpha = pressed ? 46 : (dark ? 26 : 18);
            using var hoverBrush = new SolidBrush(dark
                ? Color.FromArgb(alpha, 255, 255, 255)
                : Color.FromArgb(alpha, 0, 0, 0));
            using var hoverPath = CreateRoundedRect(rect, 4f);
            graphics.FillPath(hoverBrush, hoverPath);
        }

        // 图标区域：按钮内居中的正方形，边长约为按钮高度的 42%。
        var size = Math.Min(rect.Width, rect.Height) * 0.42f;
        var centerX = rect.X + (rect.Width / 2f);
        var centerY = rect.Y + (rect.Height / 2f);
        var iconRect = new RectangleF(centerX - (size / 2f), centerY - (size / 2f), size, size);

        using var foregroundBrush = new SolidBrush(foreground);
        using var pen = new Pen(foreground, Math.Max(1.2f, size * 0.075f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        switch (mode)
        {
            case ThemeMode.Light:
                DrawSun(graphics, iconRect, foregroundBrush, pen);
                break;

            case ThemeMode.Dark:
                DrawMoon(graphics, iconRect, foregroundBrush, pen);
                break;

            default:
                DrawAuto(graphics, iconRect, foregroundBrush, pen);
                break;
        }
    }

    /// <summary>
    /// 明亮模式图标：实心圆 + 八道光芒。
    /// </summary>
    private static void DrawSun(Graphics graphics, RectangleF iconRect, Brush brush, Pen pen)
    {
        var centerX = iconRect.X + (iconRect.Width / 2f);
        var centerY = iconRect.Y + (iconRect.Height / 2f);
        var coreRadius = iconRect.Width * 0.26f;

        graphics.FillEllipse(
            brush,
            centerX - coreRadius,
            centerY - coreRadius,
            coreRadius * 2f,
            coreRadius * 2f);

        var innerRadius = iconRect.Width * 0.38f;
        var outerRadius = iconRect.Width * 0.5f;
        for (var i = 0; i < 8; i++)
        {
            var angle = Math.PI * i / 4d;
            var dx = (float)Math.Cos(angle);
            var dy = (float)Math.Sin(angle);
            graphics.DrawLine(
                pen,
                centerX + (dx * innerRadius),
                centerY + (dy * innerRadius),
                centerX + (dx * outerRadius),
                centerY + (dy * outerRadius));
        }
    }

    /// <summary>
    /// 暗黑模式图标：以「椭圆区域减去错位椭圆」得到弯月。
    /// </summary>
    /// <remarks>
    /// 必须先把椭圆加进 <see cref="GraphicsPath"/> 再构造 <see cref="Region"/>，
    /// 否则 <c>new Region(RectangleF)</c> 得到的是矩形区域，画出来是「方块挖角」而非弯月。
    /// </remarks>
    private static void DrawMoon(Graphics graphics, RectangleF iconRect, Brush brush, Pen pen)
    {
        var offset = iconRect.Width * 0.42f;

        using var outerPath = new GraphicsPath();
        outerPath.AddEllipse(iconRect);

        using var crescent = new Region(outerPath);

        // 右上方偏移的等径圆挖掉一块，形成弯月。
        using var punchedPath = new GraphicsPath();
        punchedPath.AddEllipse(new RectangleF(
            iconRect.X + offset,
            iconRect.Y - (offset * 0.55f),
            iconRect.Width,
            iconRect.Height));
        crescent.Exclude(punchedPath);

        graphics.FillRegion(brush, crescent);
    }

    /// <summary>
    /// 跟随系统图标：圆环 + 右半填充（「自动 / 半明半暗」的通用表达）。
    /// </summary>
    private static void DrawAuto(Graphics graphics, RectangleF iconRect, Brush brush, Pen pen)
    {
        graphics.FillPie(brush, iconRect.X, iconRect.Y, iconRect.Width, iconRect.Height, -90f, 180f);
        graphics.DrawEllipse(pen, iconRect);
    }

    /// <summary>
    /// 构造圆角矩形路径。
    /// </summary>
    private static GraphicsPath CreateRoundedRect(Rectangle rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2f;

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180f, 90f);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270f, 90f);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}
