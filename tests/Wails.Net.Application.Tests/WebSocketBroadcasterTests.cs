using System.Net.WebSockets;
using System.Text.Json;
using TUnit.Core;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Tests;

/// <summary>
/// WebSocketBroadcaster 的单元测试（TUnit）。
/// 测试客户端管理、事件广播、停止操作。
/// </summary>
[NotInParallel]
public sealed class WebSocketBroadcasterTests
{
    /// <summary>
    /// 用于测试的 WebSocket 模拟类。
    /// </summary>
    private sealed class TestWebSocket : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        private readonly List<byte[]> _receivedMessages = new();

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string SubProtocol => string.Empty;
        public IReadOnlyList<byte[]> ReceivedMessages => _receivedMessages;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _state = WebSocketState.Aborted;
        }

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            var data = new byte[buffer.Count];
            Array.Copy(buffer.Array!, buffer.Offset, data, 0, buffer.Count);
            _receivedMessages.Add(data);
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task AddClient_IncrementsClientCount()
    {
        var broadcaster = new WebSocketBroadcaster();

        broadcaster.AddClient(new TestWebSocket());

        await Assert.That(broadcaster.ClientCount).IsEqualTo(1);
    }

    [Test]
    public async Task AddClient_ReturnsUniqueId()
    {
        var broadcaster = new WebSocketBroadcaster();

        var id1 = broadcaster.AddClient(new TestWebSocket());
        var id2 = broadcaster.AddClient(new TestWebSocket());

        await Assert.That(id1).IsNotEqualTo(id2);
    }

    [Test]
    public async Task RemoveClient_DecrementsClientCount()
    {
        var broadcaster = new WebSocketBroadcaster();
        var id = broadcaster.AddClient(new TestWebSocket());

        broadcaster.RemoveClient(id);

        await Assert.That(broadcaster.ClientCount).IsEqualTo(0);
    }

    [Test]
    public async Task RemoveClient_NonExistentId_DoesNotThrow()
    {
        var broadcaster = new WebSocketBroadcaster();

        broadcaster.RemoveClient("nonexistent");

        await Assert.That(broadcaster.ClientCount).IsEqualTo(0);
    }

    [Test]
    public async Task BroadcastEventAsync_NoClients_DoesNotThrow()
    {
        var broadcaster = new WebSocketBroadcaster();

        await broadcaster.BroadcastEventAsync("test-event", "data");
    }

    [Test]
    public async Task BroadcastEventAsync_DeliversMessageToClient()
    {
        var broadcaster = new WebSocketBroadcaster();
        var client = new TestWebSocket();
        broadcaster.AddClient(client);

        await broadcaster.BroadcastEventAsync("test-event", "hello");

        await Assert.That(client.ReceivedMessages.Count).IsEqualTo(1);
        var json = System.Text.Encoding.UTF8.GetString(client.ReceivedMessages[0]);
        var message = JsonSerializer.Deserialize<JsonElement>(json);
        await Assert.That(message.GetProperty("type").GetString()).IsEqualTo("event");
        await Assert.That(message.GetProperty("name").GetString()).IsEqualTo("test-event");
        await Assert.That(message.GetProperty("data").GetString()).IsEqualTo("hello");
    }

    [Test]
    public async Task BroadcastEventAsync_DeliversToMultipleClients()
    {
        var broadcaster = new WebSocketBroadcaster();
        var client1 = new TestWebSocket();
        var client2 = new TestWebSocket();
        broadcaster.AddClient(client1);
        broadcaster.AddClient(client2);

        await broadcaster.BroadcastEventAsync("event", null);

        await Assert.That(client1.ReceivedMessages.Count).IsEqualTo(1);
        await Assert.That(client2.ReceivedMessages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task BroadcastEventAsync_NullData_SerializesAsNull()
    {
        var broadcaster = new WebSocketBroadcaster();
        var client = new TestWebSocket();
        broadcaster.AddClient(client);

        await broadcaster.BroadcastEventAsync("event", null);

        var json = System.Text.Encoding.UTF8.GetString(client.ReceivedMessages[0]);
        var message = JsonSerializer.Deserialize<JsonElement>(json);
        await Assert.That(message.GetProperty("data").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    public async Task StopAsync_ClosesAllClientConnections()
    {
        var broadcaster = new WebSocketBroadcaster();
        var client = new TestWebSocket();
        broadcaster.AddClient(client);

        await broadcaster.StopAsync();

        await Assert.That(client.State).IsEqualTo(WebSocketState.Closed);
        await Assert.That(broadcaster.ClientCount).IsEqualTo(0);
    }

    [Test]
    public async Task StopAsync_CalledTwice_DoesNotThrow()
    {
        var broadcaster = new WebSocketBroadcaster();

        await broadcaster.StopAsync();
        await broadcaster.StopAsync();
    }

    [Test]
    public async Task BroadcastEvent_SyncMethod_DoesNotBlock()
    {
        var broadcaster = new WebSocketBroadcaster();
        broadcaster.AddClient(new TestWebSocket());

        // 同步广播方法不应阻塞
        broadcaster.BroadcastEvent("test", "data");

        // 给后台任务一点时间完成
        await Task.Delay(50);
        await Assert.That(broadcaster.ClientCount).IsEqualTo(1);
    }

    [Test]
    public async Task ClientCount_ReflectsCurrentConnections()
    {
        var broadcaster = new WebSocketBroadcaster();

        await Assert.That(broadcaster.ClientCount).IsEqualTo(0);

        var id1 = broadcaster.AddClient(new TestWebSocket());
        await Assert.That(broadcaster.ClientCount).IsEqualTo(1);

        broadcaster.AddClient(new TestWebSocket());
        await Assert.That(broadcaster.ClientCount).IsEqualTo(2);

        broadcaster.RemoveClient(id1);
        await Assert.That(broadcaster.ClientCount).IsEqualTo(1);
    }
}
