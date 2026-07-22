using TUnit.Core;
using Wails.Net.Application.Events;
using Wails.Net.Events;

namespace Wails.Net.Application.Tests;

/// <summary>
/// EventProcessor 应用事件 API 的单元测试（TUnit）。
/// 对应项 3：OnApplicationEvent/RegisterApplicationEventHook/EmitApplicationEvent/Reset。
/// </summary>
[NotInParallel]
public sealed class EventProcessorApplicationEventTests
{
    /// <summary>
    /// OnApplicationEvent 注册的监听器在 EmitApplicationEvent 时被调用。
    /// </summary>
    [Test]
    public async Task OnApplicationEvent_WhenEmitted_InvokesListener()
    {
        // 安排
        var processor = new EventProcessor();
        ApplicationEvent? received = null;
        processor.OnApplicationEvent(ApplicationEventType.ThemeChanged, e => received = e);

        // 操作
        processor.EmitApplicationEvent(ApplicationEventType.ThemeChanged, "dark");

        // 断言
        await Assert.That(received).IsNotNull();
        await Assert.That(received!.Id).IsEqualTo((uint)ApplicationEventType.ThemeChanged);
        await Assert.That(received.Data).IsEqualTo("dark");
    }

    /// <summary>
    /// OnApplicationEvent 返回的取消订阅回调移除监听器后，Emit 不再触发该监听器。
    /// </summary>
    [Test]
    public async Task OnApplicationEvent_Unsubscribe_RemovesListener()
    {
        // 安排
        var processor = new EventProcessor();
        var callCount = 0;
        var unsubscribe = processor.OnApplicationEvent(ApplicationEventType.Resume, _ => callCount++);

        // 操作
        processor.EmitApplicationEvent(ApplicationEventType.Resume);
        unsubscribe();
        processor.EmitApplicationEvent(ApplicationEventType.Resume);

        // 断言
        await Assert.That(callCount).IsEqualTo(1);
        await Assert.That(processor.ApplicationEventListenerCount(ApplicationEventType.Resume)).IsEqualTo(0);
    }

    /// <summary>
    /// 同一事件类型可注册多个监听器，Emit 按注册顺序触发全部。
    /// </summary>
    [Test]
    public async Task OnApplicationEvent_MultipleListeners_AllInvoked()
    {
        // 安排
        var processor = new EventProcessor();
        var order = new List<int>();
        processor.OnApplicationEvent(ApplicationEventType.Suspend, _ => order.Add(1));
        processor.OnApplicationEvent(ApplicationEventType.Suspend, _ => order.Add(2));
        processor.OnApplicationEvent(ApplicationEventType.Suspend, _ => order.Add(3));

        // 操作
        processor.EmitApplicationEvent(ApplicationEventType.Suspend);

        // 断言
        await Assert.That(order.Count).IsEqualTo(3);
        await Assert.That(order[0]).IsEqualTo(1);
        await Assert.That(order[1]).IsEqualTo(2);
        await Assert.That(order[2]).IsEqualTo(3);
    }

    /// <summary>
    /// RegisterApplicationEventHook 注册的钩子在监听器之前执行。
    /// </summary>
    [Test]
    public async Task RegisterApplicationEventHook_RunsBeforeListeners()
    {
        // 安排
        var processor = new EventProcessor();
        var order = new List<string>();
        processor.OnApplicationEvent(ApplicationEventType.WindowFocus, _ => order.Add("listener"));
        processor.RegisterApplicationEventHook(ApplicationEventType.WindowFocus, _ => order.Add("hook"));

        // 操作
        processor.EmitApplicationEvent(ApplicationEventType.WindowFocus);

        // 断言
        await Assert.That(order.Count).IsEqualTo(2);
        await Assert.That(order[0]).IsEqualTo("hook");
        await Assert.That(order[1]).IsEqualTo("listener");
    }

    /// <summary>
    /// 钩子调用 Cancel 后，后续钩子不再执行，监听器也不再执行。
    /// </summary>
    [Test]
    public async Task Hook_Cancel_PreventsListenersAndLaterHooks()
    {
        // 安排
        var processor = new EventProcessor();
        var calls = new List<string>();
        processor.RegisterApplicationEventHook(ApplicationEventType.WindowClosing, e =>
        {
            calls.Add("hook1");
            e.Cancel();
        });
        processor.RegisterApplicationEventHook(ApplicationEventType.WindowClosing, _ => calls.Add("hook2"));
        processor.OnApplicationEvent(ApplicationEventType.WindowClosing, _ => calls.Add("listener"));

        // 操作
        var cancelled = processor.EmitApplicationEvent(ApplicationEventType.WindowClosing);

        // 断言
        await Assert.That(cancelled).IsTrue();
        await Assert.That(calls.Count).IsEqualTo(1);
        await Assert.That(calls[0]).IsEqualTo("hook1");
    }

