using TUnit.Core;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// <see cref="LinuxWebviewWindow"/> 窗口位置报告的纯逻辑测试（TUnit）。
/// 对齐 Wails v3.1.0 <c>linuxWebviewWindow.position()</c> 语义：
/// 仅 X11 后端可定位窗口，非 X11（Wayland 等）报告 (0, 0) 而不是未生效的请求坐标。
/// 用例不需要 GTK 或 GUI 环境。
/// </summary>
[NotInParallel]
public sealed class LinuxWebviewWindowPositionTests
{
    /// <summary>
    /// 后端支持定位（X11）时返回缓存的 DIP 坐标。
    /// </summary>
    [Test]
    public async Task ResolveReportedPosition_WhenSupported_ReturnsCachedCoordinates()
    {
        // 操作
        var position = LinuxWebviewWindow.ResolveReportedPosition(
            supportsPositionControl: true, cachedX: 320, cachedY: -48);

        // 断言
        await Assert.That(position).IsEqualTo((320, -48));
    }

    /// <summary>
    /// 后端不支持定位（Wayland 等）时按上游语义返回 (0, 0)，不把缓存的请求坐标当作真实位置。
    /// </summary>
    [Test]
    public async Task ResolveReportedPosition_WhenNotSupported_ReturnsOrigin()
    {
        // 操作
        var position = LinuxWebviewWindow.ResolveReportedPosition(
            supportsPositionControl: false, cachedX: 320, cachedY: -48);

        // 断言
        await Assert.That(position).IsEqualTo((0, 0));
    }

    /// <summary>
    /// 显式要求居中时，无论 X/Y 如何都走居中分支（对应上游 applyScreenPlacement 的居中分支）。
    /// </summary>
    [Test]
    public async Task ShouldCenterOnInitialPlacement_WhenCenteredRequested_ReturnsTrue()
    {
        await Assert.That(LinuxWebviewWindow.ShouldCenterOnInitialPlacement(
            x: 100, y: 200, centered: true)).IsTrue();
    }

    /// <summary>
    /// X/Y 均有效且未要求居中时，按绝对 DIP 坐标定位（不居中）。
    /// </summary>
    [Test]
    public async Task ShouldCenterOnInitialPlacement_WithValidCoordinates_ReturnsFalse()
    {
        await Assert.That(LinuxWebviewWindow.ShouldCenterOnInitialPlacement(
            x: 0, y: 0, centered: false)).IsFalse();
    }

    /// <summary>
    /// 选项模型沿用 Windows 语义，X 或 Y 为 -1 表示“未指定”，此时按居中处理。
    /// </summary>
    [Test]
    public async Task ShouldCenterOnInitialPlacement_WithUnspecifiedCoordinate_ReturnsTrue()
    {
        await Assert.That(LinuxWebviewWindow.ShouldCenterOnInitialPlacement(
            x: -1, y: 200, centered: false)).IsTrue();
        await Assert.That(LinuxWebviewWindow.ShouldCenterOnInitialPlacement(
            x: 100, y: -1, centered: false)).IsTrue();
    }
}
