using TUnit.Core;
using Wails.Net.Application.Streams;

namespace Wails.Net.Application.Tests.Streams;

/// <summary>
/// <see cref="StreamManager"/> 与 <see cref="StreamSession"/> 的单元测试（TUnit）：
/// 会话准入、全局额度、TTL 回收、reload 超越与窗口隔离。
/// 对应 Wails v3 Go 版本 <c>stream.go</c> 中的 <c>sessionWithAdmission</c> /
/// <c>reapStale</c> / <c>dropWindow</c> 与各全局额度常量。
/// </summary>
[NotInParallel]
public sealed class StreamSessionManagerTests
{
    [Test]
    public async Task SessionTtl_ReapRemovesIdleSessionAndAllowsRecreate()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager, "idle");
        await Assert.That(manager.ExistingSession("idle")).IsNotNull();

        manager.ReapStale(DateTimeOffset.UtcNow.AddSeconds(61));

        await Assert.That(manager.ExistingSession("idle")).IsNull();

        // idle reap 不推进代次水位：同窗口同代次可重建。
        var recreated = manager.SessionWithAdmission("idle", 1, 1, maySupersede: false);
        await Assert.That(recreated.Session).IsNotNull();
        await Assert.That(recreated.AtCapacity).IsFalse();
    }

    [Test]
    public async Task SessionWithLiveConnection_SurvivesTtlWithinGrace()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager, "live");
        StreamTestSupport.OpenConn(session, 1);

        manager.ReapStale(DateTimeOffset.UtcNow.AddSeconds(61));

        // 仍有连接的会话使用 10 分钟宽限，未到 TTL 之外的 60s 不回收。
        await Assert.That(manager.ExistingSession("live")).IsNotNull();
    }

    [Test]
    public async Task RetiredGeneration_CannotBeRecreated()
    {
        var manager = StreamTestSupport.NewManager();
        _ = StreamTestSupport.NewSession(manager, "gen1", windowId: 1, generation: 1, maySupersede: true);
        _ = StreamTestSupport.NewSession(manager, "gen2", windowId: 1, generation: 2, maySupersede: true);

        await Assert.That(manager.ExistingSession("gen1")).IsNull();

        var rejected = manager.SessionWithAdmission("gen1", 1, 1, maySupersede: true);

        await Assert.That(rejected.Session).IsNull();
        await Assert.That(rejected.AtCapacity).IsFalse();
    }

    [Test]
    public async Task ReloadSupersede_ClosesOldSessionAndItsConnections()
    {
        var manager = StreamTestSupport.NewManager();
        var oldSession = StreamTestSupport.NewSession(manager, "s1", windowId: 1, generation: 1, maySupersede: true);
        var oldConn = StreamTestSupport.OpenConn(oldSession, 1);

        _ = StreamTestSupport.NewSession(manager, "s2", windowId: 1, generation: 2, maySupersede: true);

        await Assert.That(manager.ExistingSession("s1")).IsNull();
        await Assert.That(oldConn.Closed.IsCancellationRequested).IsTrue();
        await Assert.That(manager.ExistingSession("s2")).IsNotNull();
    }

    [Test]
    public async Task Supersede_OnlyAffectsSameWindow()
    {
        var manager = StreamTestSupport.NewManager();
        var w1 = StreamTestSupport.NewSession(manager, "w1a", windowId: 1, generation: 1, maySupersede: true);

        _ = StreamTestSupport.NewSession(manager, "w2a", windowId: 2, generation: 1, maySupersede: true);
        _ = StreamTestSupport.NewSession(manager, "w2b", windowId: 2, generation: 2, maySupersede: true);

        await Assert.That(manager.ExistingSession("w2a")).IsNull();
        // 窗口 1 的会话不受窗口 2 的超越影响。
        await Assert.That(manager.ExistingSession("w1a")).IsNotNull();
        await Assert.That(w1.WindowId).IsEqualTo(1u);
    }

    [Test]
    public async Task SessionAdmission_PerWindowCap_RejectsBeyondLimit()
    {
        var manager = StreamTestSupport.NewManager();
        for (var i = 0; i < StreamConstants.MaxSessionsPerWindow; i++)
        {
            await Assert.That(manager.SessionWithAdmission($"s{i}", 1, 1, maySupersede: false).Session).IsNotNull();
        }

        var over = manager.SessionWithAdmission("s-over", 1, 1, maySupersede: false);

        await Assert.That(over.Session).IsNull();
        await Assert.That(over.AtCapacity).IsTrue();
    }

    [Test]
    public async Task GlobalOutboundQuota_ExhaustsThenReturnsToZero()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        // 占满全局出站字节额度（256 × 1 MiB = 256 MiB）。
        for (var i = 0; i < 256; i++)
        {
            await Assert.That(manager.TryReserveOutbound(StreamFrameKind.Data, 1 << 20)).IsTrue();
        }

        await Assert.That(manager.TryReserveOutbound(StreamFrameKind.Data, 1 << 20)).IsFalse();
        await Assert.That(manager.OutboundBytesUsed).IsEqualTo(256L << 20);

        // 全局触顶：连接发送失败。
        await Assert.That(conn.TrySend([1])).IsFalse();

        // 释放后归零，并可再次发送。
        for (var i = 0; i < 256; i++)
        {
            manager.ReleaseOutbound(StreamFrameKind.Data, 1 << 20);
        }

        await Assert.That(manager.OutboundBytesUsed).IsEqualTo(0L);
        await Assert.That(conn.TrySend([1])).IsTrue();
    }

    [Test]
    public async Task GlobalCloseQuota_IsIndependentOfControlsAndData()
    {
        var manager = StreamTestSupport.NewManager();

        for (var i = 0; i < StreamConstants.OutCloseDepthGlobal; i++)
        {
            await Assert.That(manager.TryReserveOutbound(StreamFrameKind.Close, 0)).IsTrue();
        }

        await Assert.That(manager.TryReserveOutbound(StreamFrameKind.Close, 0)).IsFalse();
        // 控制帧额度独立于 close 额度。
        await Assert.That(manager.TryReserveOutbound(StreamFrameKind.Error, 0)).IsTrue();
    }

    [Test]
    public async Task ControlFrames_RemainQueueableWhenDataIsFull()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        for (var i = 0; i < StreamConstants.OutQueueDepth; i++)
        {
            await Assert.That(conn.TrySend([1])).IsTrue();
        }

        await Assert.That(conn.TrySend([1])).IsFalse();

        // 数据占满后 open ack 仍可登记（独立控制额度）。
        await Assert.That(session.Open(2, StreamTestSupport.StreamName)).IsEqualTo(SessionOpenResult.Ok);

        // close 帧独立额度：即使数据已满，关闭连接仍能排队 close 通知。
        var second = session.Conn(2)!;
        second.Close();

        var poll = await session.AwaitFramesAsync(TimeSpan.FromSeconds(2), 1 << 30, CancellationToken.None);
        var kinds = poll.Frames.Where(f => f.ConnId == 2).Select(f => f.Kind).ToList();
        await Assert.That(kinds.Contains(StreamFrameKind.Open)).IsTrue();
        await Assert.That(kinds.Contains(StreamFrameKind.Close)).IsTrue();
    }

    [Test]
    public async Task DropWindow_ClosesOnlyTargetWindowSessions()
    {
        var manager = StreamTestSupport.NewManager();
        _ = StreamTestSupport.NewSession(manager, "win1", windowId: 1, generation: 1, maySupersede: false);
        _ = StreamTestSupport.NewSession(manager, "win2", windowId: 2, generation: 1, maySupersede: false);

        manager.DropWindow(1);

        await Assert.That(manager.ExistingSession("win1")).IsNull();
        await Assert.That(manager.ExistingSession("win2")).IsNotNull();
    }

    [Test]
    public async Task Dispose_ClosesAllSessionsAndReleasesConnections()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        manager.Dispose();

        await Assert.That(conn.Closed.IsCancellationRequested).IsTrue();
        await Assert.That(manager.SessionCount).IsEqualTo(0);
        // 关闭后拒绝新的准入。
        await Assert.That(manager.SessionWithAdmission("after", 1, 1, maySupersede: false).Session).IsNull();
    }

    [Test]
    public async Task Drain_AlwaysTakesAtLeastOneFrameEvenBeyondResponseCap()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        // 先取走 open ack，使队列只剩目标数据帧。
        await session.AwaitFramesAsync(TimeSpan.FromMilliseconds(10), StreamConstants.MaxResponseBytes, CancellationToken.None);

        var big = new byte[2 << 20]; // 2 MiB > 1 MiB 响应上限
        await Assert.That(conn.TrySend(big)).IsTrue();

        var poll = await session.AwaitFramesAsync(TimeSpan.FromSeconds(2), StreamConstants.MaxResponseBytes, CancellationToken.None);

        var dataFrames = poll.Frames.Where(f => f.Kind == StreamFrameKind.Data).ToList();
        await Assert.That(dataFrames.Count).IsEqualTo(1);
        await Assert.That(dataFrames[0].Payload!.Length).IsEqualTo(big.Length);
        await Assert.That(poll.More).IsFalse();
    }

    [Test]
    public async Task Drain_TruncatesAtResponseCapAndSetsMoreFlag()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        // 先取走 open ack。
        await session.AwaitFramesAsync(TimeSpan.FromMilliseconds(10), StreamConstants.MaxResponseBytes, CancellationToken.None);

        // 4 × 300009 字节 = 1,200,036 > 1 MiB：前 3 帧落入响应，more=true。
        var frame = new byte[300000];
        for (var i = 0; i < 4; i++)
        {
            await Assert.That(conn.TrySend(frame)).IsTrue();
        }

        var poll = await session.AwaitFramesAsync(TimeSpan.FromSeconds(2), StreamConstants.MaxResponseBytes, CancellationToken.None);

        var dataFrames = poll.Frames.Where(f => f.Kind == StreamFrameKind.Data).ToList();
        await Assert.That(dataFrames.Count).IsEqualTo(3);
        await Assert.That(poll.More).IsTrue();
    }

    [Test]
    public async Task SupersededPoll_ReturnsEmptyWithoutStealingFrames()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager);
        var conn = StreamTestSupport.OpenConn(session, 1);

        // 先取走 open ack，使队列为空。
        _ = await session.AwaitFramesAsync(TimeSpan.FromMilliseconds(10), StreamConstants.MaxResponseBytes, CancellationToken.None);

        // 第一个 poll 停放（空队列）。
        var first = session.AwaitFramesAsync(TimeSpan.FromSeconds(5), StreamConstants.MaxResponseBytes, CancellationToken.None);
        await Task.Delay(50);

        // 第二个 poll 到达即超越第一个（第一个应以空结果返回）。
        var secondTask = session.AwaitFramesAsync(TimeSpan.FromSeconds(2), StreamConstants.MaxResponseBytes, CancellationToken.None);
        await Task.Delay(50);

        // 唤醒第二个 poll。
        conn.TrySend([1, 2, 3]);

        var second = await secondTask;
        var firstResult = await first;

        await Assert.That(firstResult.Frames.Count).IsEqualTo(0);
        await Assert.That(firstResult.Alive).IsTrue();
        await Assert.That(second.Frames.Any(f => f.Kind == StreamFrameKind.Data)).IsTrue();
        await Assert.That(second.Alive).IsTrue();
    }

    [Test]
    public async Task NonSupersedingAdmission_DoesNotRetireExistingSession()
    {
        var manager = StreamTestSupport.NewManager();
        var s1 = StreamTestSupport.NewSession(manager, "s1", windowId: 1, generation: 1, maySupersede: true);

        // send 语义（maySupersede=false）：新 session id 不得退休同窗口旧会话。
        _ = StreamTestSupport.NewSession(manager, "s2", windowId: 1, generation: 2, maySupersede: false);

        await Assert.That(manager.ExistingSession("s1")).IsNotNull();
        await Assert.That(s1.ConnCount()).IsEqualTo(0);
    }
}
