using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Channels;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Events;
using Wails.Net.Application.Options;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// Channel 前后端协议测试（RH-IPC-05）。
/// 覆盖：<c>channel:open/message/close</c> 三类上行消息、下行保留事件名投递、
/// call 载荷 channelId → <see cref="ICommandContext.Channel"/> 注入、窗口回收。
/// </summary>
[NotInParallel]
public sealed class ChannelProtocolTests
{
    /// <summary>
    /// 等待条件成立（事件经邮箱异步派发，需轮询）。
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("等待条件超时。");
    }

    /// <summary>
    /// 构造带命令调度器与事件监听桩的 MessageProcessor。
    /// </summary>
    private static (MessageProcessor Processor, EventProcessor Events, IWailsEventListener Listener, CommandRegistry Registry) CreateProcessor()
    {
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var listener = Substitute.For<IWailsEventListener>();
        events.AddWailsEventListener(listener);

        var registry = new CommandRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new CommandDispatcher(registry, services, NullLogger<CommandDispatcher>.Instance);
        var processor = new MessageProcessor(bindings, events, null!, dispatcher, services);
        return (processor, events, listener, registry);
    }

    private static Message CreateMessage(string type, object? payload)
    {
        var json = JsonSerializer.Serialize(payload ?? new object());
        return new Message
        {
            Id = "m-1",
            Type = type,
            Payload = JsonSerializer.Deserialize<JsonElement>(json),
            WindowId = null,
        };
    }

    private static Dictionary<string, object?> ExtractResult(ResponseMessage? response)
    {
        if (response is null)
        {
            throw new InvalidOperationException("期望有响应，实际为 null");
        }

        return response.Result;
    }

    // ---------------------------------------------------------------------
    // channel:open
    // ---------------------------------------------------------------------

    [Test]
    public async Task ChannelOpen_GeneratesChannelId_AndRegisters()
    {
        ChannelManager.Clear();
        var (processor, _, _, _) = CreateProcessor();

        var response = await processor.ProcessAsync(CreateMessage("channel:open", new { }));

        var result = ExtractResult(response);
        var channelId = (string)result["channelId"]!;
        await Assert.That(channelId).IsNotEmpty();
        await Assert.That(ChannelManager.Get(channelId)).IsNotNull();
        ChannelManager.Clear();
    }

    [Test]
    public async Task ChannelOpen_WithExplicitId_IsIdempotent()
    {
        ChannelManager.Clear();
        var (processor, _, _, _) = CreateProcessor();

        var first = await processor.ProcessAsync(CreateMessage("channel:open", new { id = "ch-x" }));
        var second = await processor.ProcessAsync(CreateMessage("channel:open", new { id = "ch-x" }));

        await Assert.That(ExtractResult(first)["channelId"]).IsEqualTo("ch-x");
        await Assert.That(ExtractResult(second)["channelId"]).IsEqualTo("ch-x");
        await Assert.That(ChannelManager.Count).IsEqualTo(1);
        ChannelManager.Clear();
    }

    // ---------------------------------------------------------------------
    // channel:message
    // ---------------------------------------------------------------------

    [Test]
    public async Task ChannelMessage_DeliversToSubscriber()
    {
        ChannelManager.Clear();
        var (processor, _, _, _) = CreateProcessor();

        var open = await processor.ProcessAsync(CreateMessage("channel:open", new { }));
        var channelId = (string)ExtractResult(open)["channelId"]!;
        var channel = (Channel)ChannelManager.Get(channelId)!;

        ChannelMessage? received = null;
        channel.OnMessage += (_, message) => received = message;

        await processor.ProcessAsync(CreateMessage("channel:message", new
        {
            channelId,
            data = new { percent = 50 },
        }));

        await WaitUntilAsync(() => received is not null);
        await Assert.That(received!.Payload).IsNotNull();
        ChannelManager.Clear();
    }

    [Test]
    public async Task ChannelMessage_UnknownChannel_ReturnsReferenceError()
    {
        ChannelManager.Clear();
        var (processor, _, _, _) = CreateProcessor();

        var response = await processor.ProcessAsync(CreateMessage("channel:message", new
        {
            channelId = "no-such-channel",
            data = "x",
        }));

        var result = ExtractResult(response);
        await Assert.That(result["error"]).IsNotNull();
        ChannelManager.Clear();
    }

    // ---------------------------------------------------------------------
    // channel:close 与下行投递
    // ---------------------------------------------------------------------

    [Test]
    public async Task ChannelClose_RemovesChannel_AndSendsClosedDownlink()
    {
        ChannelManager.Clear();
        var (processor, _, listener, _) = CreateProcessor();

        var open = await processor.ProcessAsync(CreateMessage("channel:open", new { id = "ch-close-me", }));
        await Assert.That(ExtractResult(open)["channelId"]).IsEqualTo("ch-close-me");

        var response = await processor.ProcessAsync(CreateMessage("channel:close", new { channelId = "ch-close-me" }));

        await Assert.That(ExtractResult(response)["closed"] is true).IsTrue();
        await Assert.That(ChannelManager.Get("ch-close-me")).IsNull();
        // 关闭信号经保留事件名下发到前端。
        await WaitUntilAsync(() => listener.ReceivedCalls().Any());
        listener.Received(1).NotifyEvent(
            ChannelProtocol.EventName("ch-close-me"),
            Arg.Any<object?>(),
            Arg.Any<uint?>(),
            Arg.Any<uint?>());
        ChannelManager.Clear();
    }

    [Test]
    public async Task ChannelOpen_WithWindowId_TargetsDownlinkToWindow()
    {
        ChannelManager.Clear();
        var bindings = new BindingManager();
        var events = new EventProcessor();
        var listener = Substitute.For<IWailsEventListener>();
        events.AddWailsEventListener(listener);
        var registry = new CommandRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new CommandDispatcher(registry, services, NullLogger<CommandDispatcher>.Instance);
        var processor = new MessageProcessor(bindings, events, null!, dispatcher, services);

        var message = CreateMessage("channel:open", new { id = "ch-win" });
        message.WindowId = 7;
        await processor.ProcessAsync(message);

        // 后端经通道 SendAsync 主动推送 → 下行事件应定向到窗口 7。
        var channel = ChannelManager.Get("ch-win")!;
        await channel.SendAsync("hello");

        await WaitUntilAsync(() => listener.ReceivedCalls().Any());
        listener.Received(1).NotifyEvent(
            ChannelProtocol.EventName("ch-win"),
            Arg.Any<object?>(),
            Arg.Any<uint?>(),
            7u);
        ChannelManager.Clear();
    }

    // ---------------------------------------------------------------------
    // call 载荷 channelId → ICommandContext.Channel 注入
    // ---------------------------------------------------------------------

    [Test]
    public async Task Call_WithChannelId_InjectsChannelIntoCommandContext()
    {
        ChannelManager.Clear();
        var (processor, _, _, registry) = CreateProcessor();

        await processor.ProcessAsync(CreateMessage("channel:open", new { id = "ch-cmd" }));

        IChannel? observed = null;
        registry.MapCommand("test.channelProbe", (ICommandContext ctx) =>
        {
            observed = ctx.Channel;
        });

        var call = CreateMessage("call", new
        {
            name = "test.channelProbe",
            args = Array.Empty<object>(),
            channelId = "ch-cmd",
        });
        await processor.ProcessAsync(call);

        await WaitUntilAsync(() => observed is not null);
        await Assert.That(observed!.Id).IsEqualTo("ch-cmd");
        ChannelManager.Clear();
    }

    [Test]
    public async Task Call_WithoutChannelId_CommandContextChannelIsNull()
    {
        ChannelManager.Clear();
        var (processor, _, _, registry) = CreateProcessor();

        IChannel? observed = null;
        var probed = false;
        registry.MapCommand("test.channelProbeNone", (ICommandContext ctx) =>
        {
            observed = ctx.Channel;
            probed = true;
        });

        await processor.ProcessAsync(CreateMessage("call", new
        {
            name = "test.channelProbeNone",
            args = Array.Empty<object>(),
        }));

        await WaitUntilAsync(() => probed);
        await Assert.That(observed).IsNull();
        ChannelManager.Clear();
    }

    // ---------------------------------------------------------------------
    // 窗口回收
    // ---------------------------------------------------------------------

    [Test]
    public async Task RemoveByWindow_RemovesOnlyOwnedChannels()
    {
        ChannelManager.Clear();
        ChannelManager.Create("owned-1", windowId: 3);
        ChannelManager.Create("owned-2", windowId: 3);
        ChannelManager.Create("other-window", windowId: 4);
        ChannelManager.Create("no-window");

        var removed = ChannelManager.RemoveByWindow(3);

        await Assert.That(removed).IsEqualTo(2);
        await Assert.That(ChannelManager.Get("owned-1")).IsNull();
        await Assert.That(ChannelManager.Get("owned-2")).IsNull();
        await Assert.That(ChannelManager.Get("other-window")).IsNotNull();
        await Assert.That(ChannelManager.Get("no-window")).IsNotNull();
        ChannelManager.Clear();
    }
}
