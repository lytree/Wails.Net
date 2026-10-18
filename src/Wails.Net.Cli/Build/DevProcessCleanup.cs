using System.ComponentModel;
using System.Diagnostics;

namespace Wails.Net.Cli.Build;

/// <summary>
/// dev 派生进程的统一清理抽象。
/// <para>
/// 对应 Wails v3 beta.28 <c>wails dev</c> 的退出清理完善（v3.0.0-beta.25 / PR #6048）：
/// dev 退出（正常结束、Ctrl+C、异常）时必须可靠终止全部派生进程——<c>dotnet watch</c>
/// 及其派生的应用进程、前端 dev server 进程（含其子进程树），避免遗留孤儿进程。
/// </para>
/// <para>
/// 实现约定（实现方必须满足，供 <see cref="DevProcessCleanup"/> 在退出清理路径安全调用）：
/// <list type="bullet">
/// <item><see cref="HasExited"/> 不得抛出异常（查询竞态时按“已退出/无需清理”处理）；</item>
/// <item><see cref="KillTree"/> 为尽力而为，不得向外抛出异常；</item>
/// <item><see cref="WaitForExitAsync"/> 在超时或外部取消时返回 <c>false</c>，不抛出 <see cref="OperationCanceledException"/>。</item>
/// </list>
/// </para>
/// </summary>
internal interface IDevProcessHandle
{
    /// <summary>进程是否已退出（或尚未成功启动，此时无需清理）。</summary>
    bool HasExited { get; }

    /// <summary>
    /// 强制终止进程树，包含所有派生子进程。
    /// 跨平台统一走 .NET 的 <c>Process.Kill(entireProcessTree: true)</c>
    /// （Windows 进程树终止 / Unix 同语义），对应 Go 版本 dev 退出时对派生进程的终止。
    /// </summary>
    void KillTree();

    /// <summary>
    /// 在有限时间内等待进程退出（超时兜底，避免退出路径被无响应进程拖住）。
    /// </summary>
    /// <param name="timeout">等待上限（超时兜底）；非正值表示不设超时，仅由 <paramref name="cancellationToken"/> 取消。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <returns>进程是否在超时前退出。</returns>
    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于 <see cref="System.Diagnostics.Process"/> 的 <see cref="IDevProcessHandle"/> 适配器。
/// </summary>
internal sealed class ProcessTreeHandle : IDevProcessHandle
{
    /// <summary>目标进程；为 null 时表示无进程可清理。</summary>
    private readonly Process? _process;

    /// <summary>进程是否已成功启动（<c>Process.Start()</c> 是否返回 true）。</summary>
    private readonly bool _started;

    /// <summary>
    /// 构造适配器。
    /// </summary>
    /// <param name="process">目标进程；为 null 时表示无进程可清理。</param>
    /// <param name="started">进程是否已成功启动。</param>
    internal ProcessTreeHandle(Process? process, bool started)
    {
        _process = process;
        _started = started;
    }

    /// <inheritdoc/>
    public bool HasExited
    {
        get
        {
            if (!_started || _process is null)
            {
                return true;
            }

            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // 进程未启动或句柄已释放：无需清理。
                return true;
            }
        }
    }

    /// <inheritdoc/>
    public void KillTree()
    {
        if (!_started || _process is null)
        {
            return;
        }

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            /* 进程未启动或已退出 */
        }
        catch (Win32Exception)
        {
            /* 进程已退出或无法终止（竞态）：尽力而为 */
        }
        catch (NotSupportedException)
        {
            /* 平台不支持强制终止进程树 */
        }
    }

    /// <inheritdoc/>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!_started || _process is null)
        {
            return true;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout > TimeSpan.Zero)
            {
                cts.CancelAfter(timeout);
            }

            await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            // 超时或外部取消：未能确认退出（超时兜底）。
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}

/// <summary>
/// dev 退出清理逻辑：终止给定进程树并在有限时间内等待，避免遗留孤儿进程。
/// <para>
/// 正常结束路径上进程通常已退出，直接跳过（不产生额外等待）；仅对仍在运行的进程执行
/// <see cref="IDevProcessHandle.KillTree"/> 并以超时兜底等待，从而不在正常路径上拖慢退出。
/// </para>
/// </summary>
internal static class DevProcessCleanup
{
    /// <summary>
    /// 默认终止等待上限（1000 毫秒）。
    /// <para>
    /// 取有限小值的原因：<c>Process.Kill(entireProcessTree: true)</c> 同步发出终止信号，
    /// 孤儿进程问题在 Kill 调用时即已解决；此处的等待仅用于确认退出，且需控制在
    /// System.CommandLine 的进程终止宽限期（<c>InvocationConfiguration.ProcessTerminationTimeout</c>，
    /// 默认 2 秒）之内，确保 Ctrl+C 路径上的清理能够完成。
    /// </para>
    /// </summary>
    internal static readonly TimeSpan DefaultTerminationTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 终止给定进程树集合，并在有限时间内等待其退出。
    /// </summary>
    /// <param name="handles">待清理的进程句柄集合；null 元素忽略。</param>
    /// <param name="timeout">每个进程的等待上限。</param>
    /// <param name="cancellationToken">外部取消令牌。</param>
    /// <returns>清理结果统计。</returns>
    internal static async Task<DevCleanupResult> TerminateAsync(
        IReadOnlyList<IDevProcessHandle> handles,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handles);

        var killed = 0;
        var alreadyExited = 0;
        var waits = new List<Task<bool>>(handles.Count);

        foreach (var handle in handles)
        {
            if (handle is null)
            {
                continue;
            }

            if (handle.HasExited)
            {
                alreadyExited++;
                continue;
            }

            handle.KillTree();
            killed++;
            waits.Add(handle.WaitForExitAsync(timeout, cancellationToken));
        }

        var timedOut = 0;
        if (waits.Count > 0)
        {
            var results = await Task.WhenAll(waits).ConfigureAwait(false);
            foreach (var exited in results)
            {
                if (!exited)
                {
                    timedOut++;
                }
            }
        }

        return new DevCleanupResult(killed, alreadyExited, timedOut);
    }
}

/// <summary>
/// dev 退出清理结果统计。
/// </summary>
/// <param name="Killed">被强制终止（<see cref="IDevProcessHandle.KillTree"/>）的进程数。</param>
/// <param name="AlreadyExited">退出前已结束、无需终止的进程数。</param>
/// <param name="TimedOut">发出终止后仍在等待上限内未确认退出的进程数。</param>
internal readonly record struct DevCleanupResult(int Killed, int AlreadyExited, int TimedOut);
