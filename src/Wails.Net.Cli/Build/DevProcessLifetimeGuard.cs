using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Wails.Net.Cli.Build;

/// <summary>
/// dev 会话派生进程的「终端生命周期」守卫。
/// <para>
/// 对应 Wails v3 <c>v3.1.0</c> PR #6371：<c>wails dev</c> 所在的终端被直接关闭时
/// （Windows 关闭控制台窗口 / Unix 终端收到 <c>SIGHUP</c>），必须终止全部派生进程——
/// <c>dotnet watch</c> 及其派生的应用进程、前端 dev server，避免遗留孤儿进程。
/// 既有的 <see cref="DevProcessCleanup"/> 只覆盖正常退出 / Ctrl+C / 异常路径，
/// 本类型补充「CLI 进程被外部强制结束」这一兜底场景。
/// </para>
/// <para>
/// 平台语义：
/// <list type="bullet">
/// <item>Windows：使用作业对象（<c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>）把派生进程加入作业。
/// CLI 进程无论以何种方式结束（正常退出、异常、关闭控制台窗口），最后一个作业句柄被内核关闭时
/// 即终止作业内全部进程。</item>
/// <item>Unix：通过 <see cref="PosixSignalRegistration"/> 注册 <c>SIGHUP</c> / <c>SIGTERM</c>，
/// 收到信号时执行注入的清理回调（终止派生进程），随后沿用信号的默认终止语义
/// （不设置 <see cref="PosixSignalContext.Cancel"/>），对齐上游 SIGHUP 语义。
/// <c>SIGINT</c>（Ctrl+C）仍交由 System.CommandLine 的既有处理，避免重复清理。</item>
/// </list>
/// </para>
/// <para>
/// 所有注册动作均为尽力而为：平台不支持、作业分配被拒（如 CLI 已处于不兼容的作业中）
/// 或信号注册失败时静默降级（<see cref="TryAttach"/> 返回 <c>false</c>），
/// 不抛出异常，也不影响既有正常退出清理路径。
/// </para>
/// </summary>
internal sealed class DevProcessLifetimeGuard : IDisposable
{
    /// <summary>平台后端（Windows 作业对象 / Unix 信号注册 / 空实现）。</summary>
    private readonly IDevProcessLifetimeBackend _backend;

    /// <summary>是否已释放（1 表示已释放）；用于保证 <see cref="Dispose"/> 幂等。</summary>
    private int _disposed;

    /// <summary>
    /// 构造守卫。
    /// </summary>
    /// <param name="backend">平台后端；不允许为 null（测试可注入替身）。</param>
    internal DevProcessLifetimeGuard(IDevProcessLifetimeBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <summary>
    /// 按当前平台创建守卫。
    /// </summary>
    /// <param name="onTerminalClosed">
    /// 终端被关闭（Unix <c>SIGHUP</c> / <c>SIGTERM</c>）时的同步清理回调：应终止全部派生进程；
    /// 回调可能被重复注册的信号触发，实现需保证重复调用安全。Windows 路径不使用该回调
    /// （由作业对象在 CLI 进程结束时兜底）。
    /// </param>
    /// <returns>守卫实例（注册失败时内部降级为空实现，仍可安全使用与释放）。</returns>
    internal static DevProcessLifetimeGuard Create(Action? onTerminalClosed) =>
        new(DevProcessLifetimeBackends.Create(onTerminalClosed));

    /// <summary>
    /// 尝试把指定进程纳入生命周期守护（Windows：加入作业；Unix：由信号处理器统一清理）。
    /// 已退出 / 尚未启动的进程直接跳过（正常退出路径不会误杀已结束进程）。
    /// 注册失败时静默降级并返回 <c>false</c>，不抛出异常。
    /// </summary>
    /// <param name="process">目标进程，可为 null。</param>
    /// <returns>是否成功纳入守护。</returns>
    internal bool TryAttach(Process? process)
    {
        if (process is null || Volatile.Read(ref _disposed) != 0)
        {
            return false;
        }

        try
        {
            if (process.HasExited)
            {
                // 已退出进程无需守护，也避免对失效句柄做作业分配。
                return false;
            }
        }
        catch (InvalidOperationException)
        {
            // 进程尚未启动（无有效句柄）：无需守护。
            return false;
        }

        try
        {
            return _backend.TryAttach(process);
        }
        catch (Exception)
        {
            // 注册失败静默降级：正常退出路径的 DevProcessCleanup 仍然生效。
            return false;
        }
    }

    /// <summary>
    /// 释放守卫（幂等）：Windows 关闭作业句柄（触发 <c>KILL_ON_JOB_CLOSE</c> 终止残留进程）；
    /// Unix 注销信号处理器。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _backend.Dispose();
        }
        catch (Exception)
        {
            // 释放尽力而为：进程可能已退出或注册已失效。
        }
    }
}

