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
    /// 获取矩形右边缘 X 坐标（X + Width）。
    /// 对应 Wails v3 Go 版本 <c>Rect.right()</c>。
    /// </summary>
    public int Right => X + Width;

    /// <summary>
    /// 获取矩形底边缘 Y 坐标（Y + Height）。
    /// 对应 Wails v3 Go 版本 <c>Rect.bottom()</c>。
    /// </summary>
    public int Bottom => Y + Height;

    /// <summary>
    /// 获取矩形左上角原点。
    /// 对应 Wails v3 Go 版本 <c>Rect.Origin()</c>。
    /// </summary>
    /// <returns>左上角坐标点。</returns>
    public Point Origin() => new(X, Y);

    /// <summary>
    /// 获取矩形右下角坐标。
    /// 对应 Wails v3 Go 版本 <c>Rect.Corner()</c>。
    /// </summary>
    /// <returns>右下角坐标点。</returns>
    public Point Corner() => new(Right, Bottom);

    /// <summary>
    /// 判断矩形是否为空（宽度或高度 ≤ 0）。
    /// 对应 Wails v3 Go 版本 <c>Rect.IsEmpty()</c>。
    /// </summary>
    /// <returns>为空返回 true。</returns>
    public bool IsEmpty() => Width <= 0 || Height <= 0;

    /// <summary>
    /// 判断指定点是否在矩形内。
    /// 对应 Wails v3 Go 版本 <c>Rect.Contains()</c>。
    /// </summary>
    /// <param name="point">要判断的点。</param>
    /// <returns>在矩形内返回 true。</returns>
    public bool Contains(Point point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    /// <summary>
    /// 计算与另一个矩形的交集。
    /// 对应 Wails v3 Go 版本 <c>Rect.Intersect()</c>。
    /// </summary>
    /// <param name="other">另一个矩形。</param>
    /// <returns>交集矩形；无交集时返回空矩形。</returns>
    public Rect Intersect(Rect other)
    {
        if (IsEmpty() || other.IsEmpty())
        {
            return default;
        }

        var maxLeft = Math.Max(X, other.X);
        var maxTop = Math.Max(Y, other.Y);
        var minRight = Math.Min(Right, other.Right);
        var minBottom = Math.Min(Bottom, other.Bottom);

        if (minRight > maxLeft && minBottom > maxTop)
        {
            return new Rect(maxLeft, maxTop, minRight - maxLeft, minBottom - maxTop);
        }

        return default;
    }

    /// <summary>
    /// 计算与另一个矩形的距离平方。
    /// 相交时返回负的相交面积；否则返回 X/Y 方向最小间距的平方和。
    /// 对应 Wails v3 Go 版本 <c>Rect.distanceFromRectSquared()</c>。
    /// </summary>
    /// <param name="other">另一个矩形。</param>
    /// <returns>距离平方（相交时为负面积）。</returns>
    public int DistanceFromRectSquared(Rect other)
    {
        var intersection = Intersect(other);
        if (!intersection.IsEmpty())
        {
            return -(intersection.Width * intersection.Height);
        }

        var dX = Math.Max(0, Math.Max(X - other.Right, other.X - Right));
        var dY = Math.Max(0, Math.Max(Y - other.Bottom, other.Y - Bottom));
        return dX * dX + dY * dY;
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
