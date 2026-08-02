using TUnit.Core;
using TUnit.Assertions;
using Wails.Net.Events;

namespace Wails.Net.Events.Tests;

/// <summary>
/// <see cref="CommonEvents"/> 单元测试：保留事件名识别与集合完整性。
/// 这是防止自定义事件名与系统保留名冲突的第一道防线。
/// </summary>
public class CommonEventsTests
{
    [Test]
    public async Task IsKnownEvent_SystemEvent_ReturnsTrue()
    {
        await Assert.That(CommonEvents.IsKnownEvent(KnownEvents.Startup)).IsTrue();
        await Assert.That(CommonEvents.IsKnownEvent(KnownEvents.Shutdown)).IsTrue();
        await Assert.That(CommonEvents.IsKnownEvent(KnownEvents.SystemTrayMenuOpen)).IsTrue();
    }

    [Test]
    public async Task IsKnownEvent_CustomName_ReturnsFalse()
    {
        await Assert.That(CommonEvents.IsKnownEvent("my-app:custom-event")).IsFalse();
    }

    [Test]
    public async Task IsKnownEvent_Null_ReturnsFalse()
    {
        await Assert.That(CommonEvents.IsKnownEvent(null)).IsFalse();
    }

    [Test]
    public async Task KnownEventNames_ContainsAllReserved_AndCountIs14()
    {
        // 14 个保留名来自 CommonEvents.KnownEventNames 的显式列表；数量被改少会破坏冲突防护
        await Assert.That(CommonEvents.KnownEventNames.Count).IsEqualTo(14);
        await Assert.That(CommonEvents.KnownEventNames.Contains(KnownEvents.Startup)).IsTrue();
        await Assert.That(CommonEvents.KnownEventNames.Contains(KnownEvents.WindowClosing)).IsTrue();
        await Assert.That(CommonEvents.KnownEventNames.Contains(KnownEvents.SystemTrayMenuOpen)).IsTrue();
    }
}
