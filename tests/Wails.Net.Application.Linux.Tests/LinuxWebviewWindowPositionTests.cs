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
}
