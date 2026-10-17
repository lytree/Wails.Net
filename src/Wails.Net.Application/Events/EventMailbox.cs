namespace Wails.Net.Application.Events;

/// <summary>
/// 通用异步 FIFO 邮箱（事件投递硬化 P0-1，对应 Wails v3 Go 版本
/// <c>v3/internal/mailbox/mailbox.go</c>，见 PR #5851 "preserve frontend custom event order"）。
/// <para>
/// <b>解决的问题</b>：并发调用 <see cref="EventProcessor.Emit"/> 时，各次发布对每个窗口的
/// 派发相互交错，导致后发布的事件可能先到达前端（乱序）。本邮箱把「前端派发」收敛为
/// 单一消费者串行执行，使到达顺序等于 <see cref="Send"/> 的调用顺序。
/// </para>
/// <para>
/// <b>语义（与 Go 版一致）</b>：
/// <list type="bullet">
/// <item><see cref="Send"/> 永不阻塞——仅在锁内入队，必要时懒启动排空任务。</item>
/// <item>队列无界（不承担背压职责）；每窗口的容量约束由
/// <see cref="Transport.PerWindowEventQueue"/> 承担。</item>
/// <item>单消费者按 FIFO 顺序串行消费；队列空即退出，不留常驻 worker。</item>
/// <item>单条消息消费异常被吞掉，不中断邮箱。</item>
/// </list>
/// </para>
/// <para>
/// <b>与 Wails v3 的差异</b>：消费回调为异步（<c>Func&lt;T, Task&gt;</c>），因此排空任务在
/// 下游背压时以异步方式等待而非阻塞线程，不会占用线程池线程。
/// </para>
/// <para>
/// <b>线程安全</b>：<see cref="Send"/> 可在任意线程调用（包括 UI 线程），入队与排空启动
/// 在 <c>_gate</c> 锁内原子完成。
/// </para>
/// </summary>
/// <typeparam name="T">消息类型。</typeparam>
internal sealed class EventMailbox<T>
{
    /// <summary>
    /// 保护 <see cref="_pending"/> 与 <see cref="_draining"/> 的锁。
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// 待消费消息队列（FIFO）。
    /// </summary>
    private readonly Queue<T> _pending = new();

    /// <summary>
    /// 单消费者回调。
    /// </summary>
    private readonly Func<T, Task> _consume;

    /// <summary>
    /// 是否已有排空任务在运行（0/1 布尔语义，仅锁内读写）。
    /// </summary>
    private bool _draining;

    /// <summary>
    /// 构造邮箱。
    /// </summary>
    /// <param name="consume">单消费者回调，按 FIFO 顺序串行调用。</param>
    public EventMailbox(Func<T, Task> consume)
    {
        _consume = consume ?? throw new ArgumentNullException(nameof(consume));
    }

    /// <summary>
    /// 当前待消费消息数（用于观测与测试）。
    /// </summary>
    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>
    /// 入队一条消息；永不阻塞调用方。
    /// 首次入队（且无排空任务在运行）时懒启动排空任务。
    /// </summary>
    /// <param name="message">消息。</param>
    public void Send(T message)
    {
        var startDrain = false;
        lock (_gate)
        {
            _pending.Enqueue(message);
            if (!_draining)
            {
                _draining = true;
                startDrain = true;
            }
        }

        if (startDrain)
        {
            _ = Task.Run(DrainAsync);
        }
    }

    /// <summary>
    /// 单消费者排空循环：按 FIFO 顺序取出并串行消费，队列空时退出并复位排空标志。
    /// </summary>
    private async Task DrainAsync()
    {
        while (true)
        {
            T message;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    // 退出前复位标志：复位后若刚好有新消息入队，会重新启动排空任务。
                    _draining = false;
                    return;
                }

                message = _pending.Dequeue();
            }

            try
            {
                await _consume(message).ConfigureAwait(false);
            }
            catch
            {
                // 单条消息消费失败不应中断邮箱（由消费方自行记录日志）。
            }
        }
    }
}
