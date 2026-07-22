using TUnit.Core;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Tests;

/// <summary>
/// <see cref="ScreenPlacement.Apply"/> 的单元测试（TUnit）。
/// 对应项 A：Chromium 风格多屏幕布局算法中的屏幕放置应用逻辑。
/// </summary>
[NotInParallel]
public sealed class ScreenPlacementTests
{
    /// <summary>
    /// 构造一个 ScaleFactor=1 的屏幕，物理与 DIP 坐标一致。
    /// </summary>
    private static Screen CreateScreen(string id, int x, int y, int width, int height, bool isPrimary = false)
    {
        return new Screen
        {
            Id = id,
            Name = id,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            WorkAreaX = x,
            WorkAreaY = y,
            WorkAreaWidth = width,
            WorkAreaHeight = height,
            PhysicalX = x,
            PhysicalY = y,
            PhysicalWidth = width,
            PhysicalHeight = height,
            PhysicalWorkAreaX = x,
            PhysicalWorkAreaY = y,
            PhysicalWorkAreaWidth = width,
            PhysicalWorkAreaHeight = height,
            ScaleFactor = 1f,
            IsPrimary = isPrimary,
        };
    }

    /// <summary>
    /// BOTTOM 对齐 + BEGIN 偏移：子屏幕位于父屏幕下方，X 轴正向偏移。
    /// </summary>
    [Test]
    public async Task Apply_BottomBegin_OffsetPositive()
    {
        // 安排：父屏幕 (0,0) 100x100；子屏幕原始位置任意。
        var parent = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        var child = CreateScreen("C", 999, 999, 50, 50);
        var placement = new ScreenPlacement(child, parent, Alignment.Bottom, 30, OffsetReference.Begin);

        // 操作
        placement.Apply();

        // 断言：Y = parent.Y + parent.Height = 100；X = parent.X + offset = 30。
        await Assert.That(child.X).IsEqualTo(30);
        await Assert.That(child.Y).IsEqualTo(100);
    }

    /// <summary>
    /// TOP 对齐 + BEGIN 偏移：子屏幕位于父屏幕上方，Y 轴向上偏移。
    /// </summary>
    [Test]
    public async Task Apply_TopBegin_OffsetPositive()
    {
        var parent = CreateScreen("P", 50, 50, 100, 100, isPrimary: true);
        var child = CreateScreen("C", 0, 0, 50, 50);
        var placement = new ScreenPlacement(child, parent, Alignment.Top, 20, OffsetReference.Begin);

        placement.Apply();

        // Y = parent.Y - child.Height = 50 - 50 = 0；X = parent.X + offset = 70。
        await Assert.That(child.X).IsEqualTo(70);
        await Assert.That(child.Y).IsEqualTo(0);
    }

    /// <summary>
    /// RIGHT 对齐 + BEGIN 偏移：子屏幕位于父屏幕右侧，Y 轴正向偏移。
    /// </summary>
    [Test]
    public async Task Apply_RightBegin_OffsetPositive()
    {
        var parent = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        var child = CreateScreen("C", 0, 0, 50, 50);
        var placement = new ScreenPlacement(child, parent, Alignment.Right, 25, OffsetReference.Begin);

        placement.Apply();

        // X = parent.X + parent.Width = 100；Y = parent.Y + offset = 25。
        await Assert.That(child.X).IsEqualTo(100);
        await Assert.That(child.Y).IsEqualTo(25);
    }

    /// <summary>
    /// LEFT 对齐 + BEGIN 偏移：子屏幕位于父屏幕左侧，X 轴向左偏移。
    /// </summary>
    [Test]
    public async Task Apply_LeftBegin_OffsetPositive()
    {
        var parent = CreateScreen("P", 100, 0, 100, 100, isPrimary: true);
        var child = CreateScreen("C", 0, 0, 50, 50);
        var placement = new ScreenPlacement(child, parent, Alignment.Left, 30, OffsetReference.Begin);

        placement.Apply();

        // X = parent.X - child.Width = 100 - 50 = 50；Y = parent.Y + offset = 30。
        await Assert.That(child.X).IsEqualTo(50);
        await Assert.That(child.Y).IsEqualTo(30);
    }

    /// <summary>
    /// BOTTOM 对齐 + END 偏移：偏移量从父屏幕结束边反向计算。
    /// </summary>
    [Test]
    public async Task Apply_BottomEnd_OffsetFromEnd()
    {
        // 安排：父 100 宽，子 30 宽，offset=20。
        // END 时实际 offset = 100 - 20 - 30 = 50（从左数）。
        var parent = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        var child = CreateScreen("C", 0, 0, 30, 30);
        var placement = new ScreenPlacement(child, parent, Alignment.Bottom, 20, OffsetReference.End);

        placement.Apply();

        // X = parent.X + (parent.Width - offset - child.Width) = 0 + (100 - 20 - 30) = 50。
        await Assert.That(child.X).IsEqualTo(50);
        await Assert.That(child.Y).IsEqualTo(100);
    }