    /// <summary>
    /// EmitApplicationEvent 返回 false 表示事件未被取消。
    /// </summary>
    [Test]
    public async Task EmitApplicationEvent_WithoutCancel_ReturnsFalse()
    {
        // 安排
        var processor = new EventProcessor();
        processor.OnApplicationEvent(ApplicationEventType.BatteryChanged, _ => { });

        // 操作与断言
        var cancelled = processor.EmitApplicationEvent(ApplicationEventType.BatteryChanged);
        await Assert.That(cancelled).IsFalse();
    }

    /// <summary>
    /// 监听器抛出的异常被吞下，不影响其他监听器。
    /// </summary>
    [Test]
    public async Task EmitApplicationEvent_ListenerException_DoesNotAffectOthers()
    {
        // 安排
        var processor = new EventProcessor();
        var secondCalled = false;
        processor.OnApplicationEvent(ApplicationEventType.NetworkChanged, _ => throw new InvalidOperationException("boom"));
        processor.OnApplicationEvent(ApplicationEventType.NetworkChanged, _ => secondCalled = true);

        // 操作
        await Assert.That(() => processor.EmitApplicationEvent(ApplicationEventType.NetworkChanged)).ThrowsNothing();

        // 断言
        await Assert.That(secondCalled).IsTrue();
    }

    /// <summary>
    /// Reset 清除所有自定义事件监听器，不影响应用事件监听器。
    /// </summary>
    [Test]
    public async Task Reset_ClearsCustomEventListeners_KeepsApplicationEventListeners()
    {
        // 安排
        var processor = new EventProcessor();
        var customCalled = 0;
        var appCalled = 0;
        processor.On("custom-event", _ => customCalled++);
        processor.OnApplicationEvent(ApplicationEventType.DisplayChanged, _ => appCalled++);

        // 操作
        processor.Reset();
        processor.Emit("custom-event");
        processor.EmitApplicationEvent(ApplicationEventType.DisplayChanged);

        // 断言
        await Assert.That(customCalled).IsEqualTo(0);
        await Assert.That(appCalled).IsEqualTo(1);
    }

    /// <summary>
    /// ClearApplicationEvents 清除所有应用事件监听器和钩子。
    /// </summary>
    [Test]
    public async Task ClearApplicationEvents_RemovesAllListenersAndHooks()
    {
        // 安排
        var processor = new EventProcessor();
        processor.OnApplicationEvent(ApplicationEventType.ClipboardChanged, _ => { });
        processor.RegisterApplicationEventHook(ApplicationEventType.ClipboardChanged, _ => { });

        // 操作
        processor.ClearApplicationEvents();

        // 断言
        await Assert.That(processor.ApplicationEventListenerCount(ApplicationEventType.ClipboardChanged)).IsEqualTo(0);
        await Assert.That(processor.ApplicationEventHookCount(ApplicationEventType.ClipboardChanged)).IsEqualTo(0);
    }

    /// <summary>
    /// 未订阅的事件类型 Emit 时不抛异常。
    /// </summary>
    [Test]
    public async Task EmitApplicationEvent_NoSubscribers_DoesNotThrow()
    {
        // 安排
        var processor = new EventProcessor();

        // 操作与断言
        await Assert.That(() => processor.EmitApplicationEvent(ApplicationEventType.LowMemory)).ThrowsNothing();
    }

    /// <summary>
    /// OnApplicationEvent 传入 null 回调抛出 ArgumentNullException。
    /// </summary>
    [Test]
    public async Task OnApplicationEvent_NullCallback_ThrowsArgumentNullException()
    {
        // 安排
        var processor = new EventProcessor();

        // 操作与断言
        await Assert.That(() => processor.OnApplicationEvent(ApplicationEventType.Started, null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    /// <summary>
    /// RegisterApplicationEventHook 传入 null 回调抛出 ArgumentNullException。
    /// </summary>
    [Test]
    public async Task RegisterApplicationEventHook_NullCallback_ThrowsArgumentNullException()
    {
        // 安排
        var processor = new EventProcessor();

        // 操作与断言
        await Assert.That(() => processor.RegisterApplicationEventHook(ApplicationEventType.Started, null!))
            .ThrowsExactly<ArgumentNullException>();
    }
}