/// <summary>
/// 生命周期守护的平台后端抽象。
/// <para>
/// 接口存在的目的是把平台相关的 OS 调用（作业对象 / 信号注册）与 <see cref="DevProcessLifetimeGuard"/>
/// 的纯逻辑（幂等释放、失败降级、跳过已退出进程）分离，便于注入替身做单元测试。
/// </para>
/// </summary>
internal interface IDevProcessLifetimeBackend : IDisposable
{
    /// <summary>
    /// 尝试把进程纳入生命周期守护。
    /// </summary>
    /// <param name="process">目标进程（调用方已确认其正在运行）。</param>
    /// <returns>是否成功纳入；失败返回 <c>false</c>（调用方应静默降级）。</returns>
    bool TryAttach(Process process);
}

/// <summary>
/// 平台后端工厂：按当前操作系统选择实现，注册失败时返回空实现。
/// </summary>
internal static class DevProcessLifetimeBackends
{
    /// <summary>
    /// 创建平台后端。
    /// </summary>
    /// <param name="onTerminalClosed">Unix 信号触发时的同步清理回调。</param>
    /// <returns>平台后端；不支持或注册失败时返回空实现。</returns>
    internal static IDevProcessLifetimeBackend Create(Action? onTerminalClosed)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsJobObjectLifetimeBackend.TryCreate();
        }

        return UnixSignalLifetimeBackend.TryCreate(onTerminalClosed);
    }
}

/// <summary>
/// 空实现：平台不支持或注册失败时的降级路径，<see cref="TryAttach"/> 恒返回 <c>false</c>。
/// </summary>
internal sealed class NoOpProcessLifetimeBackend : IDevProcessLifetimeBackend
{
    /// <summary>共享单例（无状态）。</summary>
    internal static readonly NoOpProcessLifetimeBackend Instance = new();

    private NoOpProcessLifetimeBackend()
    {
    }

    /// <inheritdoc/>
    public bool TryAttach(Process process) => false;

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

/// <summary>
/// Windows 作业对象后端：<c>CreateJobObject</c> +
/// <c>SetInformationJobObject(JobObjectExtendedLimitInformation, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE)</c> +
/// <c>AssignProcessToJobObject</c>。
/// <para>
/// 对应 Wails v3.1.0 PR #6371 的 Windows 实现语义：作业最后一个句柄被关闭（CLI 进程以任何方式结束）
/// 时，内核终止作业内的全部进程，覆盖关闭控制台窗口导致的强制结束。
/// </para>
/// <para>
/// 说明：CLI 项目未引入 CsWin32（该源生成器目前仅用于 Windows 平台实现项目），
/// 此处沿用与本仓库 <c>Win32SystemTray.cs</c> 一致的 <see cref="DllImportAttribute"/> 手动声明风格，
/// 未引用 <c>PInvoke.*</c> NuGet 包。OS 级行为无法在单元测试中完全验证，
/// 仅能通过 Windows 下的集成用例验证「关闭作业句柄终止进程」。
/// </para>
/// </summary>
internal sealed class WindowsJobObjectLifetimeBackend : IDevProcessLifetimeBackend
{
    /// <summary><c>JobObjectExtendedLimitInformation</c> 信息类编号。</summary>
    private const int JobObjectExtendedLimitInformation = 9;

    /// <summary><c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>（0x2000）：作业最后一个句柄关闭时终止全部进程。</summary>
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    /// <summary>作业句柄；0 表示未创建或已关闭。</summary>
    private nint _job;

    /// <summary>是否已释放（1 表示已释放）。</summary>
    private int _disposed;

    private WindowsJobObjectLifetimeBackend(nint job)
    {
        _job = job;
    }

    /// <summary>
    /// 创建后端（仅 Windows 生效）。
    /// </summary>
    /// <returns>创建成功的后端；非 Windows 或创建/配置失败时返回空实现（静默降级）。</returns>
    internal static IDevProcessLifetimeBackend TryCreate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return NoOpProcessLifetimeBackend.Instance;
        }

