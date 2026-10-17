using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Transport;

/// <summary>
/// 每窗口有序事件投递队列（事件投递硬化 P0-1，对应 Wails v3 Go 版本
/// <c>WebviewWindow.eventQueue</c>，见 PR #5934 "deliver events in emit order via a per-window queue"）。
/// <para>
/// <b>解决的问题</b>：桌面模式下事件对每个窗口的投递此前是「fire-and-forget」——直接
/// <c>PostNativeMessageAsync</c> / <c>ExecJS</c> 或在 Task 上并发调度，同一窗口的消息可能交错或乱序。
/// 本队列为每个窗口维护独立的 FIFO 通道与单消费者，保证同一窗口的投递严格有序。
/// </para>
/// <para>
/// <b>语义</b>：
/// <list type="bullet">
/// <item>每窗口容量 <see cref="DefaultCapacity"/>（与 Go 版 <c>eventQueueCapacity = 64</c> 一致）。</item>
/// <item>溢出策略为<b>阻塞</b>而非丢弃：<see cref="Enqueue"/> 在队列满时阻塞调用线程等待槽位，
/// 形成对上游的背压。因此 <b>调用方不得为 UI 线程</b>——与 Go 版一致，实际被阻塞的是
/// <see cref="Events.EventMailbox{T}"/> 的排空任务，应用侧 <c>Emit</c> 调用永不阻塞。</item>
/// <item>窗口关闭时 <see cref="CloseWindowAsync"/> 唤醒所有被阻塞的生产者（其事件被丢弃），
/// 并等待消费者退出。</item>
/// </list>
/// </para>
/// <para>
/// <b>线程安全</b>：<see cref="_windows"/> 为并发字典；每窗口队列内部通过
/// <see cref="SemaphoreSlim"/> 计数槽位，生产者可在任意线程调用。
/// </para>
/// </summary>
internal sealed class PerWindowEventQueue
{
    /// <summary>
    /// 默认每窗口队列容量。与 Wails v3 <c>eventQueueCapacity = 64</c> 对齐
    /// （v3 实测 16/64/256/1024 对应 p99 延迟 179/257/329/427µs，64 为延迟与吞吐折中点）。
    /// </summary>
    public const int DefaultCapacity = 64;

    /// <summary>
    /// 窗口 ID → 该窗口的投递队列。
    /// </summary>
    private readonly ConcurrentDictionary<uint, WindowQueue> _windows = new();

    /// <summary>
    /// 每窗口容量。
    /// </summary>
    private readonly int _capacity;

    /// <summary>
    /// 可选日志记录器。
    /// </summary>
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造每窗口事件队列。
    /// </summary>
    /// <param name="capacity">每窗口容量；&lt;= 0 时回退到 <see cref="DefaultCapacity"/>。</param>
    /// <param name="logger">可选日志记录器。</param>
    public PerWindowEventQueue(int capacity = DefaultCapacity, ILogger? logger = null)
    {
        _capacity = capacity > 0 ? capacity : DefaultCapacity;
        _logger = logger;
    }

    /// <summary>
    /// 当前活跃的窗口队列数量（用于观测与测试）。
    /// </summary>
    public int WindowCount => _windows.Count;

    /// <summary>
    /// 将一条投递操作排入指定窗口的队列。
    /// <para>
    /// <b>调用方不得为 UI 线程</b>：队列满时本方法会阻塞等待槽位，直到消费者腾出空间或窗口关闭。
    /// </para>
    /// </summary>
    /// <param name="windowId">目标窗口 ID。</param>
    /// <param name="delivery">投递操作（实际调用平台发送 API）。</param>
    public void Enqueue(uint windowId, Func<Task> delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        var queue = _windows.GetOrAdd(windowId, id => new WindowQueue(id, _capacity, _logger));
        queue.Enqueue(delivery);
    }

    /// <summary>
    /// 投递一批定向到同一窗口的事件，保持入队顺序。
    /// </summary>
    /// <param name="windowIds">目标窗口 ID 集合。</param>
    /// <param name="deliveries">投递操作工厂：入参为窗口 ID，返回该窗口的投递操作。</param>
    public void EnqueueToWindows(IEnumerable<uint> windowIds, Func<uint, Func<Task>> deliveries)
    {
        ArgumentNullException.ThrowIfNull(windowIds);
        ArgumentNullException.ThrowIfNull(deliveries);

        foreach (var windowId in windowIds)
        {
            Enqueue(windowId, deliveries(windowId));
        }
    }

