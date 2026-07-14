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

    [Test]
    public async Task Constructor_WithZeroWindowId_DoesNotThrow()
    {
        // 操作与断言：窗口 ID 为 0 应被允许
        var listener = new WailsWebMessageListener(0u);
        await Assert.That(listener).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithMaxWindowId_DoesNotThrow()
    {
        // 操作与断言：窗口 ID 为 uint.MaxValue 应被允许
        var listener = new WailsWebMessageListener(uint.MaxValue);
        await Assert.That(listener).IsNotNull();
    }

    [Test]
    public async Task Constructor_WithDifferentWindowIds_DoesNotThrow()
    {
        // 操作与断言：不同的窗口 ID 均可正常构造
        var listener1 = new WailsWebMessageListener(1u);
        var listener2 = new WailsWebMessageListener(2u);
        var listener3 = new WailsWebMessageListener(999u);

        await Assert.That(listener1).IsNotNull();
        await Assert.That(listener2).IsNotNull();
        await Assert.That(listener3).IsNotNull();
    }

    [Test]
    public async Task PostMessage_WithWhitespaceString_DoesNotThrow()
    {
        // 安排：空白字符串（仅含空格、制表符、换行等）
        var listener = new WailsWebMessageListener(1u);

        // 操作与断言：空白字符串应被静默忽略（string.IsNullOrEmpty 对空白返回 false，
        // 但无 Application 时 HandleMessageFromFrontend 不会被调用）
        await Assert.That(() => listener.PostMessage("   \t\n  ")).ThrowsNothing();
    }

    [Test]
    public async Task PostMessage_WithInvalidJson_DoesNotThrow_WhenNoApplication()
    {
        // 安排：无全局 Application 实例，传入非法 JSON
        var listener = new WailsWebMessageListener(1u);

        // 操作与断言：无 Application 时 PostMessage 静默忽略，不解析 JSON
        await Assert.That(() => listener.PostMessage("not a valid json {")).ThrowsNothing();
    }

    [Test]
    public async Task PostMessage_WithValidJson_DoesNotThrow_WhenNoApplication()
    {
        // 安排：无全局 Application 实例，传入有效 JSON
        var listener = new WailsWebMessageListener(1u);

        // 操作与断言：无 Application 时 PostMessage 静默忽略
        await Assert.That(() => listener.PostMessage("{\"type\":\"event\",\"name\":\"click\"}")).ThrowsNothing();
    }

    [Test]
    public async Task PostMessage_WithVeryLongString_DoesNotThrow_WhenNoApplication()
    {
        // 安排：无全局 Application 实例，传入超长字符串
        var listener = new WailsWebMessageListener(1u);
        var longMessage = new string('x', 10000);

        // 操作与断言：超长字符串不应导致异常（无 Application 时静默忽略）
        await Assert.That(() => listener.PostMessage(longMessage)).ThrowsNothing();
    }
}
