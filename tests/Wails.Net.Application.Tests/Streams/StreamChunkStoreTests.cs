using TUnit.Core;
using Wails.Net.Application.Streams;

namespace Wails.Net.Application.Tests.Streams;

/// <summary>
/// <see cref="StreamChunkStore"/> 的单元测试（TUnit）：超大帧乱序重组、末片重试、重复 / 冲突 / 溢出拒绝与清账。
/// 对应 Wails v3 Go 版本 <c>stream_transport.go</c> 中的 <c>streamChunkStore</c>。
/// </summary>
[NotInParallel]
public sealed class StreamChunkStoreTests
{
    [Test]
    public async Task Add_OutOfOrder_AssemblesByIndex()
    {
        var store = new StreamChunkStore(closed: false, manager: null);

        await Assert.That(store.Add("c1", 2, 3, [3], out _)).IsEqualTo(StreamChunkStatus.Partial);
        await Assert.That(store.Add("c1", 0, 3, [1], out _)).IsEqualTo(StreamChunkStatus.Partial);
        var status = store.Add("c1", 1, 3, [2], out var assembled);

        await Assert.That(status).IsEqualTo(StreamChunkStatus.Complete);
        await Assert.That(assembled!.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
    }

    [Test]
    public async Task Add_DuplicateIndex_IsRejected()
    {
        var store = new StreamChunkStore(closed: false, manager: null);
        store.Add("d", 0, 2, [1], out _);

        await Assert.That(store.Add("d", 0, 2, [1], out _)).IsEqualTo(StreamChunkStatus.Duplicate);
    }

    [Test]
    public async Task Add_ConflictingTotal_IsRejected()
    {
        var store = new StreamChunkStore(closed: false, manager: null);
        store.Add("x", 0, 2, [1], out _);

        await Assert.That(store.Add("x", 0, 3, [1], out _)).IsEqualTo(StreamChunkStatus.Conflict);
    }

    [Test]
    public async Task Add_TooBigAssembly_IsRejectedWithClearedAccounting()
    {
        var store = new StreamChunkStore(closed: false, manager: null);
        var large = new byte[StreamConstants.MaxFrameBytes];

        await Assert.That(store.Add("t", 0, 2, large, out _)).IsEqualTo(StreamChunkStatus.Partial);
        await Assert.That(store.Add("t", 1, 2, [1], out _)).IsEqualTo(StreamChunkStatus.TooBig);

        // 集合已清账：同一 id 可重新开始（不再是 Partial 之外的残留）。
        await Assert.That(store.Add("t", 0, 2, [1], out _)).IsEqualTo(StreamChunkStatus.Partial);
    }

    [Test]
    public async Task Add_RetryOfCompletingChunk_ReturnsSameAssembled()
    {
        var store = new StreamChunkStore(closed: false, manager: null);
        store.Add("r", 0, 2, [1], out _);
        await Assert.That(store.Add("r", 1, 2, [2], out var assembled)).IsEqualTo(StreamChunkStatus.Complete);

        // 末片重试（相同 id、序号、字节）返回同意帧。
        var retry = store.Add("r", 1, 2, [2], out var retried);
        await Assert.That(retry).IsEqualTo(StreamChunkStatus.Complete);
        await Assert.That(ReferenceEquals(assembled, retried)).IsTrue();

        // 非法重试（不同字节）被拒绝。
        await Assert.That(store.Add("r", 1, 2, [9], out _)).IsEqualTo(StreamChunkStatus.Duplicate);
    }

    [Test]
    public async Task Remove_ReleasesSetAndAllowsFreshAssembly()
    {
        var store = new StreamChunkStore(closed: false, manager: null);
        store.Add("m", 0, 2, [1], out _);

        store.Remove("m");

        await Assert.That(store.Add("m", 0, 2, [5], out _)).IsEqualTo(StreamChunkStatus.Partial);
    }

    [Test]
    public async Task Close_RejectsFurtherChunks()
    {
        var store = new StreamChunkStore(closed: false, manager: null);
        store.Add("c", 0, 2, [1], out _);

        store.Close();

        await Assert.That(store.Add("c", 0, 2, [1], out _)).IsEqualTo(StreamChunkStatus.Closed);
    }

    [Test]
    public async Task Add_ExceedingMaxChunkSets_ReturnsFull()
    {
        var store = new StreamChunkStore(closed: false, manager: null);
        for (var i = 0; i < StreamConstants.MaxChunkSets; i++)
        {
            await Assert.That(store.Add($"set{i}", 0, 2, [1], out _)).IsEqualTo(StreamChunkStatus.Partial);
        }

        await Assert.That(store.Add("over", 0, 2, [1], out _)).IsEqualTo(StreamChunkStatus.Full);
    }

    [Test]
    public async Task GlobalChunkQuota_ExhaustsThenRecovers()
    {
        var manager = StreamTestSupport.NewManager();

        // 全局分片载荷额度为 2 × MaxFrameBytes，缺口由超出部分触发。
        await Assert.That(manager.ReserveChunkResources(StreamConstants.MaxFrameBytes, 1)).IsTrue();
        await Assert.That(manager.ReserveChunkResources(StreamConstants.MaxFrameBytes, 1)).IsTrue();
        await Assert.That(manager.ReserveChunkResources(1, 1)).IsFalse();

        manager.ReleaseChunkResources(StreamConstants.MaxFrameBytes, 1);
        await Assert.That(manager.ReserveChunkResources(1, 1)).IsTrue();
    }
}
