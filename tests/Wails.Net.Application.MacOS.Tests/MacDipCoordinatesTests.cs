using TUnit.Core;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.MacOS.Tests;

/// <summary>
/// <see cref="MacDipCoordinates"/> 的单元测试（TUnit）。
/// 覆盖 Wails v3 PR #6346 的 macOS DIP / 物理像素换算、top-left ↔ bottom-left Y 翻转
/// 与多显示器目标选择语义；均为纯函数测试，不需要 AppKit 或 GUI 环境。
/// </summary>
[NotInParallel]
public sealed class MacDipCoordinatesTests
{
    /// <summary>
    /// DIP 到物理像素按 backingScaleFactor 放大（向下取整）。
    /// </summary>
    [Test]
    [Arguments(1.0, 100, 100)]
    [Arguments(1.25, 100, 125)]
    [Arguments(1.5, 100, 150)]
    [Arguments(2.0, 100, 200)]
    public async Task DipToPhysical_AtCommonScaleFactors_ScalesByFactor(
        double scaleFactor, int dipValue, int expected)
    {
        await Assert.That(MacDipCoordinates.DipToPhysical(dipValue, scaleFactor))
            .IsEqualTo(expected);
    }

    /// <summary>
    /// 物理像素到 DIP 按 backingScaleFactor 缩小（向上取整，避免往返漂移）。
    /// </summary>
    [Test]
    [Arguments(1.0, 100, 100)]
    [Arguments(1.25, 100, 80)]
    [Arguments(1.5, 100, 67)]
    [Arguments(2.0, 100, 50)]
    public async Task PhysicalToDip_AtCommonScaleFactors_ScalesByFactor(
        double scaleFactor, int physicalValue, int expected)
    {
        await Assert.That(MacDipCoordinates.PhysicalToDip(physicalValue, scaleFactor))
            .IsEqualTo(expected);
    }

    /// <summary>
    /// Retina 常见缩放下 DIP → 物理 → DIP 往返应还原原值，包含 0、正坐标与负坐标。
    /// </summary>
    [Test]
    [Arguments(1.0)]
    [Arguments(1.25)]
    [Arguments(1.5)]
    [Arguments(2.0)]
    public async Task DipToPhysical_RoundTrips_AtCommonScaleFactors(double scaleFactor)
    {
        var values = new[] { 0, 1, 37, 100, 1280, 1920, -1, -2, -120, -1512 };

        foreach (var value in values)
        {
            var physical = MacDipCoordinates.DipToPhysical(value, scaleFactor);
            var roundTrip = MacDipCoordinates.PhysicalToDip(physical, scaleFactor);
            await Assert.That(roundTrip).IsEqualTo(value);
        }
    }

    /// <summary>
    /// 物理像素恰好是缩放的整数倍时，物理 → DIP → 物理的往返换算应精确还原。
    /// </summary>
    [Test]
    [Arguments(1.0, 100)]
    [Arguments(1.25, 125)]
    [Arguments(1.5, 150)]
    [Arguments(2.0, 200)]
    public async Task PhysicalToDip_RoundTrips_AtExactMultiples(double scaleFactor, int physicalValue)
    {
        var dip = MacDipCoordinates.PhysicalToDip(physicalValue, scaleFactor);
        await Assert.That(MacDipCoordinates.DipToPhysical(dip, scaleFactor)).IsEqualTo(physicalValue);
    }

    /// <summary>
    /// 非有限值或非正缩放因子回退为 1.0。
    /// </summary>
    [Test]
    [Arguments(0.0)]
    [Arguments(-1.0)]
    [Arguments(-0.5)]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    public async Task NormalizeScaleFactor_InvalidValues_FallBackToDefault(double scaleFactor)
    {
        await Assert.That(MacDipCoordinates.NormalizeScaleFactor(scaleFactor))
            .IsEqualTo(MacDipCoordinates.DefaultScaleFactor);
    }

