using System.Diagnostics;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// <see cref="DevProcessLifetimeGuard"/> 单元测试。
/// 对应 Wails v3.1.0 PR #6371：终端被直接关闭时 terminate 全部派生进程。
/// <para>
/// 覆盖可注入的纯逻辑：幂等释放、注册失败/后端异常时的静默降级、已退出进程不误杀。
/// OS 级真实行为（关闭控制台窗口触发的作业对象终止、真实终端 SIGHUP 投递）无法完整单测：
/// Windows 作业对象仅通过「关闭作业句柄即终止已加入进程」的集成用例间接验证；
/// Unix 信号投递路径在 Windows 主机上不可验证（代码注释已说明）。
/// </para>
/// </summary>
[NotInParallel]
public sealed class DevProcessLifetimeGuardTests
{
    [Test]
    public async Task Dispose_CalledTwice_DisposesBackendOnce()
    {
        // Arrange：释放必须幂等（信号处理器与 using 作用域可能重复释放）
        var backend = new FakeBackend();
        var guard = new DevProcessLifetimeGuard(backend);

        // Act
        guard.Dispose();
        guard.Dispose();

        // Assert
        await Assert.That(backend.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Dispose_BackendThrows_SilentlyDegrades()
    {
        // Arrange：后端释放异常不得向外抛出
        var backend = new FakeBackend { ThrowOnDispose = true };
        var guard = new DevProcessLifetimeGuard(backend);

        // Act：重复释放同样安全
        guard.Dispose();
        guard.Dispose();

        // Assert
        await Assert.That(backend.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task TryAttach_AfterDispose_ReturnsFalseWithoutBackendCall()
    {
        // Arrange
        var backend = new FakeBackend();
        var guard = new DevProcessLifetimeGuard(backend);
        guard.Dispose();
        using var process = new Process();

        // Act
        var attached = guard.TryAttach(process);

        // Assert
        await Assert.That(attached).IsFalse();
        await Assert.That(backend.AttachCount).IsEqualTo(0);
    }

    [Test]
    public async Task TryAttach_NullProcess_ReturnsFalse()
    {
        // Arrange
        var backend = new FakeBackend();
        using var guard = new DevProcessLifetimeGuard(backend);

        // Act
        var attached = guard.TryAttach(null);

        // Assert
        await Assert.That(attached).IsFalse();
        await Assert.That(backend.AttachCount).IsEqualTo(0);
    }

    [Test]
    public async Task TryAttach_NotStartedProcess_SkipsBackend()
    {
        // Arrange：未启动的 Process 无有效句柄（HasExited 抛出 InvalidOperationException）
        var backend = new FakeBackend();
        using var guard = new DevProcessLifetimeGuard(backend);
        using var process = new Process();

        // Act
        var attached = guard.TryAttach(process);

        // Assert：静默降级，且不触达平台后端
        await Assert.That(attached).IsFalse();
        await Assert.That(backend.AttachCount).IsEqualTo(0);
    }

    [Test]
    public async Task TryAttach_ExitedProcess_SkipsBackend()
    {
        // Arrange：正常退出路径上的已退出进程不得被再次「误杀」
        var backend = new FakeBackend();
        using var guard = new DevProcessLifetimeGuard(backend);
        using var process = StartAndWaitExit();

        // Act
        var attached = guard.TryAttach(process);

        // Assert
        await Assert.That(attached).IsFalse();
        await Assert.That(backend.AttachCount).IsEqualTo(0);
    }

    [Test]
    public async Task TryAttach_BackendThrows_SilentlyDegrades()
    {
        // Arrange：平台注册失败（如作业分配被拒）必须静默降级
        var backend = new FakeBackend { ThrowOnAttach = true };
        using var guard = new DevProcessLifetimeGuard(backend);
        using var process = StartLongRunningProcess();

        try
        {
            // Act
            var attached = guard.TryAttach(process);

            // Assert
            await Assert.That(attached).IsFalse();
            await Assert.That(backend.AttachCount).IsEqualTo(1);
        }
        finally
        {
            KillIfRunning(process);
        }
    }

    [Test]
    public async Task TryAttach_BackendReturnsFalse_PropagatesFalse()
    {
        // Arrange：后端返回 false 表示注册失败（静默降级，不抛出异常）
        var backend = new FakeBackend { AttachResult = false };
        using var guard = new DevProcessLifetimeGuard(backend);
        using var process = StartLongRunningProcess();

        try
        {
            // Act
            var attached = guard.TryAttach(process);

            // Assert
            await Assert.That(attached).IsFalse();
        }
        finally
        {
            KillIfRunning(process);
        }
    }

    [Test]
    public async Task Create_CurrentPlatform_ReturnsUsableGuard()
    {
        // Arrange：工厂在当前平台创建真实后端（注册失败时降级为空实现）
        using var guard = DevProcessLifetimeGuard.Create(null);
        using var process = new Process();

        // Act：未启动进程无需守护
        var attached = guard.TryAttach(process);

        // Assert
        await Assert.That(attached).IsFalse();
    }

    [Test]
    public async Task UnixSignalLifetimeBackend_OnWindows_DegradesToNoOp()
    {
        // Arrange：Unix 信号后端在 Windows 上必须降级为空实现（不得抛出）
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Unix 信号后端仅适用于非 Windows 平台");
            return;
        }

        var backend = UnixSignalLifetimeBackend.TryCreate(null);
        using var process = new Process();

        // Act
        var attached = backend.TryAttach(process);
        backend.Dispose();
        backend.Dispose();

        // Assert
        await Assert.That(attached).IsFalse();
    }

    [Test]
    public async Task WindowsJobObjectBackend_ClosingJob_KillsAttachedProcess()
    {
        // Arrange：Windows 作业对象集成验证（关闭最后一个句柄 → KILL_ON_JOB_CLOSE 终止进程）。
        // 若 CLI 已处于不兼容作业（如 CI 宿主）导致分配被拒，则只验证静默降级路径。
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Windows 作业对象仅在 Windows 上可用");
            return;
        }

        using var process = StartLongRunningProcess();
        var backend = WindowsJobObjectLifetimeBackend.TryCreate();
        try
        {
            var attached = backend.TryAttach(process);
            if (!attached)
            {
                await Assert.That(attached).IsFalse();
                return;
            }

            // Act
            backend.Dispose();

            // Assert：进程在有限时间内被作业对象终止
            var handle = new ProcessTreeHandle(process, started: true);
            await Assert.That(await handle.WaitForExitAsync(TimeSpan.FromSeconds(10))).IsTrue();
        }
        finally
        {
            backend.Dispose();
            KillIfRunning(process);
        }
    }

    [Test]
    public async Task WindowsJobObjectBackend_AfterDispose_TryAttachReturnsFalse()
    {
        // Arrange
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Windows 作业对象仅在 Windows 上可用");
            return;
        }

        var backend = WindowsJobObjectLifetimeBackend.TryCreate();
        backend.Dispose();
        using var process = new Process();

        // Act
        var attached = backend.TryAttach(process);

        // Assert
        await Assert.That(attached).IsFalse();
    }

    /// <summary>
    /// 启动一个长运行进程（跨平台），仅用于验证守护行为；用完必须终止。
    /// </summary>
    /// <returns>已启动的进程。</returns>
    private static Process StartLongRunningProcess()
    {
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c ping -n 30 127.0.0.1 > nul")
            : ("sleep", "30");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.Start();
        return process;
    }

    /// <summary>
    /// 启动一个立即退出的进程（跨平台），用于验证已退出进程被跳过。
    /// </summary>
    /// <returns>已退出的进程。</returns>
    private static Process StartAndWaitExit()
    {
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c exit 0")
            : ("sh", "-c \"exit 0\"");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.Start();
        process.WaitForExit();
        return process;
    }

    /// <summary>
    /// 尽力终止测试进程（进程可能已被作业对象终止）。
    /// </summary>
    /// <param name="process">目标进程。</param>
    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            /* 测试清理尽力而为 */
        }
    }

    /// <summary>
    /// <see cref="IDevProcessLifetimeBackend"/> 的测试替身：记录调用次数并可模拟注册失败/异常。
    /// </summary>
    private sealed class FakeBackend : IDevProcessLifetimeBackend
    {
        /// <inheritdoc/>
        public bool TryAttach(Process process)
        {
            AttachCount++;
            if (ThrowOnAttach)
            {
                throw new InvalidOperationException("模拟平台注册失败");
            }

            return AttachResult;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            DisposeCount++;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("模拟释放失败");
            }
        }

        /// <summary>TryAttach 调用次数。</summary>
        public int AttachCount { get; private set; }

        /// <summary>Dispose 调用次数。</summary>
        public int DisposeCount { get; private set; }

        /// <summary>TryAttach 返回结果。</summary>
        public bool AttachResult { get; set; } = true;

        /// <summary>TryAttach 是否抛出异常。</summary>
        public bool ThrowOnAttach { get; set; }

        /// <summary>Dispose 是否抛出异常。</summary>
        public bool ThrowOnDispose { get; set; }
    }
}
