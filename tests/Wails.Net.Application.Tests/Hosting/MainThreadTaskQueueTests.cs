using TUnit.Core;
using Wails.Net.Application.Hosting;

namespace Wails.Net.Application.Tests.Hosting;

/// <summary>
/// <see cref="MainThreadTaskQueue"/> 的单元测试（TUnit）。
/// 对应 Wails v3.1.0 #6287 / RH-HOST-02：Run 之前排队的主线程任务必须按 FIFO 执行，
/// 且单个任务失败不得吞掉后续任务。
/// </summary>
public sealed class MainThreadTaskQueueTests
{
    [Test]
    public async Task Flush_ExecutesInFifoOrder()
    {
        var queue = new MainThreadTaskQueue();
        var order = new List<int>();

        queue.Enqueue(() => order.Add(1));
        queue.Enqueue(() => order.Add(2));
        queue.Enqueue(() => order.Add(3));

        var executed = queue.Flush();

        await Assert.That(executed).IsEqualTo(3);
        await Assert.That(string.Join(",", order)).IsEqualTo("1,2,3");
        await Assert.That(queue.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Flush_ContinuesAfterException()
    {
        var queue = new MainThreadTaskQueue();
        var executed = 0;

        queue.Enqueue(() => throw new InvalidOperationException("boom"));
        queue.Enqueue(() => executed++);

        var count = queue.Flush();

        await Assert.That(count).IsEqualTo(1);
        await Assert.That(executed).IsEqualTo(1);
    }

    [Test]
    public async Task Flush_EmptyQueue_ReturnsZero()
    {
        var queue = new MainThreadTaskQueue();

        await Assert.That(queue.Flush()).IsEqualTo(0);
    }

    [Test]
    public async Task Enqueue_Null_Throws()
    {
        var queue = new MainThreadTaskQueue();

        await Assert.That(() => queue.Enqueue(null!)).Throws<ArgumentNullException>();
    }
}
