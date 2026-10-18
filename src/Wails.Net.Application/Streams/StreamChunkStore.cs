namespace Wails.Net.Application.Streams;

/// <summary>
/// <see cref="StreamChunkStore.Add"/> 的结果。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream_transport.go</c> 中
/// <c>streamChunkStore.add</c> 返回的 (assembled, complete, error) 三元组。
/// </summary>
internal enum StreamChunkStatus
{
    /// <summary>已存下一个分片，整帧尚未完整。</summary>
    Partial,

    /// <summary>末片到达，整帧已完成（<c>assembled</c> 为完整帧）。</summary>
    Complete,

    /// <summary>分片集合数超限或全局分片额度已满（可重试）。</summary>
    Full,

    /// <summary>会话已关闭。</summary>
    Closed,

    /// <summary>与已存在集合的 total 冲突。</summary>
    Conflict,

    /// <summary>重复分片序号，或已完成集合的非法重试。</summary>
    Duplicate,

    /// <summary>重组后的整帧超过单帧上限。</summary>
    TooBig,
}

/// <summary>
/// 单 session 的超大前端帧重组存储。
/// 对应 Wails v3 Go 版本 <c>v3/pkg/application/stream_transport.go</c> 中的 <c>streamChunkStore</c>
/// 与 <c>streamChunkSet</c>。
/// <para>
/// 完成一个集合会临时同时保留其分片与连续的整帧，因此准入的载荷额度是有效 256 MiB 上限的一半，
/// 以支持两个同时进行的最大尺寸上传而不让重组的副本把保留数据推过上限。
/// </para>
/// </summary>
internal sealed class StreamChunkStore
{
    /// <summary>保护集合字典与记账的锁。</summary>
    private readonly object _lock = new();

    /// <summary>分片 id → 集合。</summary>
    private readonly Dictionary<string, ChunkSet> _items = new(StringComparer.Ordinal);

    /// <summary>本存储当前保留的字节数。</summary>
    private int _bytes;

    /// <summary>本存储当前保留的分片数。</summary>
    private int _parts;

    /// <summary>是否已关闭。</summary>
    private bool _closed;

    /// <summary>所属管理器（用于全局分片额度记账）；可为 null。</summary>
    private readonly StreamManager? _manager;

    /// <summary>
    /// 构造分片重组存储。
    /// </summary>
    /// <param name="closed">宿主 session 是否已关闭。</param>
    /// <param name="manager">所属管理器。</param>
    public StreamChunkStore(bool closed, StreamManager? manager)
    {
        _closed = closed;
        _manager = manager;
    }

    /// <summary>
    /// 存下一个分片；末片到达时返回重组后的整帧。
    /// 拒绝以返回状态而非「尚未完成」表示，避免端点回 204 让客户端误以为成功而丢帧。
    /// 对应 Go <c>streamChunkStore.add</c>。
    /// </summary>
    /// <param name="id">分片集合 id。</param>
    /// <param name="index">分片序号。</param>
    /// <param name="total">分片总数。</param>
    /// <param name="data">分片载荷。</param>
    /// <param name="assembled">完成时输出重组后的整帧。</param>
    /// <returns>处理结果。</returns>
    public StreamChunkStatus Add(string id, int index, int total, byte[] data, out byte[]? assembled)
    {
        assembled = null;

        lock (_lock)
        {
            if (_closed)
            {
                return StreamChunkStatus.Closed;
            }

            // 在接收更多前先回收消失页面遗留的集合。
            var now = DateTimeOffset.UtcNow;
            List<string>? expired = null;
            foreach (var pair in _items)
            {
                if (now - pair.Value.Created > StreamConstants.HoldTimeout)
                {
                    (expired ??= new List<string>()).Add(pair.Key);
                }
            }

            if (expired is not null)
            {
                foreach (var key in expired)
                {
                    ReleaseSetLocked(key);
                }
            }

            var createdSet = false;
            if (!_items.TryGetValue(id, out var set))
            {
                if (_items.Count >= StreamConstants.MaxChunkSets)
                {
                    return StreamChunkStatus.Full;
                }

                set = new ChunkSet(total, now);
                _items[id] = set;
                createdSet = true;
            }

            if (set.Total != total)
            {
                ReleaseSetLocked(id);
                return StreamChunkStatus.Conflict;
            }

            if (set.Assembled is not null)
            {
                // 只有完成本集合的那个请求可以合法重试（它可能在重组后收到 429）。
                // 在端点确认下游投递并移除集合前，返回同一份不可变帧。
                if (index == set.RetryIndex && data.AsSpan().SequenceEqual(set.RetryPart))
                {
                    assembled = set.Assembled;
                    return StreamChunkStatus.Complete;
                }

                ReleaseSetLocked(id);
                return StreamChunkStatus.Duplicate;
            }

            if (set.Parts!.ContainsKey(index))
            {
                ReleaseSetLocked(id);
                return StreamChunkStatus.Duplicate;
            }

            if (_bytes + data.Length > StreamConstants.MaxFrameBytes)
            {
                ReleaseSetLocked(id);
                return StreamChunkStatus.TooBig;
            }

            if (_manager is not null && !_manager.ReserveChunkResources(data.Length, 1))
            {
                // 共享额度是暂态背压：保留既有部分集合以便客户端在其它 session 释放额度后重试本分片；
                // 但不保留新建的空集合。
                if (createdSet)
                {
                    _items.Remove(id);
                }

                return StreamChunkStatus.Full;
            }

            set.Parts[index] = data;
            set.Size += data.Length;
            set.PartCount++;
            _bytes += data.Length;
            _parts++;

            if (set.Parts.Count < total)
            {
                return StreamChunkStatus.Partial;
            }

            var buffer = new byte[set.Size];
            var offset = 0;
            var retryStart = 0;
            var retryEnd = 0;
            for (var i = 0; i < total; i++)
            {
                var part = set.Parts[i];
                var start = offset;
                part.CopyTo(buffer, offset);
                offset += part.Length;
                if (i == index)
                {
                    retryStart = start;
                    retryEnd = offset;
                }
            }

            set.Parts = null;
            set.Assembled = buffer;
            set.RetryIndex = index;
            var retryPart = new byte[retryEnd - retryStart];
            Array.Copy(buffer, retryStart, retryPart, 0, retryPart.Length);
            set.RetryPart = retryPart;

            assembled = buffer;
            return StreamChunkStatus.Complete;
        }
    }

