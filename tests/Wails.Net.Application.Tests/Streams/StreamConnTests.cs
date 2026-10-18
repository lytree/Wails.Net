using System.Text;
using TUnit.Core;
using Wails.Net.Application.Streams;

namespace Wails.Net.Application.Tests.Streams;

/// <summary>
/// <see cref="StreamConn"/> 的单元测试（TUnit）：发送 / 接收、出站与入站背压、帧大小上限与关闭语义。
/// 对应 Wails v3 Go 版本 <c>stream.go</c> 中的 <c>StreamConn.Send</c> / <c>TrySend</c> /
/// <c>Receive</c> / <c>Close</c> / <c>deliver</c>。
/// </summary>
[NotInParallel]
public sealed class StreamConnTests
{
    [Test]
    public async Task TrySend_FillsDepthThenFails()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        for (var i = 0; i < StreamConstants.OutQueueDepth; i++)
        {
            await Assert.That(conn.TrySend([1])).IsTrue();
        }

        await Assert.That(conn.TrySend([1])).IsFalse();
    }

    [Test]
    public async Task ByteLimit_TriggersBeforeDepth()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        var oneMiB = new byte[1 << 20];

        // 前 8 帧（8 MiB）成功；第 9 帧因字节上限失败（远早于 depth=256）。
        for (var i = 0; i < 8; i++)
        {
            await Assert.That(conn.TrySend(oneMiB)).IsTrue();
        }

        await Assert.That(conn.TrySend([1])).IsFalse();
    }

    [Test]
    public async Task SingleFrameLargerThanByteCap_IsAcceptedWhenQueueEmpty()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        var oversized = new byte[9 << 20]; // 9 MiB > 8 MiB 局部字节上限
        await Assert.That(conn.TrySend(oversized)).IsTrue();
        // 队列不再为空后，下一帧即受字节上限拒绝。
        await Assert.That(conn.TrySend([1])).IsFalse();
    }

    [Test]
    public async Task SendAsync_BlocksUntilDrain()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        for (var i = 0; i < StreamConstants.OutQueueDepth; i++)
        {
            await Assert.That(conn.TrySend([1])).IsTrue();
        }

        var pending = conn.SendAsync([2]);
        await Task.Delay(50);
        await Assert.That(pending.IsCompleted).IsFalse();

        var poll = await session.AwaitFramesAsync(
            TimeSpan.FromMilliseconds(10), StreamConstants.MaxResponseBytes, CancellationToken.None);
        await Assert.That(poll.Alive).IsTrue();

        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(pending.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task FrameOver64MiB_IsRejected()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        var tooLarge = new byte[StreamConstants.MaxFrameBytes + 1];
        await Assert.That(async () => await conn.SendAsync(tooLarge)).ThrowsExactly<StreamTooLargeException>();
        await Assert.That(() => conn.TrySend(tooLarge)).ThrowsExactly<StreamTooLargeException>();
    }

    [Test]
    public async Task FrameExactly64MiB_IsAccepted()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        var exactly = new byte[StreamConstants.MaxFrameBytes];
        await Assert.That(conn.TrySend(exactly)).IsTrue();
    }

    [Test]
    public async Task Inbound_IsBoundedByDepth()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        for (var i = 0; i < StreamConstants.InQueueDepth; i++)
        {
            await Assert.That(await conn.DeliverAsync([1], block: false, CancellationToken.None))
                .IsEqualTo(StreamDeliverResult.Delivered);
        }

        await Assert.That(await conn.DeliverAsync([1], block: false, CancellationToken.None))
            .IsEqualTo(StreamDeliverResult.Full);
    }

    [Test]
    public async Task Receive_ReturnsFramesInOrder()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        for (var i = 0; i < 100; i++)
        {
            var payload = BitConverter.GetBytes(i);
            await Assert.That(await conn.DeliverAsync(payload, block: false, CancellationToken.None))
                .IsEqualTo(StreamDeliverResult.Delivered);
        }

        for (var i = 0; i < 100; i++)
        {
            var frame = await conn.ReceiveAsync();
            await Assert.That(BitConverter.ToInt32(frame)).IsEqualTo(i);
        }

        await Assert.That(manager.InboundBytesUsed).IsEqualTo(0L);
    }

    [Test]
    public async Task ReceiveJson_RoundTrips()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        var payload = Encoding.UTF8.GetBytes("{\"value\":42}");
        await conn.DeliverAsync(payload, block: false, CancellationToken.None);

        var received = await conn.ReceiveJsonAsync<Dictionary<string, int>>();
        await Assert.That(received["value"]).IsEqualTo(42);
    }

    [Test]
    public async Task SessionClose_EndsSendAndReceiveWithClosed()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        session.Close();

        await Assert.That(async () => await conn.SendAsync([1])).ThrowsExactly<StreamClosedException>();
        await Assert.That(async () => await conn.ReceiveAsync()).ThrowsExactly<StreamClosedException>();
        await Assert.That(conn.Closed.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task Close_IsIdempotentAndUnblocksReceive()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        var pending = conn.ReceiveAsync();
        await Task.Delay(20);
        await Assert.That(pending.IsCompleted).IsFalse();

        conn.Close();
        conn.Close();

        await Assert.That(async () => await pending).ThrowsExactly<StreamClosedException>();
    }

    [Test]
    public async Task HandlerReturn_ClosesConnection()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = StreamTestSupport.NewManager(async _ =>
        {
            handlerStarted.TrySetResult();
            await gate.Task.ConfigureAwait(false);
        });
        var session = StreamTestSupport.NewSession(manager);

        session.Open(1, StreamTestSupport.StreamName);
        var conn = session.Conn(1)!;
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // handler 返回 → 连接关闭。
        gate.TrySetResult();
        await StreamTestSupport.WaitUntilAsync(() => conn.Closed.IsCancellationRequested, TimeSpan.FromSeconds(5));

        await Assert.That(conn.Closed.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task ConcurrentSends_SingleDrainerPreservesPerProducerOrder()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        const int producers = 5;
        const int perProducer = 20;

        await Task.WhenAll(Enumerable.Range(0, producers).Select(p => Task.Run(() =>
        {
            for (var i = 0; i < perProducer; i++)
            {
                var payload = new byte[8];
                BitConverter.TryWriteBytes(payload.AsSpan(0), p);
                BitConverter.TryWriteBytes(payload.AsSpan(4), i);
                if (!conn.TrySend(payload))
                {
                    throw new InvalidOperationException("TrySend 意外失败（队列应足够容纳并发探针）");
                }
            }
        })));

        var poll = await session.AwaitFramesAsync(TimeSpan.FromSeconds(2), 1 << 30, CancellationToken.None);
        var dataFrames = poll.Frames.Where(f => f.Kind == StreamFrameKind.Data).ToList();

        await Assert.That(dataFrames.Count).IsEqualTo(producers * perProducer);

        // 每个生产者的帧必须按其生产顺序出现（单 drainer 顺序一致）。
        var lastPerProducer = new int[producers];
        for (var p = 0; p < producers; p++)
        {
            lastPerProducer[p] = -1;
        }

        foreach (var frame in dataFrames)
        {
            var producer = BitConverter.ToInt32(frame.Payload!.AsSpan(0));
            var seq = BitConverter.ToInt32(frame.Payload!.AsSpan(4));
            await Assert.That(seq).IsEqualTo(lastPerProducer[producer] + 1);
            lastPerProducer[producer] = seq;
        }
    }
}
