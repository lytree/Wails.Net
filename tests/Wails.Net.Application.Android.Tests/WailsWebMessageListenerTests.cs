using TUnit.Core;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Android.Tests;

/// <summary>
/// WailsWebMessageListener 的单元测试（TUnit）。
/// 测试 IPC 桥接对象的基本行为，包括构造与空消息处理。
/// 完整的 IPC 消息处理需要 Application 实例，由集成测试覆盖。
/// </summary>
[NotInParallel]
public sealed class WailsWebMessageListenerTests
{
    [Test]
    public async Task Constructor_DoesNotThrow()
    {
        // 操作与断言：构造应不抛异常
        var listener = new WailsWebMessageListener(42u);
        await Assert.That(listener).IsNotNull();
    }

    [Test]
    public async Task PostMessage_WithNull_DoesNotThrow()
    {
        // 安排
        var listener = new WailsWebMessageListener(1u);

        // 操作与断言：null 消息应被静默忽略
        await Assert.That(() => listener.PostMessage(null)).ThrowsNothing();
    }

    [Test]
    public async Task PostMessage_WithEmptyString_DoesNotThrow()
    {
        // 安排
        var listener = new WailsWebMessageListener(1u);

        // 操作与断言：空字符串应被静默忽略
        await Assert.That(() => listener.PostMessage("")).ThrowsNothing();
    }

    [Test]
    public async Task PostMessage_WithNonEmpty_DoesNotThrow_WhenNoApplication()
    {
        // 安排：无全局 Application 实例时，PostMessage 应静默忽略
        var listener = new WailsWebMessageListener(1u);

        // 操作与断言：不应抛异常（即使没有 Application 实例处理消息）
        await Assert.That(() => listener.PostMessage("{\"name\":\"test\"}")).ThrowsNothing();
    }
}
