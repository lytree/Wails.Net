using TUnit.Core;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// <see cref="LinuxLauncherEntry"/> 的纯逻辑测试：启动器 URI/对象路径与状态映射。
/// 不发起 D-Bus 调用，可在任意平台执行。
/// </summary>
[NotInParallel]
public sealed class LinuxLauncherEntryTests
{
    [Test]
    public async Task BuildLauncherUri_AppId_AppendsDesktopSuffix()
    {
        await Assert.That(LinuxLauncherEntry.BuildLauncherUri("org.wails.demo"))
            .IsEqualTo("application://org.wails.demo.desktop");
    }

    [Test]
    public async Task BuildLauncherUri_AlreadyDesktop_DoesNotDuplicateSuffix()
    {
        await Assert.That(LinuxLauncherEntry.BuildLauncherUri("org.wails.demo.desktop"))
            .IsEqualTo("application://org.wails.demo.desktop");
    }

    [Test]
    public async Task BuildObjectPath_SanitizesInvalidDbusCharacters()
    {
        var path = LinuxLauncherEntry.BuildObjectPath("org.wails.demo-app");

        await Assert.That(path).IsEqualTo("/com/canonical/unity/launcherentry/org_wails_demo_app");
    }

    [Test]
    public async Task BuildProgressState_Normal_ClampsToOne()
    {
        var state = LinuxLauncherEntry.BuildProgressState(TaskbarProgressState.Normal, 150, 100);

        await Assert.That(state.ProgressVisible).IsTrue();
        await Assert.That(state.Progress).IsEqualTo(1.0);
        await Assert.That(state.CountVisible).IsFalse();
    }

    [Test]
    public async Task BuildProgressState_Indeterminate_UsesNegativeProgress()
    {
        var state = LinuxLauncherEntry.BuildProgressState(TaskbarProgressState.Indeterminate, 0, 0);

        await Assert.That(state.ProgressVisible).IsTrue();
        await Assert.That(state.Progress).IsEqualTo(-1.0);
    }

    [Test]
    public async Task BuildProgressState_None_ReturnsEmpty()
    {
        var state = LinuxLauncherEntry.BuildProgressState(TaskbarProgressState.None, 10, 100);

        await Assert.That(state.ProgressVisible).IsFalse();
        await Assert.That(state.CountVisible).IsFalse();
    }

    [Test]
    public async Task BuildBadgeState_PositiveCount_EnablesCount()
    {
        var state = LinuxLauncherEntry.BuildBadgeState(7);

        await Assert.That(state.CountVisible).IsTrue();
        await Assert.That(state.Count).IsEqualTo(7);
        await Assert.That(state.ProgressVisible).IsFalse();
    }

    [Test]
    public async Task BuildBadgeState_Zero_ClearsBadge()
    {
        var state = LinuxLauncherEntry.BuildBadgeState(0);

        await Assert.That(state.CountVisible).IsFalse();
        await Assert.That(state.Count).IsEqualTo(0);
    }
}
