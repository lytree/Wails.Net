namespace Wails.Net.Application.SystemTray;

/// <summary>
/// 托盘图标重建去抖调度器。
/// <para>
/// 对应 Wails v3 <c>v3.1.0</c> PR #6386：Explorer 重启时会广播多次 <c>TaskbarCreated</c>，
/// 托盘图标必须在广播停止后延迟 1 秒再重新注册，避免重启过程中反复 NIM_ADD 造成闪烁或失败。
/// </para>
/// <para>
/// 语义：窗口期内多次 <see cref="Request"/> 合并为一次重建；重建发生在最后一次请求之后
/// <see cref="DefaultDelay"/>；<see cref="Dispose"/> 后不再触发重建。
/// </para>
/// </summary>
internal sealed class TrayIconRebuildScheduler : IDisposable
{
    /// <summary>
    /// 默认去抖延迟（1 秒）。对应上游 #6386 的 1 秒去抖。
    /// </summary>
    internal static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1);

    /// <summary>重建回调。</summary>
    private readonly Action _rebuild;

    /// <summary>去抖延迟。</summary>
    private readonly TimeSpan _delay;

    /// <summary>同步锁，保护计时器与释放状态。</summary>
    private readonly object _gate = new();

    /// <summary>延迟计时器；首次请求时才创建。</summary>
    private System.Threading.Timer? _timer;

    /// <summary>是否已释放。</summary>
    private bool _disposed;

    /// <summary>
    /// 构造调度器。
    /// </summary>
    /// <param name="rebuild">重建回调；在计时器线程执行，实现方需自行保证线程安全。</param>
    /// <param name="delay">去抖延迟；为 null 时使用 <see cref="DefaultDelay"/>（测试可注入更短延迟）。</param>
    internal TrayIconRebuildScheduler(Action rebuild, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(rebuild);
        _rebuild = rebuild;
        _delay = delay ?? DefaultDelay;
    }

    /// <summary>
    /// 请求一次重建：重置去抖计时器，窗口期内多次请求只触发一次重建。
    /// </summary>
    internal void Request()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer ??= new System.Threading.Timer(_ => Execute(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// 计时器到点：已释放时丢弃，否则在锁内执行重建。
    /// <para>
    /// 重建回调在 <c>_gate</c> 内执行，<see cref="Dispose"/> 需要等待正在执行的回调结束，
    /// 因此 <see cref="Dispose"/> 返回后不会再触发重建，也不会出现“释放后仍 NIM_ADD”的窗口。
    /// </para>
    /// </summary>
    private void Execute()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _rebuild();
        }
    }

    /// <summary>
    /// 释放调度器并取消尚未触发的重建。可安全重复调用。
    /// </summary>
    public void Dispose()
    {
        System.Threading.Timer? timer;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();
    }
}
