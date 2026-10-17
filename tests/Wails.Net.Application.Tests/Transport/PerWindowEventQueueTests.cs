using TUnit.Core;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// <see cref="PerWindowEventQueue"/> 的单元测试（TUnit）。
/// 对应事件投递硬化 P0-1 / Layer 2（Wails v3 PR #5934）：
/// 验证每窗口 FIFO、窗口间互不阻塞、容量耗尽时阻塞（背压）而非丢弃、
/// 窗口关闭时唤醒被阻塞的生产者并等待消费者退出。
/// </summary>
[NotInParallel]
public sealed class PerWindowEventQueueTests
{
    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!predicate())
        {
            if (Environment.TickCount64 >= deadline)
            {
                throw new TimeoutException("等待条件超时。");
            }

            await Task.Delay(10);
        }
    }

    [Test]
    public async Task Enqueue_SingleDelivery_ExecutesIt()
    {
        var queue = new PerWindowEventQueue();
        var delivered = new List<int>();

        queue.Enqueue(1, () =>
        {
            lock (delivered)
            {
                delivered.Add(1);
            }

            return Task.CompletedTask;
        });

        await WaitUntilAsync(() =>
        {
            lock (delivered)
            {
                return delivered.Count == 1;
            }
        });
    }

    [Test]
    public async Task Enqueue_ManyDeliveries_ExecutesInFifoOrder()
    {
        const int count = 100;
        var queue = new PerWindowEventQueue();
        var order = new List<int>();

        for (var i = 0; i < count; i++)
        {
            var value = i;
            queue.Enqueue(1, () =>
            {
                lock (order)
                {
                    order.Add(value);
                }

                return Task.CompletedTask;
            });
        }

        await WaitUntilAsync(() =>
        {
            lock (order)
            {
                return order.Count == count;
            }
        });

        string observed;
        lock (order)
        {
            observed = string.Join(",", order);
        }

        await Assert.That(observed).IsEqualTo(string.Join(",", Enumerable.Range(0, count)));
    }

    [Test]
    public async Task Enqueue_SecondWindow_NotBlockedBySlowFirstWindow()
    {
        var queue = new PerWindowEventQueue();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWindowDelivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 窗口 1 的消费者被长时间阻塞。
        queue.Enqueue(1, () => gate.Task);

        // 窗口 2 的投递应立即可执行，不受窗口 1 阻塞影响。
        queue.Enqueue(2, () =>
        {
            secondWindowDelivered.TrySetResult(true);
            return Task.CompletedTask;
        });

        await secondWindowDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gate.SetResult(true);

        await queue.CloseAllAsync();
    }

    [Test]
    public async Task Enqueue_QueueFull_BlocksUntilSlotReleased()
    {
        var queue = new PerWindowEventQueue(capacity: 1);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 第 1 条投递占用唯一槽位并阻塞消费者。
        queue.Enqueue(1, () => gate.Task);

        var producerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var producer = Task.Run(() =>
        {
            producerStarted.SetResult(true);
            queue.Enqueue(1, () => Task.CompletedTask);
        });

        await producerStarted.Task;
        await Task.Delay(200);

        // 容量已满：生产者必须阻塞等待槽位，而不是丢弃事件。
        await Assert.That(producer.IsCompleted).IsFalse();

        gate.SetResult(true);
        await producer.WaitAsync(TimeSpan.FromSeconds(5));

        await queue.CloseWindowAsync(1);
        await Assert.That(queue.WindowCount).IsEqualTo(0);
    }

    [Test]
    public async Task CloseWindowAsync_ReleasesBlockedProducerAndDropsPendingEvent()
    {
        var queue = new PerWindowEventQueue(capacity: 1);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterEvents = new List<int>();

        queue.Enqueue(1, () => gate.Task);

        var producerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var producer = Task.Run(() =>
        {
            producerStarted.SetResult(true);
            queue.Enqueue(1, () =>
            {
                lock (laterEvents)
                {
                    laterEvents.Add(1);
                }

                return Task.CompletedTask;
            });
        });

        await producerStarted.Task;
        await Task.Delay(100);
        await Assert.That(producer.IsCompleted).IsFalse();

        var closeTask = queue.CloseWindowAsync(1);

        // 关闭应唤醒被阻塞的生产者（其事件被丢弃，对应 Go 版 closeEventQueue 语义）。
        await producer.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(queue.WindowCount).IsEqualTo(0);

        // 放行被阻塞的消费者，使关闭流程得以完成。
        gate.SetResult(true);
        await closeTask.WaitAsync(TimeSpan.FromSeconds(5));

        int laterCount;
        lock (laterEvents)
        {
            laterCount = laterEvents.Count;
        }

        await Assert.That(laterCount).IsEqualTo(0);
    }

    [Test]
    public async Task CloseWindowAsync_UnknownWindow_CompletesImmediately()
    {
        var queue = new PerWindowEventQueue();

        await queue.CloseWindowAsync(999).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task CloseAllAsync_RemovesEveryWindowQueue()
    {
        var queue = new PerWindowEventQueue();
        queue.Enqueue(1, () => Task.CompletedTask);
        queue.Enqueue(2, () => Task.CompletedTask);
        queue.Enqueue(3, () => Task.CompletedTask);

        await queue.CloseAllAsync();

        await Assert.That(queue.WindowCount).IsEqualTo(0);
    }

    [Test]
    public async Task EnqueueToWindows_DeliversOneEventPerWindow()
    {
        var queue = new PerWindowEventQueue();
        var delivered = new List<uint>();

        queue.EnqueueToWindows([1, 2, 3], windowId => () =>
        {
            lock (delivered)
            {
                delivered.Add(windowId);
            }

            return Task.CompletedTask;
        });

        await WaitUntilAsync(() =>
        {
            lock (delivered)
            {
                return delivered.Count == 3;
            }
        });

        string observed;
        lock (delivered)
        {
            observed = string.Join(",", delivered.OrderBy(x => x));
        }

        await Assert.That(observed).IsEqualTo("1,2,3");

        await queue.CloseAllAsync();
    }

    [Test]
    public async Task Enqueue_NullDelivery_ThrowsArgumentNullException()
    {
        var queue = new PerWindowEventQueue();

        await Assert.That(() => queue.Enqueue(1, null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task EnqueueToWindows_NullArguments_ThrowArgumentNullException()
    {
        var queue = new PerWindowEventQueue();

        await Assert.That(() => queue.EnqueueToWindows(null!, _ => () => Task.CompletedTask))
            .ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => queue.EnqueueToWindows([1], null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task WindowCount_TracksDistinctWindows()
    {
        var queue = new PerWindowEventQueue();
        queue.Enqueue(1, () => Task.CompletedTask);
        queue.Enqueue(2, () => Task.CompletedTask);
        queue.Enqueue(1, () => Task.CompletedTask);

        await Assert.That(queue.WindowCount).IsEqualTo(2);

        await queue.CloseAllAsync();
    }
}
