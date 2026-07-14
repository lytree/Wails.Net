using System.Collections.Concurrent;
using System.Threading;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Channels;

namespace Wails.Net.Application.Tests.Channels;

/// <summary>
/// Channel API 单元测试。
/// 覆盖通道创建、获取、移除、双向消息收发、关闭后行为、并发场景。
/// 对应 Tauri v2 Channel&lt;T&gt; 长生命周期双向流。
/// </summary>
[NotInParallel]
public sealed class ChannelTests
{
    /// <summary>
    /// 每个测试前清理 ChannelManager 静态状态，避免相互影响。
    /// </summary>
    [Test]
    public async Task Setup_ResetChannelManager()
    {
        ChannelManager.Clear();
        await Assert.That(ChannelManager.Count).IsEqualTo(0);
    }

    // === 创建与注册 ===

    [Test]
    public async Task Create_WithExplicitId_RegistersAndReturnsChannel()
    {
        ChannelManager.Clear();
        var channel = ChannelManager.Create("test-channel-1");

        await Assert.That(channel.Id).IsEqualTo("test-channel-1");
        await Assert.That(channel.IsClosed).IsFalse();
        await Assert.That(ChannelManager.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Create_WithNullId_GeneratesGuidId()
    {
        ChannelManager.Clear();
        var channel = ChannelManager.Create();

        await Assert.That(channel.Id).IsNotNull();
        await Assert.That(channel.Id.Length).IsEqualTo(32); // Guid N 格式：32 字符
        await Assert.That(ChannelManager.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Create_DuplicateId_ThrowsArgumentException()
    {
        ChannelManager.Clear();
        ChannelManager.Create("dup-channel");

        await Assert.That(() => ChannelManager.Create("dup-channel"))
            .ThrowsExactly<ArgumentException>();
    }

    // === 获取与移除 ===

    [Test]
    public async Task Get_ExistingChannel_ReturnsChannel()
    {
        ChannelManager.Clear();
        ChannelManager.Create("get-test");

        var channel = ChannelManager.Get("get-test");
        await Assert.That(channel).IsNotNull();
        await Assert.That(channel!.Id).IsEqualTo("get-test");
    }

    [Test]
    public async Task Get_NonExistentChannel_ReturnsNull()
    {
        ChannelManager.Clear();
        var channel = ChannelManager.Get("does-not-exist");
        await Assert.That(channel).IsNull();
    }

    [Test]
    public async Task Remove_ExistingChannel_RemovesAndReturnsTrue()
    {
        ChannelManager.Clear();
        ChannelManager.Create("remove-test");

        var result = ChannelManager.Remove("remove-test");
        await Assert.That(result).IsTrue();
        await Assert.That(ChannelManager.Count).IsEqualTo(0);
        await Assert.That(ChannelManager.Get("remove-test")).IsNull();
    }

    [Test]
    public async Task Remove_NonExistentChannel_ReturnsFalse()
    {
        ChannelManager.Clear();
        var result = ChannelManager.Remove("never-existed");
        await Assert.That(result).IsFalse();
    }

    // === 双向消息收发 ===

    [Test]
    public async Task SendAsync_WithCustomSender_InvokesSender()
    {
        ChannelManager.Clear();
        var sentPayloads = new List<(string ChannelId, object? Payload)>();

        var channel = ChannelManager.Create("send-test", (channelId, payload, _) =>
        {
            sentPayloads.Add((channelId, payload));
            return Task.CompletedTask;
        });

        await channel.SendAsync("hello");
        await channel.SendAsync(42);
        await channel.SendAsync(null);

        await Assert.That(sentPayloads.Count).IsEqualTo(3);
        await Assert.That(sentPayloads[0].ChannelId).IsEqualTo("send-test");
        await Assert.That(sentPayloads[0].Payload).IsEqualTo("hello");
        await Assert.That(sentPayloads[1].Payload).IsEqualTo(42);
        await Assert.That(sentPayloads[2].Payload).IsNull();
    }

    [Test]
    public async Task SendAsync_WithDefaultSender_DoesNotThrow()
    {
        ChannelManager.Clear();
        var channel = ChannelManager.Create("default-sender");

        // 默认 sender 为 no-op，发送不应抛异常
        await channel.SendAsync("payload");
        await Assert.That(channel.IsClosed).IsFalse();
    }

    // === 关闭行为 ===

    [Test]
    public async Task CloseAsync_MarksChannelAsClosed()
    {
        ChannelManager.Clear();
        var channel = ChannelManager.Create("close-test");

        await Assert.That(channel.IsClosed).IsFalse();
        await channel.CloseAsync();
        await Assert.That(channel.IsClosed).IsTrue();
    }

    [Test]
    public async Task CloseAsync_Idempotent_MultipleCallsSafe()
    {
        ChannelManager.Clear();
        var channel = ChannelManager.Create("idempotent-close");

        await channel.CloseAsync();
        await channel.CloseAsync(); // 不应抛异常
        await channel.CloseAsync();
        await Assert.That(channel.IsClosed).IsTrue();
    }

    [Test]
    public async Task SendAsync_AfterClose_ThrowsObjectDisposedException()
    {
        ChannelManager.Clear();
        var channel = ChannelManager.Create("send-after-close");
        await channel.CloseAsync();

        await Assert.That(() => channel.SendAsync("payload"))
            .ThrowsExactly<ObjectDisposedException>();
    }

    // === 并发场景 ===

    [Test]
    public async Task Create_ConcurrentDifferentIds_AllRegisteredSuccessfully()
    {
        ChannelManager.Clear();
        const int parallelCount = 50;
        var results = new ConcurrentBag<IChannel>();

        await Parallel.ForAsync(0, parallelCount, async (i, _) =>
        {
            var channel = ChannelManager.Create($"concurrent-{i}");
            results.Add(channel);
            await Task.CompletedTask;
        });

        await Assert.That(results.Count).IsEqualTo(parallelCount);
        await Assert.That(ChannelManager.Count).IsEqualTo(parallelCount);
    }

    [Test]
    public async Task DispatchMessage_RoutesToCorrectChannel()
    {
        ChannelManager.Clear();
        ChannelMessage? receivedMessage = null;
        string? receivedEventId = null;

        var channel = ChannelManager.Create("dispatch-test");
        channel.OnMessage += (eventId, message) =>
        {
            receivedEventId = eventId;
            receivedMessage = message;
        };

        var sentMessage = new ChannelMessage { Event = "test-event", Payload = "test-data" };
        var dispatched = ChannelManager.DispatchMessage("dispatch-test", "evt-1", sentMessage);

        await Assert.That(dispatched).IsTrue();
        await Assert.That(receivedEventId).IsEqualTo("evt-1");
        await Assert.That(receivedMessage).IsNotNull();
        await Assert.That(receivedMessage!.Payload).IsEqualTo("test-data");
    }

    [Test]
    public async Task DispatchMessage_NonExistentChannel_ReturnsFalse()
    {
        ChannelManager.Clear();
        var dispatched = ChannelManager.DispatchMessage(
            "no-such-channel",
            null,
            new ChannelMessage());

        await Assert.That(dispatched).IsFalse();
    }
}
