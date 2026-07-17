using TUnit.Core;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// LinuxBackendDetector 的单元测试（TUnit）。
/// 测试通过 GDK_BACKEND 环境变量检测后端类型，以及能力查询方法。
/// 注意：此类修改环境变量和静态缓存，因此不并行执行。
/// </summary>
[NotInParallel]
public sealed class LinuxBackendDetectorTests
{
    /// <summary>
    /// 测试后清理：重置缓存与环境变量，避免影响后续测试。
    /// </summary>
    [After(Test)]
    public void Cleanup()
    {
        LinuxBackendDetector.ResetForTesting();
        Environment.SetEnvironmentVariable("GDK_BACKEND", null);
    }

    [Test]
    public async Task Detect_WithGdkBackendX11_ReturnsX11()
    {
        // 安排：显式指定 X11 后端
        Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");
        LinuxBackendDetector.ResetForTesting();

        // 操作
        var backend = LinuxBackendDetector.Detect();

        // 断言
        await Assert.That(backend).IsEqualTo(LinuxDisplayBackend.X11);
    }

    [Test]
    public async Task Detect_WithGdkBackendWayland_ReturnsWayland()
    {
        // 安排：显式指定 Wayland 后端
        Environment.SetEnvironmentVariable("GDK_BACKEND", "wayland");
        LinuxBackendDetector.ResetForTesting();

        // 操作
        var backend = LinuxBackendDetector.Detect();

        // 断言
        await Assert.That(backend).IsEqualTo(LinuxDisplayBackend.Wayland);
    }

    [Test]
    public async Task Detect_WithGdkBackendBroadway_ReturnsBroadway()
    {
        // 安排：显式指定 Broadway 后端
        Environment.SetEnvironmentVariable("GDK_BACKEND", "broadway");
        LinuxBackendDetector.ResetForTesting();

        // 操作
        var backend = LinuxBackendDetector.Detect();

        // 断言
        await Assert.That(backend).IsEqualTo(LinuxDisplayBackend.Broadway);
    }

    [Test]
    public async Task Detect_WithGdkBackendX11_UpperCase_ReturnsX11()
    {
        // 安排：大小写不敏感
        Environment.SetEnvironmentVariable("GDK_BACKEND", "X11");
        LinuxBackendDetector.ResetForTesting();

        // 操作
        var backend = LinuxBackendDetector.Detect();

        // 断言
        await Assert.That(backend).IsEqualTo(LinuxDisplayBackend.X11);
    }

    [Test]
    public async Task Detect_CachesResult_OnSecondCall()
    {
        // 安排：首次检测后更改环境变量
        Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");
        LinuxBackendDetector.ResetForTesting();
        _ = LinuxBackendDetector.Detect();

        // 操作：移除环境变量后再次检测，应返回缓存的 X11
        Environment.SetEnvironmentVariable("GDK_BACKEND", null);
        var backend = LinuxBackendDetector.Detect();

        // 断言：缓存生效，不重新检测
        await Assert.That(backend).IsEqualTo(LinuxDisplayBackend.X11);
    }

    [Test]
    public async Task IsX11_ReturnsTrue_WhenGdkBackendIsX11()
    {
        // 安排
        Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");
        LinuxBackendDetector.ResetForTesting();

        // 操作与断言
        await Assert.That(LinuxBackendDetector.IsX11()).IsTrue();
    }

    [Test]
    public async Task IsX11_ReturnsFalse_WhenGdkBackendIsWayland()
    {
        // 安排
        Environment.SetEnvironmentVariable("GDK_BACKEND", "wayland");
        LinuxBackendDetector.ResetForTesting();

        // 操作与断言
        await Assert.That(LinuxBackendDetector.IsX11()).IsFalse();
    }

    [Test]
    public async Task IsWayland_ReturnsTrue_WhenGdkBackendIsWayland()
    {
        // 安排
        Environment.SetEnvironmentVariable("GDK_BACKEND", "wayland");
        LinuxBackendDetector.ResetForTesting();

        // 操作与断言
        await Assert.That(LinuxBackendDetector.IsWayland()).IsTrue();
    }

    [Test]
    public async Task SupportsWindowPositionControl_ReturnsTrue_OnX11()
    {
        // 安排
        Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");
        LinuxBackendDetector.ResetForTesting();

        // 操作与断言：X11 支持 wmctrl 窗口位置控制
        await Assert.That(LinuxBackendDetector.SupportsWindowPositionControl()).IsTrue();
    }

    [Test]
    public async Task SupportsWindowPositionControl_ReturnsFalse_OnWayland()
    {
        // 安排
        Environment.SetEnvironmentVariable("GDK_BACKEND", "wayland");
        LinuxBackendDetector.ResetForTesting();

        // 操作与断言：Wayland 不支持 wmctrl 窗口位置控制
        await Assert.That(LinuxBackendDetector.SupportsWindowPositionControl()).IsFalse();
    }

    [Test]
    public async Task SupportsAlwaysOnTop_ReturnsTrue_OnX11()
    {
        // 安排
        Environment.SetEnvironmentVariable("GDK_BACKEND", "x11");
        LinuxBackendDetector.ResetForTesting();

        // 操作与断言：X11 支持 EWMH _NET_WM_STATE_ABOVE
        await Assert.That(LinuxBackendDetector.SupportsAlwaysOnTop()).IsTrue();
    }

    [Test]
    public async Task SupportsAlwaysOnTop_ReturnsFalse_OnWayland()
    {
        // 安排
        Environment.SetEnvironmentVariable("GDK_BACKEND", "wayland");
        LinuxBackendDetector.ResetForTesting();

        // 操作与断言：Wayland 无通用置顶协议
        await Assert.That(LinuxBackendDetector.SupportsAlwaysOnTop()).IsFalse();
    }
}
