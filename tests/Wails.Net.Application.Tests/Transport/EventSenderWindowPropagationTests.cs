using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Events;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// 事件来源窗口 ID（senderWindowId）端到端传播测试（P1-2）。
/// 验证 <see cref="EventProcessor.Emit(string, object?, uint?)"/> 中的 senderWindowID
/// 被正确传递到 <see cref="IWailsEventListener.NotifyEvent"/> 的第三个参数，
/// 对应 Wails v3 Go 版本 CustomEvent.Sender 字段端到端语义。
/// </summary>
[NotInParallel]
public sealed class EventSenderWindowPropagationTests
{
    // ---------------------------------------------------------------------
    // EventProcessor → IWailsEventListener 传播
    // ---------------------------------------------------------------------

    [Test]
    public async Task Emit_WithSenderWindowID_PassesToListener()
    {
        var processor = new EventProcessor();
        var listener = Substitute.For<IWailsEventListener>();
        processor.AddWailsEventListener(listener);

        processor.Emit("test.event", new { payload = 1 }, senderWindowID: 42u);

        // 验证第三个参数 senderWindowId 被原样传递
        listener.Received(1).NotifyEvent(
            "test.event",
            Arg.Any<object?>(),
            42u);
    }

    [Test]
    public async Task Emit_WithNullSenderWindowID_PassesNullToListener()
    {
        var processor = new EventProcessor();
        var listener = Substitute.For<IWailsEventListener>();
        processor.AddWailsEventListener(listener);

        processor.Emit("app.event", null, senderWindowID: null);

        // 验证第三个参数为 null（应用级事件）
        listener.Received(1).NotifyEvent(
            "app.event",
            null,
            Arg.Is<uint?>(v => !v.HasValue));
    }

    [Test]
    public async Task Emit_WithoutSenderWindowID_DefaultsToNull()
    {
        var processor = new EventProcessor();
        var listener = Substitute.For<IWailsEventListener>();
        processor.AddWailsEventListener(listener);

        // 不传 senderWindowID 时，默认应为 null
        processor.Emit("default.event");

        listener.Received(1).NotifyEvent(
            "default.event",
            null,
            Arg.Is<uint?>(v => !v.HasValue));
    }

    [Test]
    public async Task Emit_PropagatesSenderWindowID_ToMultipleListeners()
    {
        var processor = new EventProcessor();
        var listener1 = Substitute.For<IWailsEventListener>();
        var listener2 = Substitute.For<IWailsEventListener>();
        processor.AddWailsEventListener(listener1);
        processor.AddWailsEventListener(listener2);

        processor.Emit("multi.listener", "data", senderWindowID: 7u);

        // 两个监听器都应收到相同的 senderWindowId
        listener1.Received(1).NotifyEvent("multi.listener", Arg.Any<object?>(), 7u);
        listener2.Received(1).NotifyEvent("multi.listener", Arg.Any<object?>(), 7u);
    }

    // ---------------------------------------------------------------------
    // 本地订阅者也收到 SenderWindowID（CustomEvent 字段）
    // ---------------------------------------------------------------------

    [Test]
    public async Task Emit_LocalSubscriber_ReceivesCustomEventWithSenderWindowID()
    {
        var processor = new EventProcessor();
        uint? receivedSenderId = 99u;

        processor.On("local.event", evt => receivedSenderId = evt.SenderWindowID);
        processor.Emit("local.event", null, senderWindowID: 123u);

        await Assert.That(receivedSenderId).IsEqualTo(123u);
    }

    // ---------------------------------------------------------------------
    // 异常隔离：单个监听器抛异常不影响其他监听器收到 senderWindowId
    // ---------------------------------------------------------------------

    [Test]
    public async Task Emit_OneListenerThrows_OtherListenerStillReceivesSenderWindowID()
    {
        var processor = new EventProcessor();
        var throwingListener = Substitute.For<IWailsEventListener>();
        throwingListener
            .When(x => x.NotifyEvent(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<uint?>()))
            .Do(_ => throw new InvalidOperationException("simulated failure"));

        var healthyListener = Substitute.For<IWailsEventListener>();
        processor.AddWailsEventListener(throwingListener);
        processor.AddWailsEventListener(healthyListener);

        processor.Emit("resilient.event", null, senderWindowID: 5u);

        healthyListener.Received(1).NotifyEvent("resilient.event", null, 5u);
    }
}