    /// <summary>
    /// 合法缩放因子保持原值；无效因子参与换算时等价于 1.0（标识映射）。
    /// </summary>
    [Test]
    public async Task NormalizeScaleFactor_ValidValue_IsPreserved()
    {
        await Assert.That(MacDipCoordinates.NormalizeScaleFactor(1.25)).IsEqualTo(1.25);
        await Assert.That(MacDipCoordinates.DipToPhysical(37, 0)).IsEqualTo(37);
        await Assert.That(MacDipCoordinates.DipToPhysical(37, double.NaN)).IsEqualTo(37);
        await Assert.That(MacDipCoordinates.PhysicalToDip(37, -1)).IsEqualTo(37);
    }

    /// <summary>
    /// top-left 原点 DIP 的 Y 坐标翻转为主屏高参照的 AppKit bottom-left Y 坐标。
    /// </summary>
    [Test]
    [Arguments(1080, 100, 0, 980)]
    [Arguments(1080, 100, 100, 880)]
    [Arguments(1080, 100, 980, 0)]
    [Arguments(1080, 100, -50, 1030)]
    public async Task ToAppKitBottomLeftY_ConvertsTopLeftToBottomLeft(
        int primaryHeight, int height, int topY, int expected)
    {
        await Assert.That(MacDipCoordinates.ToAppKitBottomLeftY(topY, height, primaryHeight))
            .IsEqualTo(expected);
    }

    /// <summary>
    /// AppKit bottom-left Y 与 top-left DIP Y 的换算应严格可逆（保证位置读写往返一致）。
    /// </summary>
    [Test]
    public async Task ToDipTopLeftY_IsInverseOfToAppKitBottomLeftY()
    {
        const int primaryHeight = 1152;
        var heights = new[] { 0, 1, 100, 600, 1152 };

        foreach (var height in heights)
        {
            for (var topY = -100; topY <= primaryHeight + 100; topY += 50)
            {
                var appKitY = MacDipCoordinates.ToAppKitBottomLeftY(topY, height, primaryHeight);
                var roundTrip = MacDipCoordinates.ToDipTopLeftY(appKitY, height, primaryHeight);
                await Assert.That(roundTrip).IsEqualTo(topY);
            }
        }
    }

    /// <summary>
    /// 居中计算在 top-left 原点 DIP 工作区内完成。
    /// </summary>
    [Test]
    [Arguments(0, 0, 1920, 1040, 800, 600, 560, 220)]
    [Arguments(-1920, 0, 1920, 1040, 800, 600, -1360, 220)]
    [Arguments(0, -200, 1920, 1040, 800, 600, 560, 20)]
    public async Task CentreDipBounds_CentresWithinWorkArea(
        int workX, int workY, int workWidth, int workHeight,
        int windowWidth, int windowHeight, int expectedX, int expectedY)
    {
        var workArea = new MacMonitorInfo(workX, workY, workWidth, workHeight, 1.0);

        var (x, y) = MacDipCoordinates.CentreDipBounds(workArea, windowWidth, windowHeight);

        await Assert.That(x).IsEqualTo(expectedX);
        await Assert.That(y).IsEqualTo(expectedY);
    }

    /// <summary>
    /// 窗口大于工作区时居中结果为负偏移（不裁剪，交由窗口管理器处理）。
    /// </summary>
    [Test]
    public async Task CentreDipBounds_WindowLargerThanWorkArea_ReturnsNegativeOffset()
    {
        var workArea = new MacMonitorInfo(0, 0, 1920, 1040, 2.0);

        var (x, y) = MacDipCoordinates.CentreDipBounds(workArea, 2000, 1100);

        await Assert.That(x).IsEqualTo(-40);
        await Assert.That(y).IsEqualTo(-30);
    }

