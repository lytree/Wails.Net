using TUnit.Core;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Tests;

/// <summary>
/// ScreenManager DPI 转换 API 的单元测试（TUnit）。
/// 对应项 4：DipToPhysicalPoint/PhysicalToDipPoint/DipToPhysicalRect/PhysicalToDipRect/ScreenNearest*。
/// </summary>
[NotInParallel]
public sealed class ScreenManagerDpiTests
{
    /// <summary>
    /// 构造 150% DPI 的主屏幕用于测试。
    /// 物理分辨率 1920x1080，DIP 分辨率 1280x720。
    /// </summary>
    private static Screen Create150PercentScreen()
    {
        return new Screen
        {
            Name = "Main",
            X = 0,
            Y = 0,
            Width = 1280,
            Height = 720,
            WorkAreaX = 0,
            WorkAreaY = 0,
            WorkAreaWidth = 1280,
            WorkAreaHeight = 700,
            PhysicalX = 0,
            PhysicalY = 0,
            PhysicalWidth = 1920,
            PhysicalHeight = 1080,
            PhysicalWorkAreaX = 0,
            PhysicalWorkAreaY = 0,
            PhysicalWorkAreaWidth = 1920,
            PhysicalWorkAreaHeight = 1050,
            ScaleFactor = 1.5f,
            IsPrimary = true,
        };
    }

    /// <summary>
    /// Screen.Scale 从 DIP 缩放到物理像素时向下取整。
    /// </summary>
    [Test]
    public async Task Scale_DipToPhysical_Floors()
    {
        // 安排
        var screen = Create150PercentScreen();

        // 操作
        // 100 DIP * 1.5 = 150 物理。
        var result = screen.Scale(100, toDip: false);

        // 断言
        await Assert.That(result).IsEqualTo(150);
    }

    /// <summary>
    /// Screen.Scale 从物理像素缩放到 DIP 时向上取整。
    /// </summary>
    [Test]
    public async Task Scale_PhysicalToDip_Ceils()
    {
        // 安排
        var screen = Create150PercentScreen();

        // 操作
        // 150 物理 / 1.5 = 100 DIP。
        var result = screen.Scale(150, toDip: true);

        // 断言
        await Assert.That(result).IsEqualTo(100);
    }

    /// <summary>
    /// Screen.Scale 因子为 0 时退化为 1.0，避免除零。
    /// </summary>
    [Test]
    public async Task Scale_ZeroScaleFactor_FallsBackToOne()
    {
        // 安排
        var screen = new Screen { ScaleFactor = 0f };

        // 操作
        var dipToPhysical = screen.Scale(100, toDip: false);
        var physicalToDip = screen.Scale(100, toDip: true);

        // 断言
        await Assert.That(dipToPhysical).IsEqualTo(100);
        await Assert.That(physicalToDip).IsEqualTo(100);
    }

    /// <summary>
    /// DipToPhysicalPoint 在屏幕原点处返回物理原点。
    /// </summary>
    [Test]
    public async Task DipToPhysicalPoint_AtOrigin_ReturnsPhysicalOrigin()
    {
        // 安排
        var screen = Create150PercentScreen();

        // 操作
        var physical = screen.DipToPhysicalPoint(new Point(0, 0));

        // 断言
        await Assert.That(physical.X).IsEqualTo(0);
        await Assert.That(physical.Y).IsEqualTo(0);
    }

    /// <summary>
    /// DipToPhysicalPoint 按 ScaleFactor 放大坐标。
    /// </summary>
    [Test]
    public async Task DipToPhysicalPoint_ScalesByFactor()
    {
        // 安排
        var screen = Create150PercentScreen();

        // 操作
        // DIP (100, 50) * 1.5 = 物理 (150, 75)。
        var physical = screen.DipToPhysicalPoint(new Point(100, 50));

        // 断言
        await Assert.That(physical.X).IsEqualTo(150);
        await Assert.That(physical.Y).IsEqualTo(75);
    }

