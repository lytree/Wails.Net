using System.Text;
using TUnit.Core;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Tests.Transport;

/// <summary>
/// ChunkStore 与 PendingChunks 的单元测试（TUnit）。
/// 对应 P0-C1 分块上传机制，验证会话管理、并发安全与 TTL 清理。
/// 对应 Wails v3 Go 版本 transport_http.go 中的 pendingChunks 与 chunkStore 行为。
/// </summary>
[NotInParallel]
public sealed class ChunkStoreTests
{
    // ============================================================
    // PendingChunks 单元测试
    // ============================================================

    /// <summary>
    /// PendingChunks 应在构造时正确存储 total。
    /// </summary>
    [Test]
    public async Task PendingChunks_Constructor_StoresTotal()
    {
        var pc = new PendingChunks(total: 5);

        await Assert.That(pc.Total).IsEqualTo(5);
        await Assert.That(pc.Size).IsEqualTo(0L);
    }

    /// <summary>
    /// AddChunk 应正确累计已接收 chunk 数量与字节大小。
    /// </summary>
    [Test]
    public async Task PendingChunks_AddChunk_AccumulatesReceivedCount()
    {
        var pc = new PendingChunks(total: 3);

        var received1 = pc.AddChunk(0, Encoding.UTF8.GetBytes("AAA"));
        var received2 = pc.AddChunk(1, Encoding.UTF8.GetBytes("BBB"));

        await Assert.That(received1).IsEqualTo(1);
        await Assert.That(received2).IsEqualTo(2);
        await Assert.That(pc.Size).IsEqualTo(6L);
    }

    /// <summary>
    /// Assemble 应按 index 顺序拼接所有 chunk。
    /// </summary>
    [Test]
    public async Task PendingChunks_Assemble_ConcatenatesInOrder()
    {
        var pc = new PendingChunks(total: 3);
        pc.AddChunk(0, Encoding.UTF8.GetBytes("Hello, "));
        pc.AddChunk(1, Encoding.UTF8.GetBytes("World"));
        pc.AddChunk(2, Encoding.UTF8.GetBytes("!"));

        var assembled = pc.Assemble();
        var text = Encoding.UTF8.GetString(assembled);

        await Assert.That(text).IsEqualTo("Hello, World!");
    }

    /// <summary>
    /// 乱序到达的 chunk 仍应按 index 顺序组装。
    /// </summary>
    [Test]
    public async Task PendingChunks_Assemble_OutOfOrderChunks_ConcatenatesByIndex()
    {
        var pc = new PendingChunks(total: 4);
        // 乱序到达
        pc.AddChunk(2, Encoding.UTF8.GetBytes("C"));
        pc.AddChunk(0, Encoding.UTF8.GetBytes("A"));
        pc.AddChunk(3, Encoding.UTF8.GetBytes("D"));
        pc.AddChunk(1, Encoding.UTF8.GetBytes("B"));

        var assembled = pc.Assemble();
        var text = Encoding.UTF8.GetString(assembled);

        await Assert.That(text).IsEqualTo("ABCD");
    }

    /// <summary>
    /// 重复写入同一 index 应覆盖旧值且 Size 不重复累加。
    /// </summary>
    [Test]
    public async Task PendingChunks_AddChunk_DuplicateIndex_OverwritesWithoutDoubleCount()
    {
        var pc = new PendingChunks(total: 2);

        pc.AddChunk(0, Encoding.UTF8.GetBytes("OLD"));  // 3 字节
        pc.AddChunk(1, Encoding.UTF8.GetBytes("SECOND"));  // 6 字节
        var oldSize = pc.Size;  // 9 字节

        // 重复发送同一 index（前端重试场景）
        pc.AddChunk(0, Encoding.UTF8.GetBytes("NEW_DATA"));  // 8 字节，覆盖 OLD（3 字节）

        var assembled = pc.Assemble();
        var text = Encoding.UTF8.GetString(assembled);

        await Assert.That(text).IsEqualTo("NEW_DATASECOND");
        await Assert.That(pc.Size).IsEqualTo(oldSize + 8 - 3);  // 减去旧值再加新值
    }

