using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// 主线程任务队列（Wails v3.1.0 #6287，RH-HOST-02 抽出）。
/// <para>
/// 主循环尚未启动时，<c>DispatchOnMainThread</c> 不能立即执行（线程错误）也不应丢弃任务，
/// 因此先入队；<see cref="Wails.Net.Application.Application.Run"/> 在进入平台主循环前
/// 在当前（主）线程上按 FIFO 顺序 <see cref="Flush"/>。
/// </para>
/// </summary>
public sealed class MainThreadTaskQueue
{
    private readonly ConcurrentQueue<Action> _pending = new();

    /// <summary>
    /// 当前排队中的任务数量（诊断用）。
    /// </summary>
    public int Count => _pending.Count;

    /// <summary>
    /// 将任务入队。
    /// </summary>
    /// <param name="action">待执行的主线程任务，不能为 null。</param>
    public void Enqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _pending.Enqueue(action);
    }

    /// <summary>
    /// 按 FIFO 顺序执行所有排队任务。仅在主线程上调用。
    /// 单个任务抛出的异常被记录后继续执行后续任务（避免一个任务的失败吞掉其余任务）。
    /// </summary>
    /// <param name="logger">日志记录器，可为 null。</param>
    /// <returns>实际执行的任务数量。</returns>
    public int Flush(ILogger? logger = null)
    {
        var executed = 0;

        while (_pending.TryDequeue(out var action))
        {
            try
            {
                action();
                executed++;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "执行 Run 之前排队的主线程任务失败");
            }
        }

        return executed;
    }
}
