using TUnit.Core;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Tests;

/// <summary>
/// Screen 类 ID 和 Rotation 字段的单元测试（TUnit）。
/// 对应项 B：补全 Wails v3 Screen 结构中缺失的 ID 和 Rotation 字段。
/// </summary>
[NotInParallel]
public sealed class ScreenIdRotationTests
{
    /// <summary>
    /// 默认构造的 Screen，Id 为空字符串、Rotation 为 0。
    /// </summary>
    [Test]
    public async Task Screen_DefaultConstructor_IdEmptyAndRotationZero()
    {
        // 安排
        var screen = new Screen();

        // 断言
        await Assert.That(screen.Id).IsEqualTo(string.Empty);
        await Assert.That(screen.Rotation).IsEqualTo(0f);
    }

    /// <summary>
    /// 旧构造函数（不带 id/rotation）默认填充 Id=name、Rotation=0。
    /// </summary>
    [Test]
    public async Task Screen_LegacyConstructor_IdFallsBackToName()
    {
        // 安排
        var screen = new Screen(
            name: "DISPLAY1",
            x: 0, y: 0, width: 1920, height: 1080,
            workAreaX: 0, workAreaY: 0, workAreaWidth: 1920, workAreaHeight: 1040,
            scaleFactor: 1.0f, isPrimary: true);

        // 断言
        await Assert.That(screen.Id).IsEqualTo("DISPLAY1");
        await Assert.That(screen.Name).IsEqualTo("DISPLAY1");
        await Assert.That(screen.Rotation).IsEqualTo(0f);
    }

    /// <summary>
    /// 完整构造函数可独立设置 Id 和 Rotation。
    /// </summary>
    [Test]
    public async Task Screen_FullConstructor_IdAndRotationSet()
    {
        // 安排
        var screen = new Screen(
            id: "monitor-0",
            name: "DELL U2720Q",
            x: 0, y: 0, width: 2560, height: 1440,
            workAreaX: 0, workAreaY: 0, workAreaWidth: 2560, workAreaHeight: 1400,
            scaleFactor: 1.5f, isPrimary: true, rotation: 90);

        // 断言
        await Assert.That(screen.Id).IsEqualTo("monitor-0");
        await Assert.That(screen.Name).IsEqualTo("DELL U2720Q");
        await Assert.That(screen.Rotation).IsEqualTo(90f);
    }

    /// <summary>
    /// Rotation 支持 180 度倒置场景。
    /// </summary>
    [Test]
    public async Task Screen_Rotation180_Supported()
    {
        // 安排
        var screen = new Screen
        {
            Id = "upside-down",
            Name = "Rotated Display",
            Rotation = 180,
        };

        // 断言
        await Assert.That(screen.Rotation).IsEqualTo(180f);
    }

    /// <summary>
    /// ToString 输出包含 Id 和 Rotation 字段。
    /// </summary>
    [Test]
    public async Task Screen_ToString_IncludesIdAndRotation()
    {
        // 安排
        var screen = new Screen
        {
            Id = "disp1",
            Name = "Main",
            Width = 1920,
            Height = 1080,
            X = 0,
            Y = 0,
            ScaleFactor = 1.0f,
            Rotation = 90,
            IsPrimary = true,
        };

        // 操作
        var str = screen.ToString();

        // 断言
        await Assert.That(str).Contains("disp1");
        await Assert.That(str).Contains("Rotation=90");
    }

    /// <summary>
    /// 两个不同 Id 的 Screen 可区分。
    /// </summary>
    [Test]
    public async Task Screen_DifferentIds_Distinguishable()
    {
        // 安排
        var s1 = new Screen { Id = "screen-a", Name = "A" };
        var s2 = new Screen { Id = "screen-b", Name = "B" };

        // 断言
        await Assert.That(s1.Id).IsNotEqualTo(s2.Id);
    }
}
