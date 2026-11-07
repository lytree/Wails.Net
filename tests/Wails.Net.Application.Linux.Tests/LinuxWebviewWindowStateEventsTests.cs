using TUnit.Core;
using Wails.Net.Application.Platform;
using Wails.Net.Events;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// <see cref="LinuxWebviewWindow.ChangedWindowStateEvents"/> 的纯逻辑测试（TUnit）。
/// 对齐 Wails v3.1.0 <c>webview_window_linux_test.go</c> 中的 <c>TestChangedLinuxWindowStateEvents</c>：
/// 首次观测只通告当前激活态，之后按前后状态差分派发，全屏切换需同时保留独立的最大化事件。
/// 用例不需要 GTK 或 GUI 环境。
/// </summary>
[NotInParallel]
public sealed class LinuxWebviewWindowStateEventsTests
{
    /// <summary>
    /// 首次观测到普通状态时不派发任何事件。
    /// </summary>
    [Test]
    public async Task ChangedWindowStateEvents_InitialNormalState_ReturnsNoEvents()
    {
        var events = LinuxWebviewWindow.ChangedWindowStateEvents(
            new LinuxWindowState(Minimised: false, Maximised: false, Fullscreen: false),
            new LinuxWindowState(Minimised: false, Maximised: false, Fullscreen: false),
            observed: false);

        await Assert.That(events).IsEquivalentTo(Array.Empty<WindowEventType>());
    }

    /// <summary>
    /// 首次观测到最大化窗口时通告 WindowMaximised（启动即为最大化的场景）。
    /// </summary>
    [Test]
    public async Task ChangedWindowStateEvents_InitialMaximisedState_AnnouncesMaximised()
    {
        var events = LinuxWebviewWindow.ChangedWindowStateEvents(
            new LinuxWindowState(Minimised: false, Maximised: false, Fullscreen: false),
            new LinuxWindowState(Minimised: false, Maximised: true, Fullscreen: false),
            observed: false);

        await Assert.That(events).IsEquivalentTo(new[] { WindowEventType.WindowMaximised });
    }

    /// <summary>
    /// 首次观测到最小化窗口时通告 WindowMinimised。
    /// </summary>
    [Test]
    public async Task ChangedWindowStateEvents_InitialMinimisedState_AnnouncesMinimised()
    {
        var events = LinuxWebviewWindow.ChangedWindowStateEvents(
            new LinuxWindowState(Minimised: false, Maximised: false, Fullscreen: false),
            new LinuxWindowState(Minimised: true, Maximised: false, Fullscreen: false),
            observed: false);

        await Assert.That(events).IsEquivalentTo(new[] { WindowEventType.WindowMinimised });
    }

    /// <summary>
    /// 从最大化恢复时派发 WindowUnmaximised。
    /// </summary>
    [Test]
    public async Task ChangedWindowStateEvents_RestoreFromMaximised_ReturnsUnmaximised()
    {
        var events = LinuxWebviewWindow.ChangedWindowStateEvents(
            new LinuxWindowState(Minimised: false, Maximised: true, Fullscreen: false),
            new LinuxWindowState(Minimised: false, Maximised: false, Fullscreen: false),
            observed: true);

        await Assert.That(events).IsEquivalentTo(new[] { WindowEventType.WindowUnmaximised });
    }

    /// <summary>
    /// 从最小化恢复时派发 WindowUnminimised。
    /// </summary>
    [Test]
    public async Task ChangedWindowStateEvents_RestoreFromMinimised_ReturnsUnminimised()
    {
        var events = LinuxWebviewWindow.ChangedWindowStateEvents(
            new LinuxWindowState(Minimised: true, Maximised: false, Fullscreen: false),
            new LinuxWindowState(Minimised: false, Maximised: false, Fullscreen: false),
            observed: true);

        await Assert.That(events).IsEquivalentTo(new[] { WindowEventType.WindowUnminimised });
    }

    /// <summary>
    /// 最大化直接进入全屏时，上游要求先保留独立的 WindowUnmaximised 再派发 WindowFullscreen。
    /// </summary>
    [Test]
    public async Task ChangedWindowStateEvents_MaximisedToFullscreen_PreservesDistinctUnmaximise()
    {
        var events = LinuxWebviewWindow.ChangedWindowStateEvents(
            new LinuxWindowState(Minimised: false, Maximised: true, Fullscreen: false),
            new LinuxWindowState(Minimised: false, Maximised: false, Fullscreen: true),
            observed: true);

        await Assert.That(events).IsEquivalentTo(new[]
        {
            WindowEventType.WindowUnmaximised,
            WindowEventType.WindowFullscreen
        });
    }

    /// <summary>
    /// 全屏退出并恢复到普通状态时派发 WindowUnfullscreen。
    /// </summary>
    [Test]
    public async Task ChangedWindowStateEvents_ExitFullscreen_ReturnsUnfullscreen()
    {
        var events = LinuxWebviewWindow.ChangedWindowStateEvents(
            new LinuxWindowState(Minimised: false, Maximised: false, Fullscreen: true),
            new LinuxWindowState(Minimised: false, Maximised: false, Fullscreen: false),
            observed: true);

        await Assert.That(events).IsEquivalentTo(new[] { WindowEventType.WindowUnfullscreen });
    }
}
