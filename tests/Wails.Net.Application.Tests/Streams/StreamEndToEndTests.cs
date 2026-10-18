using System.Net.WebSockets;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Streams;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Tests.Streams;

/// <summary>
/// Streams P0-1 端到端测试：桌面 held-poll/POST 全链路（open → poll ack → send data →
/// handler 回送 → close）与 server 模式真实 WebSocket 回环，以及全局 session 上界。
/// 对应 Wails v3 Go 版本 <c>stream.go</c> / <c>stream_transport.go</c> / <c>stream_server.go</c>。
/// </summary>
[NotInParallel]
public sealed class StreamEndToEndTests
{
    /// <summary>
    /// 桌面模式全链路：open 帧 → poll 收到 open ack → POST 数据帧 → handler 回送并返回
    /// → poll 收到数据帧 + close 帧。
    /// </summary>
    [Test]
    public async Task Desktop_OpenSendEchoClose_RoundTrip()
    {
        using var manager = new StreamManager();
        manager.HandleStream("echo", async conn =>
        {
            var frame = await conn.ReceiveAsync();
            await conn.SendAsync(frame);
            // 返回即关闭连接（排队 close 帧）。
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // 1) open：POST /wails/stream/send（kind=Open，name=echo）
        var open = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s1", 1, StreamFrameKind.Open, name: "echo"), cts.Token);
        await Assert.That(open.StatusCode).IsEqualTo(204);

        // 2) poll：应立即拿到 open ack
        var ack = await manager.HandleAsync(StreamTestSupport.Poll(1, "s1", "1"), cts.Token);
        await Assert.That(ack.StatusCode).IsEqualTo(200);
        var ackFrames = DecodePoll(ack.Body);
        await Assert.That(ackFrames.Count).IsEqualTo(1);
        await Assert.That(ackFrames[0].Kind).IsEqualTo(StreamFrameKind.Open);
        await Assert.That(ackFrames[0].ConnId).IsEqualTo(1u);

        // 3) 发送数据帧
        var payload = new byte[] { 10, 20, 30, 40 };
        var send = await manager.HandleAsync(
            StreamTestSupport.Send(1, "s1", 1, StreamFrameKind.Data, body: payload), cts.Token);
        await Assert.That(send.StatusCode).IsEqualTo(204);

        // 4) 等待 handler 完成（回送 + 返回关闭）
        var session = manager.ExistingSession("s1");
        await Assert.That(session).IsNotNull();
        await Assert.That(await StreamTestSupport.WaitUntilAsync(
            () => session!.ConnCount() == 0, TimeSpan.FromSeconds(5))).IsTrue();

        // 5) poll：应同时拿到数据帧与 close 帧
        var final = await manager.HandleAsync(StreamTestSupport.Poll(1, "s1", "1"), cts.Token);
        await Assert.That(final.StatusCode).IsEqualTo(200);
        var finalFrames = DecodePoll(final.Body);
        var dataFrames = finalFrames.Where(f => f.Kind == StreamFrameKind.Data).ToList();
        await Assert.That(dataFrames.Count).IsEqualTo(1);
        await Assert.That(dataFrames[0].Payload.SequenceEqual(payload)).IsTrue();
        await Assert.That(finalFrames.Any(f => f.Kind == StreamFrameKind.Close)).IsTrue();
    }

    /// <summary>
    /// 全局 session 上界（1024）：超出后 <see cref="StreamManager.SessionWithAdmission"/> 返回容量耗尽。
    /// </summary>
    [Test]
    public async Task GlobalSessionCapacity_RefusesBeyondGlobalLimit()
    {
        using var manager = StreamTestSupport.NewManager();

        var created = 0;
        for (var i = 0; i < 1024; i++)
        {
            // 每窗口上限 16 → 用 64 个窗口分摊 1024 个 session。
            var windowId = (uint)(i / 16) + 1;
            if (manager.SessionWithAdmission($"g{i}", windowId, 1, maySupersede: false).Session is not null)
            {
                created++;
            }
        }

        await Assert.That(created).IsEqualTo(1024);

        var overflow = manager.SessionWithAdmission("overflow", 100, 1, maySupersede: false);
        await Assert.That(overflow.Session).IsNull();
        await Assert.That(overflow.AtCapacity).IsTrue();
    }

    /// <summary>
    /// server 模式：<c>/wails/stream/ws?name=echo</c> 真实 WebSocket 回环，
    /// handler 返回后服务端以 NormalClosure 关闭 socket。
    /// </summary>
    [Test]
    public async Task ServerMode_WebSocketEcho_RoundTrip()
    {
        var options = new ApplicationOptions { Name = "StreamServerTest" };
        var app = new Application(options);
        app.SetPlatformApp(new ServerPlatformApp(options));
        app.HandleStream("echo", async conn =>
        {
            var frame = await conn.ReceiveAsync();
            await conn.SendAsync(frame);
        });

        var processor = new MessageProcessor(new BindingManager(), new EventProcessor());
        var transport = new WebSocketTransport(processor, new WebSocketBroadcaster());
        await transport.StartAsync();

        try
        {
            using var client = new ClientWebSocket();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await client.ConnectAsync(
                new Uri($"ws://localhost:{transport.Port}{WebSocketTransport.StreamWebSocketPath}?name=echo"), cts.Token);

            var payload = "ping"u8.ToArray();
            await client.SendAsync(payload, WebSocketMessageType.Binary, endOfMessage: true, cts.Token);

            var buffer = new byte[1024];
            var echo = await client.ReceiveAsync(buffer.AsMemory(), cts.Token);
            await Assert.That(echo.MessageType).IsEqualTo(WebSocketMessageType.Binary);
            await Assert.That(buffer.AsSpan(0, echo.Count).SequenceEqual(payload)).IsTrue();

            // handler 返回 → 服务端排队 close 控制帧 → 客户端收到 Close。
            var close = await client.ReceiveAsync(buffer.AsMemory(), cts.Token);
            await Assert.That(close.MessageType).IsEqualTo(WebSocketMessageType.Close);
        }
        finally
        {
            await transport.StopAsync();
        }
    }

    /// <summary>
    /// 解码 poll 响应体：magic(4) + flags(1) + count(4) + count×(connID(4)+kind(1)+len(4)+payload)。
    /// </summary>
    /// <param name="body">poll 响应体。</param>
    /// <returns>解码出的帧列表。</returns>
    private static List<(uint ConnId, byte Kind, byte[] Payload)> DecodePoll(byte[] body)
    {
        var frames = new List<(uint, byte, byte[])>();
        var count = StreamTestSupport.ReadUInt32BigEndian(body, 5);
        var off = 9;
        for (uint i = 0; i < count; i++)
        {
            var connId = StreamTestSupport.ReadUInt32BigEndian(body, off);
            off += 4;
            var kind = body[off];
            off += 1;
            var len = (int)StreamTestSupport.ReadUInt32BigEndian(body, off);
            off += 4;
            var payload = body[off..(off + len)];
            off += len;
            frames.Add((connId, kind, payload));
        }

        return frames;
    }
}
