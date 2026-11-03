using TUnit.Core;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// <see cref="LinuxDipCoordinates"/> 的单元测试（TUnit）。
/// 覆盖 Wails v3 PR #6346 的 DIP / 物理像素换算与多显示器目标选择语义。
/// 均为纯函数测试，不需要 GTK 或 GUI 环境；与同目录用例保持一致，标记为不并行执行。
/// </summary>
[NotInParallel]
public sealed class LinuxDipCoordinatesTests
{
    /// <summary>
    /// DIP 到物理像素按缩放因子放大（向下取整）。
    /// </summary>
    [Test]
    [Arguments(1.0, 100, 100)]
    [Arguments(1.25, 100, 125)]
    [Arguments(1.5, 100, 150)]
    [Arguments(2.0, 100, 200)]
    public async Task DipToPhysical_AtCommonScaleFactors_ScalesByFactor(
        double scaleFactor, int dipValue, int expected)
    {
        // 操作与断言
        await Assert.That(LinuxDipCoordinates.DipToPhysical(dipValue, scaleFactor))
            .IsEqualTo(expected);
    }

    /// <summary>
    /// 物理像素到 DIP 按缩放因子缩小（向上取整，避免往返漂移）。
    /// </summary>
    [Test]
    [Arguments(1.0, 100, 100)]
    [Arguments(1.25, 100, 80)]
    [Arguments(1.5, 100, 67)]
    [Arguments(2.0, 100, 50)]
    public async Task PhysicalToDip_AtCommonScaleFactors_ScalesByFactor(
        double scaleFactor, int physicalValue, int expected)
    {
        // 操作与断言
        await Assert.That(LinuxDipCoordinates.PhysicalToDip(physicalValue, scaleFactor))
            .IsEqualTo(expected);
    }

    /// <summary>
    /// 1.0 / 1.25 / 1.5 / 2.0 缩放下 DIP → 物理 → DIP 的往返换算应还原原值，
    /// 包含 0、正坐标与负坐标（多显示器位于主屏左侧/上方时坐标可为负）。
    /// </summary>
    [Test]
    [Arguments(1.0)]
    [Arguments(1.25)]
    [Arguments(1.5)]
    [Arguments(2.0)]
    public async Task DipToPhysical_RoundTrips_AtCommonScaleFactors(double scaleFactor)
    {
        // 安排：覆盖零值、常见尺寸与负坐标
        var values = new[] { 0, 1, 37, 100, 1280, 1920, -1, -2, -120, -1512 };

        // 操作与断言：DIP → 物理 → DIP 应逐值还原
        foreach (var value in values)
        {
            var physical = LinuxDipCoordinates.DipToPhysical(value, scaleFactor);
            var roundTrip = LinuxDipCoordinates.PhysicalToDip(physical, scaleFactor);
            await Assert.That(roundTrip).IsEqualTo(value);
        }
    }

    /// <summary>
    /// 物理像素恰好是缩放的整数倍时，物理 → DIP → 物理的往返换算应精确还原。
    /// 非整数倍的物理像素在放大后会丢失亚像素信息（数学上不可逆），故不在本用例范围内。
    /// </summary>
    [Test]
    [Arguments(1.0)]
    [Arguments(1.25)]
    [Arguments(1.5)]
    [Arguments(2.0)]
    public async Task PhysicalToDip_ExactMultiples_RoundTripsAtCommonScaleFactors(double scaleFactor)
    {
        // 安排：150 / 1920 在 1.25、1.5、2.0 下均可被整除
        var values = new[] { 0, 150, 1920, -150, -1920 };

        // 操作与断言
        foreach (var value in values)
        {
            var dip = LinuxDipCoordinates.PhysicalToDip(value, scaleFactor);
            var roundTrip = LinuxDipCoordinates.DipToPhysical(dip, scaleFactor);
            await Assert.That(roundTrip).IsEqualTo(value);
        }
    }

    /// <summary>
    /// 无效缩放因子（0、负数、NaN、Infinity）回退为 1.0，换算结果保持不变。
    /// </summary>
    [Test]
    [Arguments(0.0)]
    [Arguments(-1.5)]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    public async Task DipToPhysical_InvalidScaleFactor_FallsBackToOne(double scaleFactor)
    {
        // 断言：归一化结果为 1.0
        await Assert.That(LinuxDipCoordinates.NormalizeScaleFactor(scaleFactor)).IsEqualTo(1.0);

        // 断言：换算退化为恒等换算
        await Assert.That(LinuxDipCoordinates.DipToPhysical(120, scaleFactor)).IsEqualTo(120);
        await Assert.That(LinuxDipCoordinates.DipToPhysical(-120, scaleFactor)).IsEqualTo(-120);
        await Assert.That(LinuxDipCoordinates.PhysicalToDip(120, scaleFactor)).IsEqualTo(120);
        await Assert.That(LinuxDipCoordinates.PhysicalToDip(-120, scaleFactor)).IsEqualTo(-120);
    }

    /// <summary>
    /// 负坐标的舍入方向：DIP → 物理向下取整，物理 → DIP 向上取整，保证往返还原。
    /// </summary>
    [Test]
    public async Task DipToPhysical_NegativeValues_UseFloorRounding()
    {
        // 断言：-1 * 1.5 = -1.5 → 向下取整 -2
        await Assert.That(LinuxDipCoordinates.DipToPhysical(-1, 1.5)).IsEqualTo(-2);

        // 断言：-120 * 1.25 = -150（精确值）
        await Assert.That(LinuxDipCoordinates.DipToPhysical(-120, 1.25)).IsEqualTo(-150);

        // 断言：-2 / 1.5 ≈ -1.333 → 向上取整 -1
        await Assert.That(LinuxDipCoordinates.PhysicalToDip(-2, 1.5)).IsEqualTo(-1);
    }

