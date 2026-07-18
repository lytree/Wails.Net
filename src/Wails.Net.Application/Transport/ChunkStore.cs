using System.Collections.Concurrent;

namespace Wails.Net.Application.Transport;

/// <summary>
/// 分块上传会话，累积同一 chunkID 的所有 chunk 并按顺序拼接。
/// 对应 Wails v3 Go 版本 transport_http.go 中的 <c>pendingChunks</c> 结构。
/// <para>
/// 每个会话由 <c>chunkID</c>（前端 nanoid 生成）唯一标识，
/// 持有 <c>total</c> 总块数与已接收 chunk 的字典。
/// 当所有 chunk 到齐后由 <see cref="Assemble"/> 拼接为完整字节数组。
/// </para>
/// <para>
/// <b>线程安全</b>：内部使用 <see cref="Lock"/> 保护并发 AddChunk/Assemble 调用，
/// 对应 Wails v3 Go 版本中的 <c>sync.Mutex</c>。
/// </para>
/// </summary>
internal sealed class PendingChunks
{
    /// <summary>
    /// 同步锁，保护 <see cref="_chunks"/> 与 <see cref="Size"/> 的并发访问。
    /// </summary>
    private readonly Lock _lock = new();

    /// <summary>
    /// 已接收 chunk 的字典：index → chunk bytes。
    /// </summary>
    private readonly Dictionary<int, byte[]> _chunks = new();

    /// <summary>
    /// 会话创建时间（UTC），用于 TTL 清理判断。
    /// 对应 Wails v3 Go 版本中 <c>pendingChunks.createdAt</c> 字段。
    /// </summary>
    private readonly DateTime _createdAt = DateTime.UtcNow;

    /// <summary>
    /// 获取本会话预期的总 chunk 数量。
    /// </summary>
    public int Total { get; }

    /// <summary>
    /// 获取当前已接收 chunk 的累计字节大小。
    /// 用于在 AddChunk 后判断是否超过 <see cref="ChunkStore.MaxAssembledBytes"/>。
    /// </summary>
    public long Size { get; private set; }

    /// <summary>
    /// 获取会话创建时间（UTC）。
    /// </summary>
    public DateTime CreatedAt => _createdAt;

    /// <summary>
    /// 使用指定的总 chunk 数量构造 PendingChunks 实例。
    /// </summary>
    /// <param name="total">总 chunk 数量，必须为正整数。</param>
    public PendingChunks(int total)
    {
        if (total <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(total), "total 必须为正整数");
        }
        Total = total;
    }

    /// <summary>
    /// 添加一个 chunk 到会话中。
    /// </summary>
    /// <param name="index">chunk 索引（0-based）。</param>
    /// <param name="chunk">chunk 字节数据。</param>
    /// <returns>返回当前已接收的不同 index 的 chunk 数量。</returns>
    /// <remarks>
    /// 若同一 index 被多次写入，后写入的覆盖前者，且 <see cref="Size"/> 不会重复累加（先减去旧值再加新值）。
    /// 对应 Wails v3 Go 版本中 <c>pc.chunks[idx] = chunk</c> 操作。
    /// </remarks>
    public int AddChunk(int index, byte[] chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        lock (_lock)
        {
            if (_chunks.TryGetValue(index, out var existing))
            {
                // 重复发送同一 index（重试场景）：先减去旧 chunk 大小
                Size -= existing.Length;
            }
            _chunks[index] = chunk;
            Size += chunk.Length;
            return _chunks.Count;
        }
    }

    /// <summary>
    /// 按 index 顺序拼接所有已接收 chunk 为完整字节数组。
    /// </summary>
    /// <returns>组装后的完整字节数组。</returns>
    /// <exception cref="InvalidOperationException">
    /// 若已接收 chunk 数量不等于 <see cref="Total"/>，无法完整组装。
    /// </exception>
    public byte[] Assemble()
    {
        lock (_lock)
        {
            if (_chunks.Count != Total)
            {
                throw new InvalidOperationException(
                    $"无法组装：已接收 {_chunks.Count} 个 chunk，预期 {Total} 个");
            }

            // 预分配内存，避免多次扩容
            var result = new byte[Size];
            var offset = 0;
            for (var i = 0; i < Total; i++)
            {
                var chunk = _chunks[i];
                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }
            return result;
        }
    }
}

