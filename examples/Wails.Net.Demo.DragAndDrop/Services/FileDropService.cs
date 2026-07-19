using Wails.Net.Application.Bindings;

namespace Wails.Net.Demo.DragAndDrop.Services;

/// <summary>
/// 文件拖放记录，包含路径、文件名、扩展名、大小与拖入时间。
/// </summary>
/// <param name="Path">文件完整路径。</param>
/// <param name="Name">文件名（含扩展名）。</param>
/// <param name="Extension">文件扩展名（含点，如 ".txt"）。</param>
/// <param name="SizeBytes">文件大小（字节），无法获取时为 -1。</param>
/// <param name="DroppedAt">拖入时间戳。</param>
public sealed record FileDropRecord(string Path, string Name, string Extension, long SizeBytes, DateTime DroppedAt);

/// <summary>
/// 文件拖放统计信息。
/// </summary>
/// <param name="TotalCount">累计拖入文件数。</param>
/// <param name="TotalSizeBytes">累计字节数。</param>
/// <param name="DistinctExtensions">去重扩展名列表。</param>
public sealed record FileDropStats(int TotalCount, long TotalSizeBytes, List<string> DistinctExtensions);

/// <summary>
/// 文件拖放服务，记录拖放历史并提供元信息查询。
/// 平台层通过 KnownEvents.WindowFileDropped 事件广播文件路径，
/// 前端订阅事件后将路径列表传入 RecordDrop 绑定方法以持久化历史。
/// </summary>
public sealed class FileDropService
{
    /// <summary>
    /// 拖放历史列表，使用锁保证线程安全。
    /// </summary>
    private readonly List<FileDropRecord> _records = new();

    /// <summary>
    /// 历史记录锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 记录一次拖放操作中的所有文件。
    /// </summary>
    /// <param name="paths">本次拖入的文件路径数组（来自前端事件订阅）。</param>
    /// <returns>本次记录的文件数。</returns>
    [Binding]
    public int RecordDrop(string[] paths)
    {
        if (paths is null || paths.Length == 0)
        {
            return 0;
        }

        var now = DateTime.Now;
        lock (_lock)
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var name = System.IO.Path.GetFileName(path);
                var ext = System.IO.Path.GetExtension(path);
                long size = -1;
                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        size = new System.IO.FileInfo(path).Length;
                    }
                }
                catch
                {
                    // 读取大小失败时保持 -1
                }

                _records.Add(new FileDropRecord(path, name, ext, size, now));
                if (_records.Count > 200)
                {
                    _records.RemoveAt(0);
                }
            }
        }

        return paths.Length;
    }

    /// <summary>
    /// 获取拖放历史记录列表的副本。
    /// </summary>
    /// <returns>文件拖放记录列表。</returns>
    [Binding]
    public List<FileDropRecord> GetHistory()
    {
        lock (_lock)
        {
            return new List<FileDropRecord>(_records);
        }
    }

    /// <summary>
    /// 清空拖放历史记录。
    /// </summary>
    [Binding]
    public void ClearHistory()
    {
        lock (_lock)
        {
            _records.Clear();
        }
    }

    /// <summary>
    /// 获取拖放统计信息：累计文件数、累计字节数、去重扩展名列表。
    /// </summary>
    /// <returns>统计信息对象。</returns>
    [Binding]
    public FileDropStats GetStats()
    {
        lock (_lock)
        {
            var totalCount = _records.Count;
            var totalSize = _records.Where(r => r.SizeBytes > 0).Sum(r => r.SizeBytes);
            var extensions = _records
                .Select(r => r.Extension)
                .Where(e => !string.IsNullOrEmpty(e))
                .Distinct()
                .OrderBy(e => e)
                .ToList();
            return new FileDropStats(totalCount, totalSize, extensions);
        }
    }

    /// <summary>
    /// 读取指定文件的前若干字节并以 Base64 返回（用于预览文本/图片）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="maxBytes">最大读取字节数，默认 1024。</param>
    /// <returns>Base64 编码的文件片段；无法读取时返回空字符串。</returns>
    [Binding]
    public string ReadFilePreview(string path, int maxBytes = 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxBytes <= 0)
        {
            maxBytes = 1024;
        }

        try
        {
            if (!System.IO.File.Exists(path))
            {
                return string.Empty;
            }

            var info = new System.IO.FileInfo(path);
            var read = (int)Math.Min(maxBytes, info.Length);
            using var fs = System.IO.File.OpenRead(path);
            var buffer = new byte[read];
            var actual = fs.Read(buffer, 0, read);
            if (actual < read)
            {
                Array.Resize(ref buffer, actual);
            }
            return Convert.ToBase64String(buffer);
        }
        catch
        {
            return string.Empty;
        }
    }
}
