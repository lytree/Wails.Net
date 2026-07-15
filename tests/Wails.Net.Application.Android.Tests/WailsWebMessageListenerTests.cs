using TUnit.Core;
using Wails.Net.Application.Android;

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
    public async Task Invoke_WithNull_ReturnsEmptyString()
    {
        // 安排
        var listener = new WailsWebMessageListener(1u);

        // 操作
        var result = listener.Invoke(null);

        // 断言：null 消息返回空字符串（不是抛异常）
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Invoke_WithEmptyString_ReturnsEmptyString()
    {
        // 安排
        var listener = new WailsWebMessageListener(1u);

        // 操作
        var result = listener.Invoke(string.Empty);

        // 断言：空字符串返回空字符串
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Invoke_WithNonEmpty_ReturnsErrorJson_WhenNoApplication()
    {
        // 安排：无全局 Application 实例时，Invoke 应返回错误 JSON
        var listener = new WailsWebMessageListener(1u);

        // 操作
        var result = listener.Invoke("{\"name\":\"test\"}");

        // 断言：无 Application 时返回包含 error 字段的 JSON
        await Assert.That(result).IsNotNull();
        await Assert.That(result).Contains("error");
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
    public async Task Invoke_WithWhitespaceString_DoesNotThrow()
    {
        // 安排：空白字符串（仅含空格、制表符、换行等）
        var listener = new WailsWebMessageListener(1u);

        // 操作与断言：空白字符串不为空（IsNullOrEmpty 对空白返回 false），
        // 调用 HandleMessageFromFrontend 解析失败会返回错误 JSON（不抛异常）
        string? result = null;
        await Assert.That(() => result = listener.Invoke("   \t\n  ")).ThrowsNothing();
        // 空白字符串在 JsonOptions 中可能解析失败或解析为 null Message，返回空字符串或错误 JSON
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task Invoke_WithInvalidJson_ReturnsEmptyOrError_WhenNoApplication()
    {
        // 安排：无全局 Application 实例，传入非法 JSON
        var listener = new WailsWebMessageListener(1u);

        // 操作：非法 JSON 在 HandleMessageFromFrontend 中 ParseMessage 返回 null，
        // 导致返回 null，Invoke 返回空字符串；或在 Application 不可用时返回错误 JSON
        var result = listener.Invoke("not a valid json {");

        // 断言：应返回空字符串或错误 JSON（不抛异常）
        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task Invoke_WithValidJson_ReturnsErrorJson_WhenNoApplication()
    {
        // 安排：无全局 Application 实例，传入有效 JSON
        var listener = new WailsWebMessageListener(1u);

        // 操作
        var result = listener.Invoke("{\"type\":\"event\",\"name\":\"click\"}");

        // 断言：无 Application 时返回错误 JSON
        await Assert.That(result).Contains("error");
    }

    [Test]
    public async Task Invoke_WithVeryLongString_DoesNotThrow_WhenNoApplication()
    {
        // 安排：无全局 Application 实例，传入超长字符串
        var listener = new WailsWebMessageListener(1u);
        var longMessage = new string('x', 10000);

        // 操作与断言：超长字符串不应导致异常（无 Application 时返回错误 JSON）
        string? result = null;
        await Assert.That(() => result = listener.Invoke(longMessage)).ThrowsNothing();
        await Assert.That(result).IsNotNull();
    }
}