    /// <summary>
    /// 点落在第二个显示器（不同缩放因子）内时，选择该显示器以取其缩放因子。
    /// </summary>
    [Test]
    public async Task SelectTargetMonitor_PointInsideSecondMonitor_ReturnsThatMonitor()
    {
        // 安排：1x 主显示器 + 右侧 2x 显示器
        var monitors = new[]
        {
            new LinuxMonitorInfo(0, 0, 1920, 1080, 1.0, isPrimary: true),
            new LinuxMonitorInfo(1920, 0, 1512, 982, 2.0),
        };

        // 操作
        var target = LinuxDipCoordinates.SelectTargetMonitor(monitors, 2000, 100);

        // 断言：命中右侧 2x 显示器
        await Assert.That(target.HasValue).IsTrue();
        var selected = target.GetValueOrDefault();
        await Assert.That(selected.DipX).IsEqualTo(1920);
        await Assert.That(selected.ScaleFactor).IsEqualTo(2.0);
    }

    /// <summary>
    /// 位于主显示器左侧（负 X 原点）的显示器也能被正确选中。
    /// </summary>
    [Test]
    public async Task SelectTargetMonitor_PointInsideMonitorWithNegativeOrigin_ReturnsThatMonitor()
    {
        // 安排：2x 主显示器 + 左侧 1x 显示器
        var monitors = new[]
        {
            new LinuxMonitorInfo(0, 0, 1512, 982, 2.0, isPrimary: true),
            new LinuxMonitorInfo(-1920, 0, 1920, 1080, 1.0),
        };

        // 操作
        var target = LinuxDipCoordinates.SelectTargetMonitor(monitors, -500, 100);

        // 断言
        await Assert.That(target.HasValue).IsTrue();
        var selected = target.GetValueOrDefault();
        await Assert.That(selected.DipX).IsEqualTo(-1920);
        await Assert.That(selected.ScaleFactor).IsEqualTo(1.0);
    }

    /// <summary>
    /// 显示器边界为左闭右开：共享边界上的点归属于右侧显示器。
    /// </summary>
    [Test]
    public async Task SelectTargetMonitor_PointOnSharedEdge_BelongsToNextMonitor()
    {
        // 安排
        var monitors = new[]
        {
            new LinuxMonitorInfo(0, 0, 1920, 1080, 1.0, isPrimary: true),
            new LinuxMonitorInfo(1920, 0, 1512, 982, 2.0),
        };

        // 操作：点恰好位于两屏共享边
        var target = LinuxDipCoordinates.SelectTargetMonitor(monitors, 1920, 500);

        // 断言
        await Assert.That(target.HasValue).IsTrue();
        await Assert.That(target.GetValueOrDefault().DipX).IsEqualTo(1920);
    }

    /// <summary>
    /// 点不在任何显示器内时，回退为距离最近的显示器。
    /// </summary>
    [Test]
    public async Task SelectTargetMonitor_PointOutsideAllMonitors_ReturnsNearestMonitor()
    {
        // 安排
        var monitors = new[]
        {
            new LinuxMonitorInfo(0, 0, 1920, 1080, 1.0, isPrimary: true),
            new LinuxMonitorInfo(1920, 0, 1512, 982, 2.0),
        };

        // 操作：点位于主显示器左上方外部
        var left = LinuxDipCoordinates.SelectTargetMonitor(monitors, -100, -100);

        // 断言：最近的是主显示器
        await Assert.That(left.HasValue).IsTrue();
        await Assert.That(left.GetValueOrDefault().DipX).IsEqualTo(0);

        // 操作：点位于右侧显示器右方外部
        var right = LinuxDipCoordinates.SelectTargetMonitor(monitors, 3600, 500);

        // 断言：最近的是右侧显示器
        await Assert.That(right.HasValue).IsTrue();
        var selected = right.GetValueOrDefault();
        await Assert.That(selected.DipX).IsEqualTo(1920);
        await Assert.That(selected.ScaleFactor).IsEqualTo(2.0);
    }

    /// <summary>
    /// 显示器列表为空或为 null 时返回 null，调用方回退到默认缩放因子 1.0。
    /// </summary>
    [Test]
    public async Task SelectTargetMonitor_EmptyOrNullList_ReturnsNull()
    {
        // 断言：空列表
        await Assert.That(
            LinuxDipCoordinates.SelectTargetMonitor(Array.Empty<LinuxMonitorInfo>(), 0, 0).HasValue)
            .IsFalse();

        // 断言：null 列表
        await Assert.That(
            LinuxDipCoordinates.SelectTargetMonitor(null, 0, 0).HasValue)
            .IsFalse();
    }

    /// <summary>
    /// 构造显示器信息时无效缩放因子会被归一化为 1.0。
    /// </summary>
    [Test]
    public async Task LinuxMonitorInfo_NonPositiveScaleFactor_NormalizesToOne()
    {
        // 操作
        var monitor = new LinuxMonitorInfo(0, 0, 100, 100, 0);

        // 断言
        await Assert.That(monitor.ScaleFactor).IsEqualTo(1.0);
    }
}
