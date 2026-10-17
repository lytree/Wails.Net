using System.Text;
using TUnit.Core;
using Wails.Net.Application.Events;

namespace Wails.Net.Application.Tests.Events;

/// <summary>
/// <see cref="EventPayloadStore"/> 的单元测试（TUnit）。
/// 对应事件投递硬化 P0-1 / Layer 3（Wails v3 PR #5930）：
/// 验证 id 形状、读取即删、按窗口绑定、关闭窗口即释放与关闭后拒绝新载荷。
/// </summary>
[NotInParallel]
public sealed class EventPayloadStoreTests
{
    private static byte[] Payload(string text) => Encoding.UTF8.GetBytes(text);

    [Test]
    public async Task Constants_AlignWithWailsImplementation()
    {
        // 先取出到局部变量，避免 TUnit 对常量表达式的断言分析（TUnitAssertions0005）。
        var maxInlineBytes = EventPayloadStore.MaxInlinePayloadBytes;
        var idLength = EventPayloadStore.IdLength;
        var pathPrefix = EventPayloadStore.PathPrefix;

        await Assert.That(maxInlineBytes).IsEqualTo(8192);
        await Assert.That(idLength).IsEqualTo(32);
        await Assert.That(pathPrefix).IsEqualTo("/wails/eventpayload/");
    }

    [Test]
    public async Task TryPut_ValidData_ReturnsLowercaseHexIdAndTracksBytes()
    {
        using var store = new EventPayloadStore();
        var data = Payload("hello");

        var ok = store.TryPut(windowId: 7, data, out var id);

        await Assert.That(ok).IsTrue();
        await Assert.That(id.Length).IsEqualTo(EventPayloadStore.IdLength);
        await Assert.That(EventPayloadStore.IsValidId(id)).IsTrue();
        await Assert.That(store.Count).IsEqualTo(1);
        await Assert.That(store.Bytes).IsEqualTo(data.Length);
    }

    [Test]
    public async Task TryPut_ManyPayloads_GeneratesUniqueIds()
    {
        using var store = new EventPayloadStore();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 200; i++)
        {
            await Assert.That(store.TryPut(windowId: 1, Payload($"payload-{i}"), out var id)).IsTrue();
            await Assert.That(ids.Add(id)).IsTrue();
        }