    /// <summary>
    /// PhysicalToDipPoint 按 ScaleFactor 缩小坐标。
    /// </summary>
    [Test]
    public async Task PhysicalToDipPoint_ScalesByFactor()
    {
        // 安排
        var screen = Create150PercentScreen();

        // 操作
        // 物理 (150, 75) / 1.5 = DIP (100, 50)。
        var dip = screen.PhysicalToDipPoint(new Point(150, 75));

        // 断言
        await Assert.That(dip.X).IsEqualTo(100);
        await Assert.That(dip.Y).IsEqualTo(50);
    }

    /// <summary>
    /// DipToPhysicalRect 按 ScaleFactor 放大矩形。
    /// </summary>
    [Test]
    public async Task DipToPhysicalRect_ScalesByFactor()
    {
        // 安排
        var screen = Create150PercentScreen();

        // 操作
        // DIP (100,50,200,100) * 1.5 = 物理 (150,75,300,150)。
        var physical = screen.DipToPhysicalRect(new Rect(100, 50, 200, 100));

        // 断言
        await Assert.That(physical.X).IsEqualTo(150);
        await Assert.That(physical.Y).IsEqualTo(75);
        await Assert.That(physical.Width).IsEqualTo(300);
        await Assert.That(physical.Height).IsEqualTo(150);
    }

    /// <summary>
    /// PhysicalToDipRect 按 ScaleFactor 缩小矩形。
    /// </summary>
    [Test]
    public async Task PhysicalToDipRect_ScalesByFactor()
    {
        // 安排
        var screen = Create150PercentScreen();

        // 操作
        var dip = screen.PhysicalToDipRect(new Rect(150, 75, 300, 150));

        // 断言
        await Assert.That(dip.X).IsEqualTo(100);
        await Assert.That(dip.Y).IsEqualTo(50);
        await Assert.That(dip.Width).IsEqualTo(200);
        await Assert.That(dip.Height).IsEqualTo(100);
    }

    /// <summary>
    /// ContainsDipPoint 对边界内点返回 true，对边界外点返回 false。
    /// </summary>
    [Test]
    public async Task ContainsDipPoint_BoundaryChecks()
    {
        // 安排
        var screen = Create150PercentScreen();

        // 操作与断言
        await Assert.That(screen.ContainsDipPoint(new Point(100, 100))).IsTrue();
        await Assert.That(screen.ContainsDipPoint(new Point(1279, 719))).IsTrue();
        await Assert.That(screen.ContainsDipPoint(new Point(1280, 720))).IsFalse();
        await Assert.That(screen.ContainsDipPoint(new Point(-1, 0))).IsFalse();
    }

    /// <summary>
    /// ScreenManager 无平台应用时 DPI 转换返回原值。
    /// </summary>
    [Test]
    public async Task ScreenManager_NoPlatform_DpiConvertReturnsInput()
    {
        // 安排
        var manager = new ScreenManager(null);
        var point = new Point(100, 200);
        var rect = new Rect(1, 2, 3, 4);

        // 操作与断言
        await Assert.That(manager.DipToPhysicalPoint(point).X).IsEqualTo(100);
        await Assert.That(manager.PhysicalToDipPoint(point).Y).IsEqualTo(200);
        await Assert.That(manager.DipToPhysicalRect(rect).Width).IsEqualTo(3);
        await Assert.That(manager.PhysicalToDipRect(rect).Height).IsEqualTo(4);
    }

    /// <summary>
    /// ScreenManager 无平台应用时 ScreenNearest* 返回 null。
    /// </summary>
    [Test]
    public async Task ScreenManager_NoPlatform_NearestReturnsNull()
    {
        // 安排
        var manager = new ScreenManager(null);

        // 操作与断言
        await Assert.That(manager.ScreenNearestDipPoint(new Point(0, 0))).IsNull();
        await Assert.That(manager.ScreenNearestPhysicalPoint(new Point(0, 0))).IsNull();
        await Assert.That(manager.ScreenNearestDipRect(new Rect(0, 0, 10, 10))).IsNull();
        await Assert.That(manager.ScreenNearestPhysicalRect(new Rect(0, 0, 10, 10))).IsNull();
    }
}
