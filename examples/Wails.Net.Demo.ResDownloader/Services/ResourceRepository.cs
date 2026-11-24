using System.Security.Cryptography;
using System.Text;

namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 已嗅探资源的进程内仓库：按 URL 摘要去重、可控制插入方向、有界容量。
/// </summary>
public sealed class ResourceRepository
{
    private const int MaxItems = 5000;

    private readonly object _gate = new();
    private readonly List<ResourceItem> _items = [];
    private readonly HashSet<string> _signs = new(StringComparer.Ordinal);
    private long _nextId;

    /// <summary>当前资源数量。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    /// 新增一条资源；URL 重复时返回 <c>null</c>。
    /// </summary>
    /// <param name="item">资源（Id / UrlSign / CreatedAt 由本方法填充）。</param>
    /// <param name="insertTail">为 <c>true</c> 追加到尾部，否则插入头部。</param>
    /// <returns>入库后的资源快照；重复返回 <c>null</c>。</returns>
    public ResourceItem? Add(ResourceItem item, bool insertTail)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (_gate)
        {
            var sign = string.IsNullOrEmpty(item.UrlSign) ? ComputeSign(item.Url) : item.UrlSign;
            if (!_signs.Add(sign))
            {
                return null;
            }

            item.UrlSign = sign;
            item.Id = string.IsNullOrEmpty(item.Id)
                ? Interlocked.Increment(ref _nextId).ToString()
                : item.Id;
            item.CreatedAt = string.IsNullOrEmpty(item.CreatedAt)
                ? DateTime.Now.ToString("HH:mm:ss")
                : item.CreatedAt;

            if (insertTail)
            {
                _items.Add(item);
            }
            else
            {
                _items.Insert(0, item);
            }

            while (_items.Count > MaxItems)
            {
                var removed = insertTail ? _items[0] : _items[^1];
                _items.Remove(removed);
                _signs.Remove(removed.UrlSign);
            }

            return item.Clone();
        }
    }

    /// <summary>
    /// 批量导入资源（重复 URL 跳过）。
    /// </summary>
    /// <param name="items">待导入资源。</param>
    /// <param name="insertTail">是否追加到尾部。</param>
    /// <returns>实际导入的资源列表。</returns>
    public List<ResourceItem> Import(IEnumerable<ResourceItem> items, bool insertTail)
    {
        var added = new List<ResourceItem>();
        foreach (var item in items)
        {
            // 导入时重新生成 Id，避免与已有条目冲突
            item.Id = string.Empty;
            item.Progress = 0;
            item.Message = string.Empty;

            // 已完成 / 失败的历史记录保留，运行中与排队中的状态回落为就绪
            if (string.IsNullOrEmpty(item.Status)
                || item.Status is ResourceStatus.Running or ResourceStatus.Pending)
            {
                item.Status = ResourceStatus.Ready;
                item.SavePath = string.Empty;
            }

            var result = Add(item, insertTail);
            if (result is not null)
            {
                added.Add(result);
            }
        }

        return added;
    }

    /// <summary>获取资源快照（按当前列表顺序）。</summary>
    /// <returns>资源副本列表。</returns>
    public List<ResourceItem> Snapshot()
    {
        lock (_gate)
        {
            return _items.Select(item => item.Clone()).ToList();
        }
    }

    /// <summary>按标识查找资源快照。</summary>
    /// <param name="id">资源标识。</param>
    /// <returns>资源副本；不存在返回 <c>null</c>。</returns>
    public ResourceItem? Get(string id)
    {
        lock (_gate)
        {
            return _items.Find(item => item.Id == id)?.Clone();
        }
    }

    /// <summary>
    /// 更新一条资源。
    /// </summary>
    /// <param name="id">资源标识。</param>
    /// <param name="mutate">修改委托。</param>
    /// <returns>更新后的资源副本；不存在返回 <c>null</c>。</returns>
    public ResourceItem? Update(string id, Action<ResourceItem> mutate)
    {
        lock (_gate)
        {
            var item = _items.Find(entry => entry.Id == id);
            if (item is null)
            {
                return null;
            }

            mutate(item);
            return item.Clone();
        }
    }

    /// <summary>
    /// 删除一条资源（同时释放 URL 去重占位，便于重新嗅探）。
    /// </summary>
    /// <param name="id">资源标识。</param>
    /// <returns>删除成功返回 <c>true</c>。</returns>
    public bool Remove(string id)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return false;
            }

            _signs.Remove(_items[index].UrlSign);
            _items.RemoveAt(index);
            return true;
        }
    }

    /// <summary>
    /// 按 URL 摘要批量删除。
    /// </summary>
    /// <param name="signs">URL 摘要集合。</param>
    /// <returns>实际删除数量。</returns>
    public int RemoveBySign(IReadOnlyCollection<string> signs)
    {
        if (signs.Count == 0)
        {
            return 0;
        }

        lock (_gate)
        {
            var set = new HashSet<string>(signs, StringComparer.Ordinal);
            var removed = _items.RemoveAll(item => set.Contains(item.UrlSign));
            foreach (var sign in set)
            {
                _signs.Remove(sign);
            }

            return removed;
        }
    }

    /// <summary>
    /// 清空列表。
    /// </summary>
    /// <param name="keepActive">是否保留下载中 / 排队中的资源。</param>
    /// <returns>被移除的资源摘要（用于前端同步）。</returns>
    public List<string> Clear(bool keepActive)
    {
        lock (_gate)
        {
            if (!keepActive)
            {
                var all = _items.Select(item => item.Id).ToList();
                _items.Clear();
                _signs.Clear();
                return all;
            }

            var removed = new List<string>();
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                if (item.Status is ResourceStatus.Running or ResourceStatus.Pending)
                {
                    continue;
                }

                removed.Add(item.Id);
                _signs.Remove(item.UrlSign);
                _items.RemoveAt(i);
            }

            return removed;
        }
    }

    /// <summary>
    /// 计算 URL 的 MD5 摘要（与 res-downloader 的 UrlSign 一致）。
    /// </summary>
    /// <param name="url">资源直链。</param>
    /// <returns>32 位小写十六进制摘要。</returns>
    public static string ComputeSign(string url)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(url ?? string.Empty));
        return Convert.ToHexStringLower(bytes);
    }
}