        await Assert.That(store.Count).IsEqualTo(200);
    }

    [Test]
    public async Task TryPut_EmptyData_ReturnsFalse()
    {
        using var store = new EventPayloadStore();

        await Assert.That(store.TryPut(windowId: 1, [], out var id)).IsFalse();
        await Assert.That(id).IsEqualTo(string.Empty);
        await Assert.That(store.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TryPut_NullData_ThrowsArgumentNullException()
    {
        using var store = new EventPayloadStore();

        await Assert.That(() => store.TryPut(1, null!, out _)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Take_ValidId_IsOneShot()
    {
        using var store = new EventPayloadStore();
        var data = Payload("once");
        store.TryPut(windowId: 3, data, out var id);

        var first = store.Take(id, windowId: 3);
        var second = store.Take(id, windowId: 3);

        await Assert.That(Encoding.UTF8.GetString(first!)).IsEqualTo("once");
        await Assert.That(second).IsNull();
        await Assert.That(store.Count).IsEqualTo(0);
        await Assert.That(store.Bytes).IsEqualTo(0);
    }

    [Test]
    public async Task Take_WindowMismatch_ReturnsNullAndKeepsPayload()
    {
        using var store = new EventPayloadStore();
        store.TryPut(windowId: 3, Payload("bound"), out var id);

        var wrongWindow = store.Take(id, windowId: 4);
        var rightWindow = store.Take(id, windowId: 3);

        await Assert.That(wrongWindow).IsNull();
        await Assert.That(Encoding.UTF8.GetString(rightWindow!)).IsEqualTo("bound");
    }

    [Test]
    public async Task Take_ZeroWindowId_SkipsWindowCheck()
    {
        using var store = new EventPayloadStore();
        store.TryPut(windowId: 3, Payload("any"), out var id);

        var data = store.Take(id, windowId: 0);

        await Assert.That(Encoding.UTF8.GetString(data!)).IsEqualTo("any");
    }

    [Test]
    public async Task Take_UnknownOrEmptyId_ReturnsNull()
    {
        using var store = new EventPayloadStore();

        await Assert.That(store.Take("00000000000000000000000000000000", 1)).IsNull();
        await Assert.That(store.Take(string.Empty, 1)).IsNull();
    }

    [Test]
    public async Task DropWindow_RemovesOnlyTargetWindowPayloads()
    {
        using var store = new EventPayloadStore();
        store.TryPut(windowId: 1, Payload("w1-a"), out var id1);
        store.TryPut(windowId: 2, Payload("w2"), out var id2);
        store.TryPut(windowId: 1, Payload("w1-b"), out var id1b);

        store.DropWindow(1);

        await Assert.That(store.Count).IsEqualTo(1);
        await Assert.That(store.Take(id1, 1)).IsNull();
        await Assert.That(store.Take(id1b, 1)).IsNull();
        await Assert.That(Encoding.UTF8.GetString(store.Take(id2, 2)!)).IsEqualTo("w2");
    }

    [Test]
    public async Task DropWindow_NoMatches_LeavesStoreUntouched()
    {
        using var store = new EventPayloadStore();
        store.TryPut(windowId: 1, Payload("a"), out var id);

        store.DropWindow(42);

        await Assert.That(store.Count).IsEqualTo(1);
        await Assert.That(store.Take(id, 1)).IsNotNull();
    }

    [Test]
    public async Task Dispose_ClearsEntriesAndRejectsFurtherPuts()
    {
        using var store = new EventPayloadStore();
        store.TryPut(windowId: 1, Payload("a"), out _);

        store.Dispose();

        await Assert.That(store.Count).IsEqualTo(0);
        await Assert.That(store.Bytes).IsEqualTo(0);
        await Assert.That(store.TryPut(windowId: 1, Payload("b"), out _)).IsFalse();
    }

    [Test]
    public async Task Dispose_IsIdempotent()
    {
        var store = new EventPayloadStore();
        store.TryPut(windowId: 1, Payload("a"), out _);

        store.Dispose();
        store.Dispose();

        await Assert.That(store.Count).IsEqualTo(0);
    }

    [Test]
    public async Task IsValidId_AcceptsOnlyThirtyTwoLowercaseHexChars()
    {
        await Assert.That(EventPayloadStore.IsValidId("0123456789abcdef0123456789abcdef")).IsTrue();
        await Assert.That(EventPayloadStore.IsValidId(null)).IsFalse();
        await Assert.That(EventPayloadStore.IsValidId(string.Empty)).IsFalse();
        await Assert.That(EventPayloadStore.IsValidId("0123456789abcdef0123456789abcde")).IsFalse();
        await Assert.That(EventPayloadStore.IsValidId("0123456789abcdef0123456789abcdef0")).IsFalse();
        await Assert.That(EventPayloadStore.IsValidId("0123456789ABCDEF0123456789abcdef")).IsFalse();
        await Assert.That(EventPayloadStore.IsValidId("0123456789abcdef0123456789abcdeg")).IsFalse();
    }

    [Test]
    public async Task Take_ConcurrentReads_ReturnPayloadExactlyOnce()
    {
        using var store = new EventPayloadStore();
        store.TryPut(windowId: 1, Payload("race"), out var id);

        var results = new byte[]?[8];
        Parallel.For(0, results.Length, i => results[i] = store.Take(id, 1));

        var winners = results.Count(r => r is not null);
        await Assert.That(winners).IsEqualTo(1);
    }
}
