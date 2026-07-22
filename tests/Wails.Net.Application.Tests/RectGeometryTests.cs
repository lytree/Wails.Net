using TUnit.Core;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Tests;

/// <summary>
/// <see cref="Rect"/> 几何方法的单元测试（TUnit）。
/// 对应项 A：Chromium 风格多屏幕布局算法中 Rect 扩展方法。
/// </summary>
public sealed class RectGeometryTests
{
    /// <summary>
    /// Right 属性返回 X + Width。
    /// </summary>
    [Test]
    public async Task Right_ReturnsXPlusWidth()
    {
        var rect = new Rect(10, 20, 100, 50);

        await Assert.That(rect.Right).IsEqualTo(110);
    }

    /// <summary>
    /// Bottom 属性返回 Y + Height。
    /// </summary>
    [Test]
    public async Task Bottom_ReturnsYPlusHeight()
    {
        var rect = new Rect(10, 20, 100, 50);

        await Assert.That(rect.Bottom).IsEqualTo(70);
    }

    /// <summary>
    /// Origin 返回左上角点。
    /// </summary>
    [Test]
    public async Task Origin_ReturnsTopLeft()
    {
        var rect = new Rect(10, 20, 100, 50);

        var origin = rect.Origin();

        await Assert.That(origin.X).IsEqualTo(10);
        await Assert.That(origin.Y).IsEqualTo(20);
    }

    /// <summary>
    /// Corner 返回右下角点。
    /// </summary>
    [Test]
    public async Task Corner_ReturnsBottomRight()
    {
        var rect = new Rect(10, 20, 100, 50);

        var corner = rect.Corner();

        await Assert.That(corner.X).IsEqualTo(110);
        await Assert.That(corner.Y).IsEqualTo(70);
    }

    /// <summary>
    /// IsEmpty 在宽度为 0 时返回 true。
    /// </summary>
    [Test]
    public async Task IsEmpty_ZeroWidth_ReturnsTrue()
    {
        var rect = new Rect(0, 0, 0, 100);

        await Assert.That(rect.IsEmpty()).IsTrue();
    }

    /// <summary>
    /// IsEmpty 在高度为 0 时返回 true。
    /// </summary>
    [Test]
    public async Task IsEmpty_ZeroHeight_ReturnsTrue()
    {
        var rect = new Rect(0, 0, 100, 0);

        await Assert.That(rect.IsEmpty()).IsTrue();
    }

    /// <summary>
    /// IsEmpty 在负宽度时返回 true。
    /// </summary>
    [Test]
    public async Task IsEmpty_NegativeWidth_ReturnsTrue()
    {
        var rect = new Rect(0, 0, -10, 100);

        await Assert.That(rect.IsEmpty()).IsTrue();
    }

    /// <summary>
    /// IsEmpty 在正常矩形时返回 false。
    /// </summary>
    [Test]
    public async Task IsEmpty_NormalRect_ReturnsFalse()
    {
        var rect = new Rect(0, 0, 100, 100);

        await Assert.That(rect.IsEmpty()).IsFalse();
    }

    /// <summary>
    /// Contains 在点位于矩形内时返回 true。
    /// </summary>
    [Test]
    public async Task Contains_PointInside_ReturnsTrue()
    {
        var rect = new Rect(0, 0, 100, 100);

        await Assert.That(rect.Contains(new Point(50, 50))).IsTrue();
    }

    /// <summary>
    /// Contains 在点位于左上角时返回 true（边界包含原点）。
    /// </summary>
    [Test]
    public async Task Contains_PointAtOrigin_ReturnsTrue()
    {
        var rect = new Rect(0, 0, 100, 100);

        await Assert.That(rect.Contains(new Point(0, 0))).IsTrue();
    }

    /// <summary>
    /// Contains 在点位于右下角外侧时返回 false（边界不含 Right/Bottom）。
    /// </summary>
    [Test]
    public async Task Contains_PointAtCorner_ReturnsFalse()
    {
        var rect = new Rect(0, 0, 100, 100);

        await Assert.That(rect.Contains(new Point(100, 100))).IsFalse();
    }

    /// <summary>
    /// Contains 在点位于右上边缘时返回 false。
    /// </summary>
    [Test]
    public async Task Contains_PointAtRightEdge_ReturnsFalse()
    {
        var rect = new Rect(0, 0, 100, 100);

        await Assert.That(rect.Contains(new Point(100, 50))).IsFalse();
    }