/// <summary>
/// chunkID → <see cref="PendingChunks"/> 的并发安全存储，支持 TTL 自动清理。
/// 对应 Wails v3 Go 版本 transport_http.go 中的 <c>HTTPTransport.chunkStore</c>
/// （<c>sync.Map</c> + 后台 <c>cleanupChunks</c> goroutine）。
/// <para>
/// <b>限制</b>：单会话最大 chunk 数 <see cref="MaxChunkTotal"/>、
/// 单 chunk body 最大字节数 <see cref="MaxChunkBodyBytes"/>、
/// 组装后总大小上限 <see cref="MaxAssembledBytes"/>、
/// 会话 TTL <see cref="ChunkTTL"/>，与 Wails v3 保持一致。
/// </para>
/// </summary>
internal sealed class ChunkStore : IDisposable
{
    /// <summary>
    /// 单会话允许的最大 chunk 数量。
    /// 对应 Wails v3 Go 版本 <c>maxChunkTotal = 1024</c>。
    /// </summary>
    public const int MaxChunkTotal = 1024;

    /// <summary>
    /// 单个 chunk body 允许的最大字节数（1 MB）。
    /// 对应 Wails v3 Go 版本 <c>maxChunkBodyBytes = 1 * 1024 * 1024</c>。
    /// </summary>
    public const long MaxChunkBodyBytes = 1024 * 1024;

    /// <summary>
    /// 组装后总字节数上限（64 MB）。
    /// 对应 Wails v3 Go 版本 <c>maxAssembledBytes = 64 * 1024 * 1024</c>。
    /// </summary>
    public const long MaxAssembledBytes = 64 * 1024 * 1024;

    /// <summary>
    /// 会话 TTL（30 秒），超过此时间未完成的会话将被清理。
    /// 对应 Wails v3 Go 版本 <c>chunkTTL = 30 * time.Second</c>。
    /// </summary>
    public static readonly TimeSpan ChunkTTL = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 后台清理周期（10 秒）。
    /// 对应 Wails v3 Go 版本中 <c>time.NewTicker(10 * time.Second)</c>。
    /// </summary>
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// chunkID → PendingChunks 的并发存储。
    /// </summary>
    private readonly ConcurrentDictionary<string, PendingChunks> _store = new();

    /// <summary>
    /// 后台清理定时器。
    /// </summary>
    private readonly Timer _cleanupTimer;

    /// <summary>
    /// 构造 ChunkStore 实例，启动后台 TTL 清理任务。
    /// </summary>
    public ChunkStore()
    {
        _cleanupTimer = new Timer(
            _ => CleanupExpired(),
            null,
            CleanupInterval,
            CleanupInterval);
    }

    /// <summary>
    /// 原子地获取或创建指定 chunkID 的会话。
    /// 对应 Wails v3 Go 版本 <c>sync.Map.LoadOrStore</c>。
    /// </summary>
    /// <param name="chunkId">chunk 会话唯一标识符。</param>
    /// <param name="total">预期总 chunk 数量。</param>
    /// <returns>已有或新建的 <see cref="PendingChunks"/> 实例。</returns>
    public PendingChunks GetOrAdd(string chunkId, int total)
    {
        return _store.GetOrAdd(chunkId, _ => new PendingChunks(total));
    }

    /// <summary>
    /// 尝试从存储中移除指定 chunkID 的会话。
    /// 对应 Wails v3 Go 版本 <c>sync.Map.Delete</c>。
    /// </summary>
    /// <param name="chunkId">要移除的 chunk 会话标识符。</param>
    /// <param name="pc">移除成功时输出被移除的 <see cref="PendingChunks"/> 实例。</param>
    /// <returns>是否移除成功。</returns>
    public bool TryRemove(string chunkId, out PendingChunks? pc)
    {
        return _store.TryRemove(chunkId, out pc);
    }

    /// <summary>
    /// 获取当前存储中的会话数量（主要用于测试与诊断）。
    /// </summary>
    public int Count => _store.Count;

    /// <summary>
    /// 清理已过期的会话（创建时间 + TTL &lt; 当前时间）。
    /// 对应 Wails v3 Go 版本 <c>cleanupChunks</c> 函数。
    /// </summary>
    private void CleanupExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _store)
        {
            if (now - kvp.Value.CreatedAt > ChunkTTL)
            {
                _store.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// 释放后台清理定时器。
    /// </summary>
    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }
}