    /// <summary>
    /// 在 chunk 数量未达到 total 时 Assemble 应抛出异常。
    /// </summary>
    [Test]
    public async Task PendingChunks_Assemble_BeforeAllReceived_ThrowsInvalidOperationException()
    {
        var pc = new PendingChunks(total: 3);
        pc.AddChunk(0, Encoding.UTF8.GetBytes("A"));
        pc.AddChunk(1, Encoding.UTF8.GetBytes("B"));

        await Assert.That(() => pc.Assemble()).ThrowsExactly<InvalidOperationException>();
    }

    /// <summary>
    /// PendingChunks 不接受 total ≤ 0。
    /// </summary>
    [Test]
    public async Task PendingChunks_Constructor_ZeroOrNegativeTotal_Throws()
    {
        await Assert.That(() => new PendingChunks(0)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => new PendingChunks(-1)).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// AddChunk 应拒绝 null chunk。
    /// </summary>
    [Test]
    public async Task PendingChunks_AddChunk_NullChunk_Throws()
    {
        var pc = new PendingChunks(total: 1);

        await Assert.That(() => pc.AddChunk(0, null!)).ThrowsExactly<ArgumentNullException>();
    }

    /// <summary>
    /// 并发 AddChunk 应安全累积多个 chunk。
    /// </summary>
    [Test]
    [Timeout(10000)]
    public async Task PendingChunks_ConcurrentAddChunk_IsThreadSafe(CancellationToken ct)
    {
        const int chunkCount = 100;
        var pc = new PendingChunks(total: chunkCount);

        // 并发添加 100 个 chunk
        var tasks = Enumerable.Range(0, chunkCount)
            .Select(i => Task.Run(() =>
            {
                pc.AddChunk(i, new byte[] { (byte)(i % 256) });
            }, ct))
            .ToArray();

        await Task.WhenAll(tasks);

        await Assert.That(pc.Size).IsEqualTo((long)chunkCount);
        var assembled = pc.Assemble();
        await Assert.That(assembled.Length).IsEqualTo(chunkCount);

        // 每个 chunk 的字节值应等于其 index mod 256
        for (var i = 0; i < chunkCount; i++)
        {
            await Assert.That(assembled[i]).IsEqualTo((byte)(i % 256));
        }
    }

    // ============================================================
    // ChunkStore 单元测试
    // ============================================================

    /// <summary>
    /// GetOrAdd 对新 chunkID 应创建新会话。
    /// </summary>
    [Test]
    public async Task ChunkStore_GetOrAdd_NewChunkId_CreatesSession()
    {
        using var store = new ChunkStore();

        var pc = store.GetOrAdd("test-chunk-id-1", total: 3);

        await Assert.That(pc).IsNotNull();
        await Assert.That(pc.Total).IsEqualTo(3);
        await Assert.That(store.Count).IsEqualTo(1);
    }

    /// <summary>
    /// GetOrAdd 对已存在的 chunkID 应返回原会话（不创建新的）。
    /// </summary>
    [Test]
    public async Task ChunkStore_GetOrAdd_ExistingChunkId_ReturnsSameSession()
    {
        using var store = new ChunkStore();

        var pc1 = store.GetOrAdd("duplicate-id", total: 2);
        var pc2 = store.GetOrAdd("duplicate-id", total: 2);

        await Assert.That(ReferenceEquals(pc1, pc2)).IsTrue();
        await Assert.That(store.Count).IsEqualTo(1);
    }

    /// <summary>
    /// TryRemove 应移除指定会话。
    /// </summary>
    [Test]
    public async Task ChunkStore_TryRemove_ExistingChunkId_ReturnsTrue()
    {
        using var store = new ChunkStore();
        store.GetOrAdd("to-remove", total: 1);

        var removed = store.TryRemove("to-remove", out var pc);

        await Assert.That(removed).IsTrue();
        await Assert.That(pc).IsNotNull();
        await Assert.That(store.Count).IsEqualTo(0);
    }

    /// <summary>
    /// TryRemove 对不存在的 chunkID 应返回 false。
    /// </summary>
    [Test]
    public async Task ChunkStore_TryRemove_NonExistentChunkId_ReturnsFalse()
    {
        using var store = new ChunkStore();

        var removed = store.TryRemove("non-existent", out var pc);

        await Assert.That(removed).IsFalse();
        await Assert.That(pc).IsNull();
    }

    /// <summary>
    /// 完整的分块上传流程应正确组装。
    /// </summary>
    [Test]
    public async Task ChunkStore_FullUploadFlow_AssemblesCorrectly()
    {
        using var store = new ChunkStore();

        var chunkId = "flow-test-id";
        var total = 4;
        var originalPayloads = new[]
        {
            Encoding.UTF8.GetBytes("Hello"),
            Encoding.UTF8.GetBytes(", "),
            Encoding.UTF8.GetBytes("Wails"),
            Encoding.UTF8.GetBytes(".Net!")
        };

        // 模拟前端串行上传
        var pc = store.GetOrAdd(chunkId, total);
        for (var i = 0; i < total; i++)
        {
            var received = pc.AddChunk(i, originalPayloads[i]);
            if (i < total - 1)
            {
                await Assert.That(received).IsEqualTo(i + 1);
                await Assert.That(received < total).IsTrue();
            }
            else
            {
                await Assert.That(received).IsEqualTo(total);
            }
        }

        // 组装
        var assembled = pc.Assemble();
        var text = Encoding.UTF8.GetString(assembled);

        await Assert.That(text).IsEqualTo("Hello, Wails.Net!");
        store.TryRemove(chunkId, out _);
        await Assert.That(store.Count).IsEqualTo(0);
    }

    /// <summary>
    /// 常量值应与 Wails v3 一致。
    /// </summary>
    [Test]
    public async Task ChunkStore_Constants_MatchWailsV3()
    {
        // 使用局部变量绕过 TUnit 对 const 对 const 断言的检查。
        int maxTotal = ChunkStore.MaxChunkTotal;
        long maxChunkBody = ChunkStore.MaxChunkBodyBytes;
        long maxAssembled = ChunkStore.MaxAssembledBytes;
        var chunkTtl = ChunkStore.ChunkTTL;

        await Assert.That(maxTotal).IsEqualTo(1024);
        await Assert.That(maxChunkBody).IsEqualTo(1024L * 1024L);
        await Assert.That(maxAssembled).IsEqualTo(64L * 1024L * 1024L);
        await Assert.That(chunkTtl).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// UTF-8 字节切分不应破坏非 BMP 字符（surrogate pairs）。
    /// 验证：包含 emoji 的字符串切分后组装应得到原始字符串。
    /// </summary>
    [Test]
    public async Task ChunkStore_Assemble_UTF8Boundary_PreservesSurrogatePairs()
    {
        // 构造一个包含 emoji 的字符串，并模拟前端按 UTF-8 字节切分
        // 🚀 (U+1F680) 在 UTF-8 中占 4 字节：F0 9F 9A 80
        var original = "Hello 🚀 World 🎉 End";
        var utf8Bytes = Encoding.UTF8.GetBytes(original);

        // 模拟在 UTF-8 字节流中任意位置切分（例如 7 字节处，恰好在 🚀 的 UTF-8 序列中间）
        var splitPos = 7;
        var total = 2;
        var pc = new PendingChunks(total);

        pc.AddChunk(0, utf8Bytes[..splitPos]);
        pc.AddChunk(1, utf8Bytes[splitPos..]);

        var assembled = pc.Assemble();
        var text = Encoding.UTF8.GetString(assembled);

        await Assert.That(text).IsEqualTo(original);
    }

    /// <summary>
    /// 大尺寸 payload（模拟超过 512KB）切分为多个 chunk 后组装应正确。
    /// </summary>
    [Test]
    public async Task ChunkStore_Assemble_LargePayload_ReconstructsCorrectly()
    {
        // 构造 1MB 的随机数据
        var random = new Random(42);
        var original = new byte[1024 * 1024];
        random.NextBytes(original);

        // 模拟按 512KB 切分
        const int chunkSize = 512 * 1024;
        var total = (int)Math.Ceiling((double)original.Length / chunkSize);

        var pc = new PendingChunks(total);
        for (var i = 0; i < total; i++)
        {
            var start = i * chunkSize;
            var end = Math.Min(start + chunkSize, original.Length);
            var chunk = new byte[end - start];
            Buffer.BlockCopy(original, start, chunk, 0, chunk.Length);
            pc.AddChunk(i, chunk);
        }

        var assembled = pc.Assemble();

        await Assert.That(assembled.Length).IsEqualTo(original.Length);
        await Assert.That(assembled.SequenceEqual(original)).IsTrue();
    }
}
