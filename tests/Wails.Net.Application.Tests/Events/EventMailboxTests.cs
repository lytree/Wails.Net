using System.Collections.Concurrent;
using TUnit.Core;
using Wails.Net.Application.Events;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Tests.Events;

/// <summary>
/// <see cref="EventMailbox{T}"/> 的单元测试（TUnit）。
/// 对应事件投递硬化 P0-1 / Layer 1（Wails v3 PR #5851）：
/// 验证 FIFO 顺序、发送永不阻塞、单消费者串行、异常隔离与空闲退出后重启。
/// </summary>
[NotInParallel]
public sealed class EventMailboxTests
{
    /// <summary>
    /// 轮询等待条件成立（避免固定 sleep 造成的偶发失败）。
    /// </summary>
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
    public async Task Send_SingleMessage_DrainsAsynchronously()
    {
        var consumed = new ConcurrentQueue<string>();
        var mailbox = new EventMailbox<string>(msg =>
        {
            consumed.Enqueue(msg);
            return Task.CompletedTask;
        });

        mailbox.Send("a");

        await WaitUntilAsync(() => consumed.Count == 1);
        await Assert.That(string.Join(",", consumed)).IsEqualTo("a");
    }

    [Test]
    public async Task Send_ManyMessages_ConsumesInFifoOrder()
    {
        const int count = 200;
        var consumed = new ConcurrentQueue<string>();
        var mailbox = new EventMailbox<int>(msg =>
        {
            consumed.Enqueue(msg.ToString());
            return Task.CompletedTask;
        });

        for (var i = 0; i < count; i++)
        {
            mailbox.Send(i);
        }

        await WaitUntilAsync(() => consumed.Count == count);
        await Assert.That(string.Join(",", consumed))
            .IsEqualTo(string.Join(",", Enumerable.Range(0, count)));
    }

    [Test]
    public async Task Send_ConcurrentSenders_ConsumesEveryMessageExactlyOnce()
    {
        const int count = 500;
        var consumed = new ConcurrentQueue<int>();
        var mailbox = new EventMailbox<int>(msg =>
        {
            consumed.Enqueue(msg);
            return Task.CompletedTask;
        });

        Parallel.For(0, count, i => mailbox.Send(i));

        await WaitUntilAsync(() => consumed.Count == count);
        await Assert.That(consumed.Distinct().Count()).IsEqualTo(count);
    }

    [Test]
    public async Task Send_ConsumerThrows_ContinuesDrainingRemainingMessages()
    {
        var consumed = new ConcurrentQueue<string>();
        var mailbox = new EventMailbox<string>(msg =>
        {
            consumed.Enqueue(msg);
            if (msg == "a")
            {
                throw new InvalidOperationException("消费失败");
            }

            return Task.CompletedTask;
        });

        mailbox.Send("a");
        mailbox.Send("b");

        await WaitUntilAsync(() => consumed.Count == 2);
        await Assert.That(string.Join(",", consumed)).IsEqualTo("a,b");
    }

    [Test]
    public async Task Send_ConsumerBlocksOnDownstream_RemainingMessagesStayPending()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new ManualResetEventSlim(false);
        var consumed = new ConcurrentQueue<string>();
        var mailbox = new EventMailbox<string>(async msg =>
        {
            consumed.Enqueue(msg);
            if (msg == "a")
            {
                entered.Set();
                await gate.Task;
            }
        });

        mailbox.Send("a");
        mailbox.Send("b");
        mailbox.Send("c");

        await WaitUntilAsync(() => entered.IsSet);
        await WaitUntilAsync(() => mailbox.PendingCount == 2);

        await Assert.That(mailbox.PendingCount).IsEqualTo(2);
        await Assert.That(consumed.Count).IsEqualTo(1);

        gate.SetResult(true);

        await WaitUntilAsync(() => consumed.Count == 3);
        await Assert.That(string.Join(",", consumed)).IsEqualTo("a,b,c");
    }

    [Test]
    public async Task Send_AfterIdleDrainExit_RestartsDrainTask()
    {
        var consumed = new ConcurrentQueue<string>();
        var mailbox = new EventMailbox<string>(msg =>
        {
            consumed.Enqueue(msg);
            return Task.CompletedTask;
        });

        mailbox.Send("first");
        await WaitUntilAsync(() => mailbox.PendingCount == 0);

        // 排空任务退出后再次投递，应重新启动消费者。
        mailbox.Send("second");

        await WaitUntilAsync(() => consumed.Count == 2);
        await Assert.That(string.Join(",", consumed)).IsEqualTo("first,second");
    }

    [Test]
    public async Task PendingCount_NewMailbox_IsZero()
    {
        var mailbox = new EventMailbox<string>(_ => Task.CompletedTask);

        await Assert.That(mailbox.PendingCount).IsEqualTo(0);
    }

    [Test]
    public async Task Constructor_NullConsumer_ThrowsArgumentNullException()
    {
        await Assert.That(() => new EventMailbox<string>(null!))
            .ThrowsExactly<ArgumentNullException>();
    }
}

/// <summary>
/// <see cref="EventProcessor"/> 与 Layer 1 邮箱的接线测试。
/// 验证并发 <c>Emit</c> 时前端监听器仍按 Emit 调用顺序收到事件。
/// </summary>
[NotInParallel]
public sealed class EventProcessorMailboxOrderingTests
{
    /// <summary>
    /// 记录事件名的桩 <see cref="IWailsEventListener"/>。
    /// </summary>
    private sealed class RecordingListener : IWailsEventListener
    {
        private readonly ConcurrentQueue<string> _names = new();

        /// <summary>已收到的事件名（保持到达顺序）。</summary>
        public IReadOnlyList<string> Names => _names.ToArray();

        /// <inheritdoc />
        public void NotifyEvent(string eventName, object? data, uint? senderWindowId = null, uint? targetWindowId = null)
            => _names.Enqueue(eventName);
    }

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
    public async Task Emit_ManyEvents_ListenersReceiveInEmitOrder()
    {
        const int count = 100;
        var processor = new EventProcessor();
        var listener = new RecordingListener();
        processor.AddWailsEventListener(listener);

        for (var i = 0; i < count; i++)
        {
            processor.Emit($"evt-{i}");
        }

        await WaitUntilAsync(() => listener.Names.Count == count);
        await Assert.That(string.Join(",", listener.Names))
            .IsEqualTo(string.Join(",", Enumerable.Range(0, count).Select(i => $"evt-{i}")));
    }

    [Test]
    public async Task Emit_ListenerThrows_LaterEventsStillDelivered()
    {
        var processor = new EventProcessor();
        var received = new ConcurrentQueue<string>();
        processor.AddWailsEventListener(new ThrowingThenRecordingListener(received));

        processor.Emit("boom");
        processor.Emit("ok");

        await WaitUntilAsync(() => received.Count == 2);
        await Assert.That(string.Join(",", received)).IsEqualTo("boom,ok");
    }

    /// <summary>
    /// 第一个事件抛出、后续事件正常记录的桩监听器。
    /// </summary>
    private sealed class ThrowingThenRecordingListener : IWailsEventListener
    {
        private readonly ConcurrentQueue<string> _received;

        public ThrowingThenRecordingListener(ConcurrentQueue<string> received) => _received = received;

        /// <inheritdoc />
        public void NotifyEvent(string eventName, object? data, uint? senderWindowId = null, uint? targetWindowId = null)
        {
            _received.Enqueue(eventName);
            if (eventName == "boom")
            {
                throw new InvalidOperationException("监听器失败");
            }
        }
    }
}