        nint job = 0;
        try
        {
            job = CreateJobObjectW(0, null);
            if (job == 0)
            {
                return NoOpProcessLifetimeBackend.Instance;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JobObjectLimitKillOnJobClose,
                },
            };

            if (!SetInformationJobObject(
                    job,
                    JobObjectExtendedLimitInformation,
                    ref info,
                    (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            {
                CloseHandle(job);
                return NoOpProcessLifetimeBackend.Instance;
            }

            return new WindowsJobObjectLifetimeBackend(job);
        }
        catch (Exception)
        {
            // 作业对象创建/配置失败（如受限于宿主安全策略）：静默降级。
            if (job != 0)
            {
                CloseHandle(job);
            }

            return NoOpProcessLifetimeBackend.Instance;
        }
    }

    /// <inheritdoc/>
    public bool TryAttach(Process process)
    {
        if (Volatile.Read(ref _disposed) != 0 || _job == 0)
        {
            return false;
        }

        try
        {
            // 已经 CLI 自身处于（不兼容的）作业中时分配会被拒绝：返回 false 静默降级。
            return AssignProcessToJobObject(_job, process.Handle);
        }
        catch (Exception)
        {
            // 句柄失效 / 进程已退出（竞态）：尽力而为。
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var job = Interlocked.Exchange(ref _job, 0);
        if (job != 0)
        {
            // KILL_ON_JOB_CLOSE：关闭最后一个句柄即终止作业内全部进程。
            CloseHandle(job);
        }
    }

    /// <summary>
    /// <c>JOBOBJECT_BASIC_LIMIT_INFORMATION</c> 结构（与 Windows SDK 布局一致）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        /// <summary>每进程用户态时间限制（LARGE_INTEGER）。</summary>
        public long PerProcessUserTimeLimit;

        /// <summary>每作业用户态时间限制（LARGE_INTEGER）。</summary>
        public long PerJobUserTimeLimit;

        /// <summary>限制标志。</summary>
        public uint LimitFlags;

        /// <summary>最小工作集（SIZE_T）。</summary>
        public nint MinimumWorkingSetSize;

        /// <summary>最大工作集（SIZE_T）。</summary>
        public nint MaximumWorkingSetSize;

        /// <summary>活动进程数上限。</summary>
        public uint ActiveProcessLimit;

        /// <summary>处理器亲和性（ULONG_PTR）。</summary>
        public nuint Affinity;

        /// <summary>优先级类别。</summary>
        public uint PriorityClass;

        /// <summary>调度类别。</summary>
        public uint SchedulingClass;
    }

    /// <summary><c>IO_COUNTERS</c> 结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        /// <summary>读操作次数。</summary>
        public ulong ReadOperationCount;

        /// <summary>写操作次数。</summary>
        public ulong WriteOperationCount;

        /// <summary>其他操作次数。</summary>
        public ulong OtherOperationCount;

        /// <summary>读取字节数。</summary>
        public ulong ReadTransferCount;

        /// <summary>写入字节数。</summary>
        public ulong WriteTransferCount;

        /// <summary>其他传输字节数。</summary>
        public ulong OtherTransferCount;
    }

    /// <summary><c>JOBOBJECT_EXTENDED_LIMIT_INFORMATION</c> 结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        /// <summary>基本限制信息。</summary>
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;

        /// <summary>IO 计数器。</summary>
        public IO_COUNTERS IoInfo;

        /// <summary>进程内存限制（SIZE_T）。</summary>
        public nint ProcessMemoryLimit;

        /// <summary>作业内存限制（SIZE_T）。</summary>
        public nint JobMemoryLimit;

        /// <summary>进程内存峰值（SIZE_T）。</summary>
        public nint PeakProcessMemoryUsed;

