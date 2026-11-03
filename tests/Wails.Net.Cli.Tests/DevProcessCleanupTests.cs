using System.Diagnostics;
using System.Runtime.InteropServices;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// DevProcessCleanup / ProcessTreeHandle 单元测试。
/// 对应任务 P2-12：<c>wails dev</c> 退出清理完善（Wails v3 beta.25 / PR #6048）。
/// 覆盖：运行中进程被终止并有限等待、已退出/未启动进程被跳过、超时兜底、多句柄聚合统计，
/// 以及基于真实进程的跨平台进程树终止（<c>Process.Kill(entireProcessTree: true)</c>）。
/// </summary>
[NotInParallel]
public sealed class DevProcessCleanupTests
{
    [Test]
    public async Task TerminateAsync_RunningProcess_KillsTreeAndWaits()
    {
        // Arrange
        var timeout = TimeSpan.FromMilliseconds(123);
        var handle = new FakeProcessHandle { HasExited = false };

        // Act
        var result = await DevProcessCleanup.TerminateAsync(new IDevProcessHandle[] { handle }, timeout);

        // Assert
        await Assert.That(result.Killed).IsEqualTo(1);
        await Assert.That(result.AlreadyExited).IsEqualTo(0);
        await Assert.That(result.TimedOut).IsEqualTo(0);
        await Assert.That(handle.KillCount).IsEqualTo(1);
        await Assert.That(handle.WaitCount).IsEqualTo(1);
        await Assert.That(handle.LastWaitTimeout).IsEqualTo(timeout);
    }

    [Test]
    public async Task TerminateAsync_AlreadyExitedProcess_SkippedWithoutKill()
    {
        // Arrange：正常结束路径上进程已退出，必须跳过以避免额外等待
        var handle = new FakeProcessHandle { HasExited = true };

        // Act
        var result = await DevProcessCleanup.TerminateAsync(
            new IDevProcessHandle[] { handle }, DevProcessCleanup.DefaultTerminationTimeout);

        // Assert
        await Assert.That(result.Killed).IsEqualTo(0);
        await Assert.That(result.AlreadyExited).IsEqualTo(1);
        await Assert.That(handle.KillCount).IsEqualTo(0);
        await Assert.That(handle.WaitCount).IsEqualTo(0);
    }

    [Test]
    public async Task TerminateAsync_TimedOutWait_ReportedAsTimedOut()
    {
        // Arrange：强制终止后仍未在等待上限内确认退出（超时兜底）
        var handle = new FakeProcessHandle { HasExited = false, WaitResult = false };

        // Act
        var result = await DevProcessCleanup.TerminateAsync(
            new IDevProcessHandle[] { handle }, DevProcessCleanup.DefaultTerminationTimeout);

        // Assert
        await Assert.That(result.Killed).IsEqualTo(1);
        await Assert.That(result.TimedOut).IsEqualTo(1);
        await Assert.That(handle.KillCount).IsEqualTo(1);
    }

    [Test]
    public async Task TerminateAsync_MultipleHandles_OnlyRunningAreKilled()
    {
        // Arrange
        var runningA = new FakeProcessHandle { HasExited = false };
        var exited = new FakeProcessHandle { HasExited = true };
        var runningB = new FakeProcessHandle { HasExited = false, WaitResult = false };

        // Act
        var result = await DevProcessCleanup.TerminateAsync(
            new IDevProcessHandle[] { runningA, exited, runningB },
            DevProcessCleanup.DefaultTerminationTimeout);

        // Assert
        await Assert.That(result.Killed).IsEqualTo(2);
        await Assert.That(result.AlreadyExited).IsEqualTo(1);
        await Assert.That(result.TimedOut).IsEqualTo(1);
        await Assert.That(runningA.KillCount).IsEqualTo(1);
        await Assert.That(exited.KillCount).IsEqualTo(0);
        await Assert.That(runningB.KillCount).IsEqualTo(1);
    }

    [Test]
    public async Task TerminateAsync_EmptyHandles_DoesNothing()
    {
        // Act
        var result = await DevProcessCleanup.TerminateAsync(
            Array.Empty<IDevProcessHandle>(), DevProcessCleanup.DefaultTerminationTimeout);

        // Assert
        await Assert.That(result.Killed).IsEqualTo(0);
        await Assert.That(result.AlreadyExited).IsEqualTo(0);
        await Assert.That(result.TimedOut).IsEqualTo(0);
    }