    /// <summary>
    /// 命中包含该 DIP 点的显示器时返回其索引（重叠歧义取第一个）。
    /// </summary>
    [Test]
    public async Task SelectTargetMonitorIndex_PointInsideMonitor_ReturnsThatMonitor()
    {
        var monitors = new[]
        {
            new MacMonitorInfo(0, 0, 1920, 1080, 2.0),
            new MacMonitorInfo(1920, 0, 2560, 1440, 1.0),
        };

        await Assert.That(MacDipCoordinates.SelectTargetMonitorIndex(monitors, 100, 100)).IsEqualTo(0);
        await Assert.That(MacDipCoordinates.SelectTargetMonitorIndex(monitors, 2000, 100)).IsEqualTo(1);
        await Assert.That(MacDipCoordinates.SelectTargetMonitorIndex(monitors, 1920, 0)).IsEqualTo(1);
    }

    /// <summary>
    /// 未命中任何显示器时返回最近的显示器（支持负原点）。
    /// </summary>
    [Test]
    public async Task SelectTargetMonitorIndex_PointOutsideAllMonitors_ReturnsNearest()
    {
        var monitors = new[]
        {
            new MacMonitorInfo(0, 0, 1920, 1080, 2.0),
            new MacMonitorInfo(-2560, -200, 2560, 1440, 1.0),
        };

        await Assert.That(MacDipCoordinates.SelectTargetMonitorIndex(monitors, -3000, -300)).IsEqualTo(1);
        await Assert.That(MacDipCoordinates.SelectTargetMonitorIndex(monitors, 3000, 500)).IsEqualTo(0);
    }

    /// <summary>
    /// 无显示器（null 或空列表）时返回 -1。
    /// </summary>
    [Test]
    public async Task SelectTargetMonitorIndex_NoMonitors_ReturnsMinusOne()
    {
        await Assert.That(MacDipCoordinates.SelectTargetMonitorIndex(null, 0, 0)).IsEqualTo(-1);
        await Assert.That(MacDipCoordinates.SelectTargetMonitorIndex(Array.Empty<MacMonitorInfo>(), 0, 0)).IsEqualTo(-1);
    }

    /// <summary>
    /// 显示器边界为左闭右开、上闭下开。
    /// </summary>
    [Test]
    public async Task MacMonitorInfo_ContainsDipPoint_UsesExclusiveRightAndBottomEdges()
    {
        var monitor = new MacMonitorInfo(0, 0, 1920, 1080, 1.0);

        await Assert.That(monitor.ContainsDipPoint(0, 0)).IsTrue();
        await Assert.That(monitor.ContainsDipPoint(1919, 1079)).IsTrue();
        await Assert.That(monitor.ContainsDipPoint(1920, 500)).IsFalse();
        await Assert.That(monitor.ContainsDipPoint(500, 1080)).IsFalse();
        await Assert.That(monitor.ContainsDipPoint(-1, 500)).IsFalse();
    }

    /// <summary>
    /// <see cref="MacMonitorInfo"/> 构造时归一化缩放因子，并计算点到矩形的距离平方。
    /// </summary>
    [Test]
    public async Task MacMonitorInfo_InvalidScaleFactor_IsNormalized()
    {
        var monitor = new MacMonitorInfo(0, 0, 1920, 1080, double.NaN);

        await Assert.That(monitor.ScaleFactor).IsEqualTo(MacDipCoordinates.DefaultScaleFactor);
        await Assert.That(new MacMonitorInfo(0, 0, 1920, 1080, -2.0).ScaleFactor)
            .IsEqualTo(MacDipCoordinates.DefaultScaleFactor);
        await Assert.That(new MacMonitorInfo(0, 0, 1920, 1080, 2.0).ScaleFactor).IsEqualTo(2.0);
    }

    /// <summary>
    /// 距离平方：点在矩形内为 0，点在矩形外按最近边计算。
    /// </summary>
    [Test]
    public async Task MacMonitorInfo_DistanceSquaredTo_ComputesNearestEdgeDistance()
    {
        var monitor = new MacMonitorInfo(0, 0, 1920, 1080, 1.0);

        await Assert.That(monitor.DistanceSquaredTo(500, 500)).IsEqualTo(0L);
        await Assert.That(monitor.DistanceSquaredTo(1930, 1090)).IsEqualTo(200L);
        await Assert.That(monitor.DistanceSquaredTo(-30, -40)).IsEqualTo(2500L);
    }
}
