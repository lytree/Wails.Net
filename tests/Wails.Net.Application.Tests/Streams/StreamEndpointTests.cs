using System.Text;
using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Streams;
using Wails.Net.Application.Webview;

namespace Wails.Net.Application.Tests.Streams;

/// <summary>
/// Streams 端点（<c>StreamManager.HandleAsync</c>）的状态码与协议行为测试（TUnit），
/// 以及 <see cref="WebviewRequestPipeline"/> 的 Streams 路由测试。
/// 对应 Wails v3 Go 版本 <c>stream_transport.go</c> 中的 <c>serveStreamPoll</c> / <c>serveStreamSend</c>
/// 与 <c>stream.go</c> 中的 <c>App.sessionFor</c>。
/// </summary>
[NotInParallel]
public sealed class StreamEndpointTests
{
    [Test]
    public async Task Poll_Head_Returns405WithAllowGet()
    {
        var manager = StreamTestSupport.NewManager();

        var response = await manager.HandleAsync(StreamTestSupport.Poll(1, "s", "1", "HEAD"), CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(405);
        await Assert.That(response.Headers).IsNotNull();
        await Assert.That(response.Headers!["Allow"]).IsEqualTo("GET");
    }

    [Test]
    public async Task Poll_MissingGeneration_Returns410()
    {
        var manager = StreamTestSupport.NewManager();

        var response = await manager.HandleAsync(StreamTestSupport.Poll(1, "s", generation: null), CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(410);
    }

    [Test]
    public async Task Poll_ZeroOrBadGeneration_Returns400()
    {
        var manager = StreamTestSupport.NewManager();

        var zero = await manager.HandleAsync(StreamTestSupport.Poll(1, "s", "0"), CancellationToken.None);
        var bad = await manager.HandleAsync(StreamTestSupport.Poll(1, "s", "not-a-number"), CancellationToken.None);

        await Assert.That(zero.StatusCode).IsEqualTo(400);
        await Assert.That(bad.StatusCode).IsEqualTo(400);
    }

    [Test]
    public async Task Poll_MissingSession_Returns400()
    {
        var manager = StreamTestSupport.NewManager();

        var response = await manager.HandleAsync(StreamTestSupport.Poll(1, string.Empty, "1"), CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(400);
    }

    [Test]
    public async Task Poll_SessionBelongsToAnotherWindow_Returns403()
    {
        var manager = StreamTestSupport.NewManager();
        _ = StreamTestSupport.NewSession(manager, "s", windowId: 1, generation: 1, maySupersede: false);

        var response = await manager.HandleAsync(StreamTestSupport.Poll(2, "s", "1"), CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(403);
    }

    [Test]
    public async Task Poll_GenerationMismatch_Returns403()
    {
        var manager = StreamTestSupport.NewManager();
        _ = StreamTestSupport.NewSession(manager, "s", windowId: 1, generation: 1, maySupersede: false);

        var response = await manager.HandleAsync(StreamTestSupport.Poll(1, "s", "2"), CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(403);
    }

    [Test]
    public async Task Poll_ClosedSession_Returns410()
    {
        var manager = StreamTestSupport.NewManager();
        var session = StreamTestSupport.NewSession(manager, "s");
        session.Close();

        var response = await manager.HandleAsync(StreamTestSupport.Poll(1, "s", "1"), CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(410);
    }

    [Test]
    public async Task Poll_EmptyQueue_Returns204()
    {
        var manager = StreamTestSupport.NewManager();

        // 预取消令牌：避免空 poll 停放 20s（AwaitFrames 在取消时返回 alive=true 的空结果）。
        var response = await manager.HandleAsync(StreamTestSupport.Poll(1, "s", "1"), new CancellationToken(canceled: true));

        await Assert.That(response.StatusCode).IsEqualTo(204);
    }

    [Test]
    public async Task Send_IllegalKind_Returns400()
    {
        var manager = StreamTestSupport.NewManager();

        var response = await manager.HandleAsync(StreamTestSupport.Send(1, "s", 1, kind: 9), CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(400);
    }

    [Test]
    public async Task Send_MissingGeneration_Returns410()
    {
        var manager = StreamTestSupport.NewManager();

        var response = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, generation: null, name: StreamTestSupport.StreamName),
            CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(410);
    }

    [Test]
    public async Task Send_BatchWithOpen_Returns400()
    {
        var manager = StreamTestSupport.NewManager();

        var response = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, name: StreamTestSupport.StreamName, batch: true),
            CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(400);
    }

    [Test]
    public async Task Send_ControlFrameWithBody_Returns400()
    {
        var manager = StreamTestSupport.NewManager();

        var response = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, name: StreamTestSupport.StreamName, body: [1, 2, 3]),
            CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(400);
    }

    [Test]
    public async Task Send_OpenWithoutHandler_Returns204AndQueuesErrorFrame()
    {
        var manager = StreamTestSupport.NewManager();

        var response = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, name: "nope"),
            CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(204);

        var poll = await manager.HandleAsync(StreamTestSupport.Poll(1, "s", "1"), CancellationToken.None);
        await Assert.That(poll.StatusCode).IsEqualTo(200);

        var frames = DecodeFrames(poll.Body);
        var error = frames.Single(f => f.ConnId == 1 && f.Kind == StreamFrameKind.Error);
        await Assert.That(Encoding.UTF8.GetString(error.Payload)).IsEqualTo("no handler registered for stream nope");
    }

    [Test]
    public async Task Send_DuplicateConnId_Returns409()
    {
        var manager = StreamTestSupport.NewManager();

        var first = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, name: StreamTestSupport.StreamName), CancellationToken.None);
        var second = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, name: StreamTestSupport.StreamName), CancellationToken.None);