    [Test]
    public async Task DefaultTerminationTimeout_IsBoundedForCtrlCGracePeriod()
    {
        // 需小于 System.CommandLine 的进程终止宽限期（InvocationConfiguration.ProcessTerminationTimeout，默认 2s），
        // 以保证 Ctrl+C 路径上的退出清理能够完成。
        await Assert.That(DevProcessCleanup.DefaultTerminationTimeout > TimeSpan.Zero).IsTrue();
        await Assert.That(DevProcessCleanup.DefaultTerminationTimeout <= TimeSpan.FromSeconds(2)).IsTrue();
    }

    [Test]
    public async Task TerminateSync_OnlyRunningProcessesAreKilledWithoutWaiting()
    {
        // Arrange：终端被关闭（SIGHUP）路径上的同步清理：已退出进程必须跳过，且不产生等待
        var running = new FakeProcessHandle { HasExited = false };
        var exited = new FakeProcessHandle { HasExited = true };

        // Act
        DevProcessCleanup.TerminateSync(new IDevProcessHandle[] { running, exited });

        // Assert
        await Assert.That(running.KillCount).IsEqualTo(1);
        await Assert.That(running.WaitCount).IsEqualTo(0);
        await Assert.That(exited.KillCount).IsEqualTo(0);
    }

    [Test]
    public async Task TerminateSync_NullElementAndEmptyHandles_AreIgnored()
    {
        // Arrange：null 元素忽略；信号可能重复触发，空集合必须安全无操作
        var running = new FakeProcessHandle { HasExited = false };

        // Act
        DevProcessCleanup.TerminateSync(new IDevProcessHandle[] { null!, running });
        DevProcessCleanup.TerminateSync(Array.Empty<IDevProcessHandle>());

        // Assert
        await Assert.That(running.KillCount).IsEqualTo(1);
    }

    [Test]
    public async Task ProcessTreeHandle_NotStarted_ReportsExited()
    {
        // Arrange：Start() 未成功（或未调用）时句柄无需清理
        using var proc = new Process();
        var handle = new ProcessTreeHandle(proc, started: false);

        // Assert
        await Assert.That(handle.HasExited).IsTrue();

        // 未启动的句柄调用终止不应抛出异常。
        handle.KillTree();

        await Assert.That(await handle.WaitForExitAsync(TimeSpan.FromMilliseconds(10))).IsTrue();
    }

    [Test]
    public async Task ProcessTreeHandle_NullProcess_ReportsExited()
    {
        // Arrange
        var handle = new ProcessTreeHandle(null, started: true);

        // Assert
        await Assert.That(handle.HasExited).IsTrue();
        handle.KillTree();
        await Assert.That(await handle.WaitForExitAsync(TimeSpan.FromMilliseconds(10))).IsTrue();
    }

    [Test]
    public async Task ProcessTreeHandle_RunningRealProcess_KillTreeTerminates()
    {
        // Arrange：启动一个长运行的真实进程（跨平台），验证整棵进程树可被终止
        var (fileName, arguments) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("cmd.exe", "/c ping -n 30 127.0.0.1 > nul")
            : ("sleep", "30");

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        var started = proc.Start();
        await Assert.That(started).IsTrue();

        var handle = new ProcessTreeHandle(proc, started: true);
        await Assert.That(handle.HasExited).IsFalse();

        // Act
        handle.KillTree();
        var exited = await handle.WaitForExitAsync(TimeSpan.FromSeconds(10));

        // Assert
        await Assert.That(exited).IsTrue();
        await Assert.That(handle.HasExited).IsTrue();
    }

    /// <summary>
    /// <see cref="IDevProcessHandle"/> 的测试替身：记录终止/等待调用与参数，可模拟超时与已退出状态。
    /// </summary>
    private sealed class FakeProcessHandle : IDevProcessHandle
    {
        /// <inheritdoc/>
        public bool HasExited { get; set; }

        /// <summary>KillTree 调用次数。</summary>
        public int KillCount { get; private set; }

        /// <summary>WaitForExitAsync 调用次数。</summary>
        public int WaitCount { get; private set; }

        /// <summary>最后一次等待使用的超时值。</summary>
        public TimeSpan LastWaitTimeout { get; private set; }

        /// <summary>等待返回结果（true 表示已确认退出）。</summary>
        public bool WaitResult { get; set; } = true;

        /// <inheritdoc/>
        public void KillTree() => KillCount++;

        /// <inheritdoc/>
        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            WaitCount++;
            LastWaitTimeout = timeout;
            return Task.FromResult(WaitResult);
        }
    }
}
