namespace Wails.Net.Application.Screens;

/// <summary>
/// 表示一个矩形区域（DIP 或物理像素，取决于上下文）。
/// 对应 Wails v3 Go 版本 screenmanager.go 中的 Rect 结构。
/// </summary>
public readonly struct Rect
{
    /// <summary>
    /// X 坐标。
    /// </summary>
    public int X { get; }

    /// <summary>
    /// Y 坐标。
    /// </summary>
    public int Y { get; }

    /// <summary>
    /// 宽度。
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// 高度。
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// 使用指定坐标和尺寸构造矩形实例。
    /// </summary>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    public Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// 返回矩形的字符串表示。
    /// </summary>
    /// <returns>矩形信息字符串。</returns>
    public override string ToString() => $"({X},{Y}) {Width}x{Height}";
}

/// <summary>
/// 表示一个二维点坐标。
/// 对应 Wails v3 Go 版本 screenmanager.go 中的 Point 结构。
/// </summary>
public readonly struct Point
{
    /// <summary>
    /// X 坐标。
    /// </summary>
    public int X { get; }

    /// <summary>
    /// Y 坐标。
    /// </summary>
    public int Y { get; }

    /// <summary>
    /// 使用指定坐标构造点实例。
    /// </summary>
    /// <param name="x">X 坐标。</param>
    /// <param name="y">Y 坐标。</param>
    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// 返回点的字符串表示。
    /// </summary>
    /// <returns>点信息字符串。</returns>
    public override string ToString() => $"({X},{Y})";
}

/// <summary>
/// 表示左、右、上、下四个边距值。
/// 对应 Wails v3 Go 版本 webview_window.go 中的 LRTB 结构，
/// 用于描述窗口边框尺寸。
/// </summary>
public readonly struct LRTB
{
    /// <summary>
    /// 左边距。
    /// </summary>
    public int Left { get; }

    /// <summary>
    /// 右边距。
    /// </summary>
    public int Right { get; }

    /// <summary>
    /// 上边距。
    /// </summary>
    public int Top { get; }

    /// <summary>
    /// 下边距。
    /// </summary>
    public int Bottom { get; }

    /// <summary>
    /// 使用指定边距构造 LRTB 实例。
    /// </summary>
    /// <param name="left">左边距。</param>
    /// <param name="right">右边距。</param>
    /// <param name="top">上边距。</param>
    /// <param name="bottom">下边距。</param>
    public LRTB(int left, int right, int top, int bottom)
    {
        Left = left;
        Right = right;
        Top = top;
        Bottom = bottom;
    }

    /// <summary>
    /// 返回边距的字符串表示。
    /// </summary>
    /// <returns>边距信息字符串。</returns>
    public override string ToString() => $"L={Left} R={Right} T={Top} B={Bottom}";
}