        await Assert.That(first.StatusCode).IsEqualTo(204);
        await Assert.That(second.StatusCode).IsEqualTo(409);
    }

    [Test]
    public async Task Send_UnknownConnection_Returns410()
    {
        var manager = StreamTestSupport.NewManager();
        _ = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, name: StreamTestSupport.StreamName), CancellationToken.None);

        var response = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 99, StreamFrameKind.Data, body: [1]), CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(410);
    }

    [Test]
    public async Task Send_UnknownSession_Returns410()
    {
        var manager = StreamTestSupport.NewManager();

        var response = await manager.HandleAsync(
            StreamTestSupport.Send(1, "nosuch", 1, StreamFrameKind.Data, body: [1]), CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(410);
    }

    [Test]
    public async Task Send_DataForwardsToConnectionInbox()
    {
        var manager = StreamTestSupport.NewManager();
        _ = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, name: StreamTestSupport.StreamName), CancellationToken.None);

        var response = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Data, body: [7, 8, 9]), CancellationToken.None);
        await Assert.That(response.StatusCode).IsEqualTo(204);

        var session = manager.ExistingSession("s")!;
        var frame = await session.Conn(1)!.ReceiveAsync();
        await Assert.That(frame.SequenceEqual(new byte[] { 7, 8, 9 })).IsTrue();
    }

    [Test]
    public async Task Send_BatchInvalidBody_Returns400()
    {
        var manager = StreamTestSupport.NewManager();
        _ = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, name: StreamTestSupport.StreamName), CancellationToken.None);

        var response = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Data, body: [0, 0], batch: true), CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(400);
    }

    [Test]
    public async Task Send_BadChunkHeaders_Returns400()
    {
        var manager = StreamTestSupport.NewManager();
        _ = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, name: StreamTestSupport.StreamName), CancellationToken.None);

        var response = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Data, body: [1], chunkId: "z", chunkIndex: 0, chunkTotal: 0),
            CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(400);
    }

    [Test]
    public async Task Send_ChunkConflict_Returns400()
    {
        var manager = StreamTestSupport.NewManager();
        _ = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, name: StreamTestSupport.StreamName), CancellationToken.None);

        var first = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Data, body: [1], chunkId: "x", chunkIndex: 0, chunkTotal: 2),
            CancellationToken.None);
        var conflict = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Data, body: [1], chunkId: "x", chunkIndex: 0, chunkTotal: 3),
            CancellationToken.None);

        await Assert.That(first.StatusCode).IsEqualTo(204);
        await Assert.That(conflict.StatusCode).IsEqualTo(400);
    }

    [Test]
    public async Task Send_ChunkDuplicate_Returns400()
    {
        var manager = StreamTestSupport.NewManager();
        _ = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, name: StreamTestSupport.StreamName), CancellationToken.None);

        var first = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Data, body: [1], chunkId: "y", chunkIndex: 0, chunkTotal: 2),
            CancellationToken.None);
        var dup = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Data, body: [1], chunkId: "y", chunkIndex: 0, chunkTotal: 2),
            CancellationToken.None);

        await Assert.That(first.StatusCode).IsEqualTo(204);
        await Assert.That(dup.StatusCode).IsEqualTo(400);
    }

    [Test]
    public async Task Send_LastChunkRetriedAfter429_ReturnsSameAssembledFrame()
    {
        var manager = StreamTestSupport.NewManager();
        _ = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Open, name: StreamTestSupport.StreamName), CancellationToken.None);

        var session = manager.ExistingSession("s")!;
        var conn = session.Conn(1)!;

        // 用一批数据帧填满连接收件箱（depth 256）。
        var batch = StreamTestSupport.BuildBatch(Enumerable.Range(0, StreamConstants.InQueueDepth).Select(_ => new byte[] { 0 }).ToList());
        _ = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Data, body: batch, batch: true), CancellationToken.None);

        // 首片落地（partial）。
        var first = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Data, body: [1], chunkId: "c1", chunkIndex: 0, chunkTotal: 2),
            CancellationToken.None);
        await Assert.That(first.StatusCode).IsEqualTo(204);

        // 末片到达即完成整帧，但收件箱已满 → 429。
        var last = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Data, body: [2], chunkId: "c1", chunkIndex: 1, chunkTotal: 2),
            CancellationToken.None);
        await Assert.That(last.StatusCode).IsEqualTo(429);

        // 排空收件箱。
        for (var i = 0; i < StreamConstants.InQueueDepth; i++)
        {
            _ = await conn.ReceiveAsync();
        }

        // 重试末片：得到同一 assembled 帧并成功投递。
        var retry = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s", 1, StreamFrameKind.Data, body: [2], chunkId: "c1", chunkIndex: 1, chunkTotal: 2),
            CancellationToken.None);
        await Assert.That(retry.StatusCode).IsEqualTo(204);

        var assembled = await conn.ReceiveAsync();
        await Assert.That(assembled.SequenceEqual(new byte[] { 1, 2 })).IsTrue();
    }

    [Test]
    public async Task Pipeline_RoutesStreamPathsToStreamManager()
    {
        var app = new Application(new ApplicationOptions());
        app.HandleStream(StreamTestSupport.StreamName, StreamTestSupport.BlockingHandler);

        var pipeline = new WebviewRequestPipeline(
            ipcHandler: (_, _, _, _) => Task.FromResult<object?>(null),
            assetHandler: (_, _, _) => Task.FromResult<byte[]?>(null));

        // HEAD poll：由 Streams 端点返回 405（而非静态资源的 404）。
        var response = await pipeline.HandleAsync(StreamTestSupport.Poll(1, "s", "1", "HEAD"), CancellationToken.None);

        await Assert.That(response.StatusCode).IsEqualTo(405);
        await Assert.That(response.Headers!["Allow"]).IsEqualTo("GET");

        // send 为 POST /wails/stream/send，若未抢先拦截会命中 IsIpcRequest；此处应走 Streams 并返回 400（非法 kind）。
        var send = await pipeline.HandleAsync(StreamTestSupport.Send(1, "s", 1, kind: 9), CancellationToken.None);
        await Assert.That(send.StatusCode).IsEqualTo(400);
    }

    /// <summary>解码 poll 响应体中的帧列表。</summary>
    /// <param name="body">响应体。</param>
    /// <returns>帧列表。</returns>
    private static List<(uint ConnId, byte Kind, byte[] Payload)> DecodeFrames(byte[] body)
    {
        var frames = new List<(uint, byte, byte[])>();
        var count = StreamTestSupport.ReadUInt32BigEndian(body, 5);
        var off = 9;
        for (var i = 0; i < count; i++)
        {
            var connId = StreamTestSupport.ReadUInt32BigEndian(body, off);
            var kind = body[off + 4];
            var len = (int)StreamTestSupport.ReadUInt32BigEndian(body, off + 5);
            var payload = body[(off + 9)..(off + 9 + len)];
            frames.Add((connId, kind, payload));
            off += 9 + len;
        }

        return frames;
    }
}