    /// <summary>
    /// Contains 在点位于矩形外时返回 false。
    /// </summary>
    [Test]
    public async Task Contains_PointOutside_ReturnsFalse()
    {
        var rect = new Rect(10, 10, 50, 50);

        await Assert.That(rect.Contains(new Point(0, 0))).IsFalse();
    }

    /// <summary>
    /// Intersect 返回两矩形的交集。
    /// </summary>
    [Test]
    public async Task Intersect_Overlapping_ReturnsIntersection()
    {
        var a = new Rect(0, 0, 100, 100);
        var b = new Rect(50, 50, 100, 100);

        var intersection = a.Intersect(b);

        await Assert.That(intersection.X).IsEqualTo(50);
        await Assert.That(intersection.Y).IsEqualTo(50);
        await Assert.That(intersection.Width).IsEqualTo(50);
        await Assert.That(intersection.Height).IsEqualTo(50);
    }

    /// <summary>
    /// Intersect 无交集时返回空矩形。
    /// </summary>
    [Test]
    public async Task Intersect_NoOverlap_ReturnsEmpty()
    {
        var a = new Rect(0, 0, 50, 50);
        var b = new Rect(100, 100, 50, 50);

        var intersection = a.Intersect(b);

        await Assert.That(intersection.IsEmpty()).IsTrue();
    }

    /// <summary>
    /// Intersect 一方为空时返回空矩形。
    /// </summary>
    [Test]
    public async Task Intersect_EmptyRect_ReturnsEmpty()
    {
        var a = new Rect(0, 0, 0, 0);
        var b = new Rect(0, 0, 100, 100);

        var intersection = a.Intersect(b);

        await Assert.That(intersection.IsEmpty()).IsTrue();
    }

    /// <summary>
    /// Intersect 完全包含时返回较小矩形。
    /// </summary>
    [Test]
    public async Task Intersect_Contained_ReturnsInner()
    {
        var outer = new Rect(0, 0, 100, 100);
        var inner = new Rect(25, 25, 25, 25);

        var intersection = outer.Intersect(inner);

        await Assert.That(intersection.X).IsEqualTo(25);
        await Assert.That(intersection.Y).IsEqualTo(25);
        await Assert.That(intersection.Width).IsEqualTo(25);
        await Assert.That(intersection.Height).IsEqualTo(25);
    }

    /// <summary>
    /// DistanceFromRectSquared 相交时返回负的相交面积。
    /// </summary>
    [Test]
    public async Task DistanceFromRectSquared_Intersecting_ReturnsNegativeArea()
    {
        var a = new Rect(0, 0, 100, 100);
        var b = new Rect(50, 50, 100, 100);
        // 交集 50x50 = 2500，返回 -2500。

        var distance = a.DistanceFromRectSquared(b);

        await Assert.That(distance).IsEqualTo(-2500);
    }

    /// <summary>
    /// DistanceFromRectSquared 不相交时返回 X/Y 方向最小间距平方和。
    /// </summary>
    [Test]
    public async Task DistanceFromRectSquared_Separated_ReturnsSquaredDistance()
    {
        var a = new Rect(0, 0, 50, 50);
        var b = new Rect(60, 70, 50, 50);
        // dX = max(0, max(0-110, 60-50)) = 10；dY = max(0, max(0-120, 70-50)) = 20。
        // 距离平方 = 100 + 400 = 500。

        var distance = a.DistanceFromRectSquared(b);

        await Assert.That(distance).IsEqualTo(500);
    }

    /// <summary>
    /// DistanceFromRectSquared 边缘接触时返回 0（无间距无相交）。
    /// </summary>
    [Test]
    public async Task DistanceFromRectSquared_Touching_ReturnsZero()
    {
        var a = new Rect(0, 0, 50, 50);
        var b = new Rect(50, 0, 50, 50);
        // X 方向边缘接触，dX = 0；Y 方向重叠，dY = 0。

        var distance = a.DistanceFromRectSquared(b);

        await Assert.That(distance).IsEqualTo(0);
    }

    /// <summary>
    /// DistanceFromRectSquared 对称性：a 到 b 的距离等于 b 到 a 的距离。
    /// </summary>
    [Test]
    public async Task DistanceFromRectSquared_IsSymmetric()
    {
        var a = new Rect(0, 0, 30, 40);
        var b = new Rect(50, 60, 20, 10);

        var d1 = a.DistanceFromRectSquared(b);
        var d2 = b.DistanceFromRectSquared(a);

        await Assert.That(d1).IsEqualTo(d2);
    }
}