    /// <summary>
    /// 在整帧被连接收件箱接受（或因背压以外的原因被永久拒绝）后确认并移除集合。
    /// 对应 Go <c>streamChunkStore.remove</c>。
    /// </summary>
    /// <param name="id">分片集合 id。</param>
    public void Remove(string id)
    {
        lock (_lock)
        {
            ReleaseSetLocked(id);
        }
    }

    /// <summary>
    /// 在其页面 session 结束时释放所有未完成 / 可重试的上传，并阻止已派发请求在取消后重建状态。
    /// 对应 Go <c>streamChunkStore.close</c>。
    /// </summary>
    public void Close()
    {
        int released;
        int releasedParts;
        lock (_lock)
        {
            _closed = true;
            _items.Clear();
            released = _bytes;
            releasedParts = _parts;
            _bytes = 0;
            _parts = 0;
        }

        _manager?.ReleaseChunkResources(released, releasedParts);
    }

    /// <summary>
    /// 移除集合并回退其占用的本存储与全局记账（调用方必须持有 <see cref="_lock"/>）。
    /// </summary>
    /// <param name="id">集合 id。</param>
    private void ReleaseSetLocked(string id)
    {
        if (!_items.Remove(id, out var set))
        {
            return;
        }

        _bytes -= set.Size;
        _parts -= set.PartCount;
        _manager?.ReleaseChunkResources(set.Size, set.PartCount);
    }

    /// <summary>
    /// 一个分片集合。对应 Go <c>streamChunkSet</c>。
    /// </summary>
    private sealed class ChunkSet
    {
        /// <summary>构造集合。</summary>
        /// <param name="total">分片总数。</param>
        /// <param name="created">创建时刻。</param>
        public ChunkSet(int total, DateTimeOffset created)
        {
            Total = total;
            Created = created;
            Parts = new Dictionary<int, byte[]>(total);
        }

        /// <summary>分片序号 → 分片载荷；重组后置空。</summary>
        public Dictionary<int, byte[]>? Parts { get; set; }

        /// <summary>分片总数。</summary>
        public int Total { get; }

        /// <summary>当前保留的载荷字节数。</summary>
        public int Size { get; set; }

        /// <summary>当前保留的分片数。</summary>
        public int PartCount { get; set; }

        /// <summary>创建时刻（用于 TTL 回收）。</summary>
        public DateTimeOffset Created { get; }

        /// <summary>重组后的整帧（完成后非空）。</summary>
        public byte[]? Assembled { get; set; }

        /// <summary>完成本集合的分片序号（用于重试匹配）。</summary>
        public int RetryIndex { get; set; }

        /// <summary>完成本集合的分片载荷副本（用于重试匹配）。</summary>
        public byte[] RetryPart { get; set; } = [];
    }
}