    /// <summary>
    /// 关闭指定窗口的队列：丢弃未消费的投递、唤醒被阻塞的生产者，并等待消费者退出。
    /// </summary>
    /// <param name="windowId">窗口 ID。</param>
    /// <returns>消费者退出的异步任务；窗口不存在时立即完成。</returns>
    public async Task CloseWindowAsync(uint windowId)
    {
        if (!_windows.TryRemove(windowId, out var queue))
        {
            return;
        }

        await queue.CloseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 关闭所有窗口队列（应用关闭路径）。每窗口消费者被并行等待。
    /// </summary>
    /// <returns>异步任务。</returns>
    public async Task CloseAllAsync()
    {
        var queues = new List<WindowQueue>(_windows.Count);
        foreach (var kvp in _windows)
        {
            if (_windows.TryRemove(kvp.Key, out var queue))
            {
                queues.Add(queue);
            }
        }

        await Task.WhenAll(queues.Select(q => q.CloseAsync())).ConfigureAwait(false);
    }

    /// <summary>
    /// 单窗口投递队列：无界 FIFO 通道 + 有界槽位信号量 + 单消费者。
    /// <para>
    /// 槽位信号量承担容量约束：生产者先取得槽位再写入通道，消费者消费完释放槽位。
    /// 通道本身无界，因此槽位计数即为「在途 + 待消费」消息数的上界。
    /// </para>
    /// </summary>
    private sealed class WindowQueue
    {
        private readonly uint _windowId;
        private readonly Channel<Func<Task>> _channel;
        private readonly SemaphoreSlim _slots;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _consumer;
        private readonly ILogger? _logger;

        /// <summary>关闭标志：0=活跃，1=已关闭。</summary>
        private int _closed;

        public WindowQueue(uint windowId, int capacity, ILogger? logger)
        {
            _windowId = windowId;
            _logger = logger;
            _channel = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            _slots = new SemaphoreSlim(capacity, capacity);
            _consumer = Task.Run(ConsumeAsync);
        }

        /// <summary>
        /// 取得槽位后写入通道；队列满时阻塞当前线程。
        /// </summary>
        public void Enqueue(Func<Task> delivery)
        {
            if (Volatile.Read(ref _closed) != 0)
            {
                return;
            }

            try
            {
                _slots.Wait(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 窗口已关闭，丢弃该事件（对应 Go 版 closeEventQueue 语义）。
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (Volatile.Read(ref _closed) != 0 || !_channel.Writer.TryWrite(delivery))
            {
                ReleaseSlot();
            }
        }

        /// <summary>
        /// 单消费者循环：按 FIFO 顺序串行执行投递操作。
        /// </summary>
        private async Task ConsumeAsync()
        {
            try
            {
                await foreach (var delivery in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await delivery().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // 单个窗口投递失败（窗口已销毁等）不影响其他事件。
                        _logger?.LogDebug(ex, "窗口 {WindowId} 事件投递失败", _windowId);
                    }
                    finally
                    {
                        ReleaseSlot();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 关闭时正常取消。
            }
        }

        /// <summary>
        /// 关闭队列：完成写入端、唤醒被阻塞的生产者、丢弃未消费消息，并等待消费者退出。
        /// </summary>
        public async Task CloseAsync()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                await _consumer.ConfigureAwait(false);
                return;
            }

            _channel.Writer.TryComplete();

            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            // 释放残留消息占用的槽位，避免仍阻塞的生产者永久等待。
            while (_channel.Reader.TryRead(out _))
            {
                ReleaseSlot();
            }

            try
            {
                await _consumer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "窗口 {WindowId} 事件队列消费者退出异常", _windowId);
            }
        }

        /// <summary>
        /// 释放一个槽位；容量耗尽或已释放时静默忽略。
        /// </summary>
        private void ReleaseSlot()
        {
            try
            {
                _slots.Release();
            }
            catch (SemaphoreFullException)
            {
                // 槽位计数已达容量（重复释放），忽略。
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