    /// <summary>
    /// RIGHT 对齐 + END 偏移：Y 轴偏移从父屏幕底部反向计算。
    /// </summary>
    [Test]
    public async Task Apply_RightEnd_OffsetFromEnd()
    {
        var parent = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        var child = CreateScreen("C", 0, 0, 30, 30);
        var placement = new ScreenPlacement(child, parent, Alignment.Right, 20, OffsetReference.End);

        placement.Apply();

        // X = 100；Y = (100 - 20 - 30) = 50。
        await Assert.That(child.X).IsEqualTo(100);
        await Assert.That(child.Y).IsEqualTo(50);
    }

    /// <summary>
    /// TOP 对齐 + END 偏移：Y 轴向上偏移，X 轴从右反向计算。
    /// </summary>
    [Test]
    public async Task Apply_TopEnd_OffsetFromEnd()
    {
        var parent = CreateScreen("P", 0, 100, 100, 100, isPrimary: true);
        var child = CreateScreen("C", 0, 0, 30, 50);
        var placement = new ScreenPlacement(child, parent, Alignment.Top, 10, OffsetReference.End);

        placement.Apply();

        // X = 0 + (100 - 10 - 30) = 60；Y = 100 - 50 = 50。
        await Assert.That(child.X).IsEqualTo(60);
        await Assert.That(child.Y).IsEqualTo(50);
    }

    /// <summary>
    /// LEFT 对齐 + END 偏移：X 轴向左偏移，Y 轴从底部反向计算。
    /// </summary>
    [Test]
    public async Task Apply_LeftEnd_OffsetFromEnd()
    {
        var parent = CreateScreen("P", 100, 0, 100, 100, isPrimary: true);
        var child = CreateScreen("C", 0, 0, 50, 30);
        var placement = new ScreenPlacement(child, parent, Alignment.Left, 10, OffsetReference.End);

        placement.Apply();

        // X = 100 - 50 = 50；Y = (100 - 10 - 30) = 60。
        await Assert.That(child.X).IsEqualTo(50);
        await Assert.That(child.Y).IsEqualTo(60);
    }

    /// <summary>
    /// BOTTOM 对齐 + BEGIN 偏移：偏移量超过父屏幕宽度时被夹紧到 parent.Width。
    /// </summary>
    [Test]
    public async Task Apply_BottomBegin_OffsetClampedToParentWidth()
    {
        var parent = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        var child = CreateScreen("C", 0, 0, 30, 30);
        // offset=200 > parent.Width=100，被夹紧到 100。
        var placement = new ScreenPlacement(child, parent, Alignment.Bottom, 200, OffsetReference.Begin);

        placement.Apply();

        await Assert.That(child.X).IsEqualTo(100);
        await Assert.That(child.Y).IsEqualTo(100);
    }

    /// <summary>
    /// BOTTOM 对齐 + BEGIN 偏移：负偏移绝对值超过子屏幕宽度时被夹紧到 -child.Width。
    /// </summary>
    [Test]
    public async Task Apply_BottomBegin_NegativeOffsetClampedToMinusChildWidth()
    {
        var parent = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        var child = CreateScreen("C", 0, 0, 30, 30);
        // offset=-200 < -child.Width=-30，被夹紧到 -30。
        var placement = new ScreenPlacement(child, parent, Alignment.Bottom, -200, OffsetReference.Begin);

        placement.Apply();

        await Assert.That(child.X).IsEqualTo(-30);
        await Assert.That(child.Y).IsEqualTo(100);
    }

    /// <summary>
    /// Apply 保持工作区相对偏移不变（WorkAreaX - X 差值）。
    /// </summary>
    [Test]
    public async Task Apply_PreservesWorkAreaOffset()
    {
        var parent = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        var child = CreateScreen("C", 0, 0, 50, 50);
        // 工作区相对原点偏移 (5, 5)。
        child.WorkAreaX = 5;
        child.WorkAreaY = 5;
        var placement = new ScreenPlacement(child, parent, Alignment.Bottom, 10, OffsetReference.Begin);

        placement.Apply();

        // X=10, Y=100；WorkArea 应保持 (X+5, Y+5) = (15, 105)。
        await Assert.That(child.X).IsEqualTo(10);
        await Assert.That(child.Y).IsEqualTo(100);
        await Assert.That(child.WorkAreaX).IsEqualTo(15);
        await Assert.That(child.WorkAreaY).IsEqualTo(105);
    }

    /// <summary>
    /// Apply 零偏移：子屏幕左上角与父屏幕左上角对齐（按对齐方向延伸）。
    /// </summary>
    [Test]
    public async Task Apply_ZeroOffset_TopLeftAligned()
    {
        var parent = CreateScreen("P", 100, 100, 100, 100, isPrimary: true);
        var child = CreateScreen("C", 0, 0, 50, 50);
        var placement = new ScreenPlacement(child, parent, Alignment.Bottom, 0, OffsetReference.Begin);

        placement.Apply();

        // X = 100 + 0 = 100；Y = 100 + 100 = 200。
        await Assert.That(child.X).IsEqualTo(100);
        await Assert.That(child.Y).IsEqualTo(200);
    }
}
