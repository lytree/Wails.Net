using TUnit.Core;
using TUnit.Assertions;
using Wails.Net.Events;

namespace Wails.Net.Events.Tests;

/// <summary>
/// <see cref="KnownEvents"/> 单元测试：事件类型枚举到事件名常量的映射、uint 阈值路由与未知值回退。
/// 这是对「新增枚举值却漏改 switch 分支」这类回归的强约束。
/// </summary>
public class KnownEventsTests
{
    [Test]
    public async Task GetEventName_WindowEventType_MapsToConstant()
    {
        await Assert.That(KnownEvents.GetEventName(WindowEventType.WindowCreated)).IsEqualTo(KnownEvents.WindowCreated);
        await Assert.That(KnownEvents.GetEventName(WindowEventType.WindowClosed)).IsEqualTo(KnownEvents.WindowClosed);
        await Assert.That(KnownEvents.GetEventName(WindowEventType.WindowTitleChanged)).IsEqualTo(KnownEvents.WindowTitleChanged);
        await Assert.That(KnownEvents.GetEventName(WindowEventType.WindowDragDrop)).IsEqualTo(KnownEvents.WindowDragDrop);
    }

    [Test]
    public async Task GetEventName_WindowEventType_AllDefinedValues_HandledExplicitly()
    {
        // 任一窗口事件若漏加 switch 分支，会静默回退到 wails:custom:，此测试拦截该回归
        foreach (WindowEventType t in Enum.GetValues<WindowEventType>())
        {
            var name = KnownEvents.GetEventName(t);
            await Assert.That(name.StartsWith("wails:custom:")).IsFalse();
        }
    }

    [Test]
    public async Task GetEventName_ApplicationEventType_MapsToConstant()
    {
        await Assert.That(KnownEvents.GetEventName(ApplicationEventType.Started)).IsEqualTo(KnownEvents.Startup);
        await Assert.That(KnownEvents.GetEventName(ApplicationEventType.Shutdown)).IsEqualTo(KnownEvents.Shutdown);
        await Assert.That(KnownEvents.GetEventName(ApplicationEventType.LowMemory)).IsEqualTo(KnownEvents.LowMemory);
    }

    [Test]
    public async Task GetEventName_ApplicationEventType_AllDefinedValues_HandledExplicitly()
    {
        foreach (ApplicationEventType t in Enum.GetValues<ApplicationEventType>())
        {
            var name = KnownEvents.GetEventName(t);
            await Assert.That(name.StartsWith("wails:custom:")).IsFalse();
        }
    }

    [Test]
    public async Task GetEventName_Uint_ThresholdRoutesWindowVsApp()
    {
        // 窗口事件枚举值 >= 1000，应用事件 < 1000
        await Assert.That(KnownEvents.GetEventName((uint)WindowEventType.WindowCreated)).IsEqualTo(KnownEvents.WindowCreated);
        await Assert.That(KnownEvents.GetEventName((uint)ApplicationEventType.Started)).IsEqualTo(KnownEvents.Startup);
    }

    [Test]
    public async Task GetEventName_Uint_UnknownValue_FallsBackToCustom()
    {
        await Assert.That(KnownEvents.GetEventName(1999u)).IsEqualTo("wails:custom:1999");
        await Assert.That(KnownEvents.GetEventName(999u)).IsEqualTo("wails:custom:999");
    }
}