        /// <summary>作业内存峰值（SIZE_T）。</summary>
        public nint PeakJobMemoryUsed;
    }

    /// <summary>创建作业对象（调用失败时返回 0）。</summary>
    /// <param name="lpJobAttributes">作业对象安全属性，null 表示默认。</param>
    /// <param name="lpName">作业对象名称，null 表示匿名作业。</param>
    /// <returns>作业句柄。</returns>
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

    /// <summary>设置作业对象信息。</summary>
    /// <param name="hJob">作业句柄。</param>
    /// <param name="jobObjectInfoClass">信息类编号。</param>
    /// <param name="lpJobObjectInfo">信息数据。</param>
    /// <param name="cbJobObjectInfoLength">信息数据字节数。</param>
    /// <returns>是否成功。</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        nint hJob,
        int jobObjectInfoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    /// <summary>把进程加入作业。</summary>
    /// <param name="hJob">作业句柄。</param>
    /// <param name="hProcess">进程句柄。</param>
    /// <returns>是否成功。</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    /// <summary>关闭句柄。</summary>
    /// <param name="hObject">句柄。</param>
    /// <returns>是否成功。</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);
}

/// <summary>
/// Unix 信号后端：注册 <c>SIGHUP</c>（终端被关闭）与 <c>SIGTERM</c>，
/// 收到信号时执行同步清理回调后沿用默认终止语义。
/// <para>
/// 对应 Wails v3.1.0 PR #6371 的 Unix 语义。说明：OS 级行为（真实关闭终端后的 SIGHUP 投递）
/// 无法在单元测试中验证；单元测试仅覆盖可注入的纯逻辑路径（幂等释放 / 注册失败降级）。
/// </para>
/// </summary>
internal sealed class UnixSignalLifetimeBackend : IDevProcessLifetimeBackend
{
    /// <summary>已注册的信号处理器。</summary>
    private readonly List<PosixSignalRegistration> _registrations = new();

    /// <summary>信号触发时的同步清理回调，可为 null。</summary>
    private readonly Action? _onTerminalClosed;

    /// <summary>清理回调是否已执行（1 表示已执行），保证重复信号不重复清理。</summary>
    private int _cleanupInvoked;

    /// <summary>是否已释放（1 表示已释放）。</summary>
    private int _disposed;

    private UnixSignalLifetimeBackend(Action? onTerminalClosed)
    {
        _onTerminalClosed = onTerminalClosed;
    }

    /// <summary>
    /// 创建后端（仅 Unix 生效）。
    /// </summary>
    /// <param name="onTerminalClosed">信号触发时的同步清理回调。</param>
    /// <returns>至少注册一个信号的后端；Windows 或全部注册失败时返回空实现（静默降级）。</returns>
    internal static IDevProcessLifetimeBackend TryCreate(Action? onTerminalClosed)
    {
        if (OperatingSystem.IsWindows())
        {
            return NoOpProcessLifetimeBackend.Instance;
        }

        var backend = new UnixSignalLifetimeBackend(onTerminalClosed);
        backend.Register(PosixSignal.SIGHUP);
        backend.Register(PosixSignal.SIGTERM);

        if (backend._registrations.Count == 0)
        {
            backend.Dispose();
            return NoOpProcessLifetimeBackend.Instance;
        }

        return backend;
    }

    /// <inheritdoc/>
    public bool TryAttach(Process process)
    {
        // Unix 侧不按进程分配：信号处理器统一清理全部派生进程。
        return Volatile.Read(ref _disposed) == 0 && _registrations.Count > 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var registration in _registrations)
        {
            try
            {
                registration.Dispose();
            }
            catch (Exception)
            {
                // 注销尽力而为。
            }
        }

        _registrations.Clear();
    }

    /// <summary>
    /// 注册单个信号；单个信号注册失败不影响其余信号（静默降级）。
    /// </summary>
    /// <param name="signal">目标信号。</param>
    private void Register(PosixSignal signal)
    {
        try
        {
            _registrations.Add(PosixSignalRegistration.Create(signal, OnSignal));
        }
        catch (Exception)
        {
            // 平台不支持或信号不可注册：静默降级。
        }
    }

    /// <summary>
    /// 信号处理器：执行一次清理回调后保持默认终止语义。
    /// </summary>
    /// <param name="context">信号上下文；不设置 <see cref="PosixSignalContext.Cancel"/>，
    /// 以便信号默认动作（终止 CLI 进程）继续执行。</param>
    private void OnSignal(PosixSignalContext context)
    {
        if (Interlocked.Exchange(ref _cleanupInvoked, 1) != 0)
        {
            return;
        }

        try
        {
            _onTerminalClosed?.Invoke();
        }
        catch (Exception)
        {
            // 清理回调尽力而为，不得从信号处理器向外抛出。
        }
    }
}
