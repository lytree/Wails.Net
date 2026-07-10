using TUnit.Core;
using Wails.Net.Application.Events;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 事件处理器的单元测试（TUnit）。
/// 测试事件订阅、发布、取消、钩子等。
/// </summary>
[NotInParallel]
public sealed class EventProcessorTests
{
    [Test]
    public async Task On_SubscribesToEvent()
    {
        var processor = new EventProcessor();
        var received = false;

        processor.On("test-event", _ => received = true);
        processor.Emit("test-event");

        await Assert.That(received).IsTrue();
    }

    [Test]
    public async Task Emit_DeliversDataToListener()
    {
        var processor = new EventProcessor();
        object? receivedData = null;

        processor.On("data-event", evt => receivedData = evt.Data);
        processor.Emit("data-event", "hello");

        await Assert.That(receivedData).IsEqualTo("hello");
    }

    [Test]
    public async Task Emit_DeliversToMultipleListeners()
    {
        var processor = new EventProcessor();
        var count = 0;

        processor.On("multi-event", _ => count++);
        processor.On("multi-event", _ => count++);
        processor.Emit("multi-event");

        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task Once_AutoUnsubscribesAfterFirstCall()
    {
        var processor = new EventProcessor();
        var count = 0;

        processor.Once("once-event", _ => count++);
        processor.Emit("once-event");
        processor.Emit("once-event");

        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task OnMultiple_UnsubscribesAfterMaxCalls()
    {
        var processor = new EventProcessor();
        var count = 0;

        processor.OnMultiple("limited-event", _ => count++, 3);
        processor.Emit("limited-event");
        processor.Emit("limited-event");
        processor.Emit("limited-event");
        processor.Emit("limited-event");

        await Assert.That(count).IsEqualTo(3);
    }

    [Test]
    public async Task Off_ByListenerId_Unsubscribes()
    {
        var processor = new EventProcessor();
        var count = 0;

        var id = processor.On("off-event", _ => count++);
        processor.Emit("off-event");
        processor.Off(id);
        processor.Emit("off-event");

        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task Off_ByEventName_UnsubscribesAll()
    {
        var processor = new EventProcessor();
        var count = 0;

        processor.On("remove-all", _ => count++);
        processor.On("remove-all", _ => count++);
        processor.Off("remove-all");
        processor.Emit("remove-all");

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task Emit_WithSenderWindowID_PassesWindowID()
    {
        var processor = new EventProcessor();
        uint? receivedWindowID = null;

        processor.On("window-event", evt => receivedWindowID = evt.SenderWindowID);
        processor.Emit("window-event", null, 42u);

        await Assert.That(receivedWindowID).IsEqualTo(42u);
    }

    [Test]
    public async Task RegisterHook_CanCancelEvent()
    {
        var processor = new EventProcessor();
        var received = false;

        processor.RegisterHook(evt =>
        {
            if (evt.Name == "cancelled-event")
            {
                evt.Cancel();
                return false;
            }
            return true;
        });

        processor.On("cancelled-event", _ => received = true);
        processor.Emit("cancelled-event");

        await Assert.That(received).IsFalse();
    }

    [Test]
    public async Task RegisterHook_LetsUnrelatedEventsThrough()
    {
        var processor = new EventProcessor();
        var received = false;

        processor.RegisterHook(evt => evt.Name != "blocked");

        processor.On("allowed", _ => received = true);
        processor.Emit("allowed");

        await Assert.That(received).IsTrue();
    }

    [Test]
    public async Task CustomEvent_Cancel_StopsPropagation()
    {
        var processor = new EventProcessor();
        var firstCalled = false;
        var secondCalled = false;

        processor.On("propagation", evt =>
        {
            firstCalled = true;
            evt.Cancel();
        });
        processor.On("propagation", _ => secondCalled = true);
        processor.Emit("propagation");

        await Assert.That(firstCalled).IsTrue();
        await Assert.That(secondCalled).IsFalse();
    }

    [Test]
    public async Task ListenerCount_ReturnsCorrectCount()
    {
        var processor = new EventProcessor();

        processor.On("count-event", _ => { });
        processor.On("count-event", _ => { });

        await Assert.That(processor.ListenerCount("count-event")).IsEqualTo(2);
        await Assert.That(processor.ListenerCount("nonexistent")).IsEqualTo(0);
    }

    [Test]
    public async Task Clear_RemovesAllListeners()
    {
        var processor = new EventProcessor();
        var count = 0;

        processor.On("clear-event", _ => count++);
        processor.Clear();
        processor.Emit("clear-event");

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task TypedEvent_OnAndEmits_TypeSafeData()
    {
        var processor = new EventProcessor();
        var typedEvent = new TypedEvent<int>("typed-event", processor);
        int? receivedValue = null;

        typedEvent.On(value => receivedValue = value);
        typedEvent.Emit(42);

        await Assert.That(receivedValue).IsEqualTo(42);
    }

    [Test]
    public async Task TypedEvent_Once_AutoUnsubscribes()
    {
        var processor = new EventProcessor();
        var typedEvent = new TypedEvent<string>("once-typed", processor);
        var count = 0;

        typedEvent.Once(_ => count++);
        typedEvent.Emit("a");
        typedEvent.Emit("b");

        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async Task TypedEvent_Off_RemovesAllListeners()
    {
        var processor = new EventProcessor();
        var typedEvent = new TypedEvent<int>("off-typed", processor);
        var count = 0;

        typedEvent.On(_ => count++);
        typedEvent.Off();
        typedEvent.Emit(1);

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async Task EventHook_ForEvent_OnlyAffectsTargetEvent()
    {
        var processor = new EventProcessor();
        var blockedReceived = false;
        var allowedReceived = false;

        EventHook.ForEvent(processor, "blocked", _ => false);

        processor.On("blocked", _ => blockedReceived = true);
        processor.On("allowed", _ => allowedReceived = true);

        processor.Emit("blocked");
        processor.Emit("allowed");

        await Assert.That(blockedReceived).IsFalse();
        await Assert.That(allowedReceived).IsTrue();
    }

    [Test]
    public async Task Emit_NoListeners_DoesNotThrow()
    {
        var processor = new EventProcessor();

        await Assert.That(() => processor.Emit("no-listeners")).ThrowsNothing();
    }

    [Test]
    public async Task CustomEvent_ToJson_ReturnsCorrectDictionary()
    {
        var evt = new CustomEvent("test", "data", 10u);
        var json = evt.ToJson();

        await Assert.That(json["name"]?.ToString()).IsEqualTo("test");
        await Assert.That(json["data"]?.ToString()).IsEqualTo("data");
        await Assert.That(json["senderWindowId"]).IsEqualTo(10u);
    }

    [Test]
    public async Task CustomEvent_IsCancelled_DefaultFalse()
    {
        var evt = new CustomEvent("test");

        await Assert.That(evt.IsCancelled).IsFalse();
    }

    [Test]
    public async Task CustomEvent_Cancel_SetsIsCancelledTrue()
    {
        var evt = new CustomEvent("test");
        evt.Cancel();

        await Assert.That(evt.IsCancelled).IsTrue();
    }
}
