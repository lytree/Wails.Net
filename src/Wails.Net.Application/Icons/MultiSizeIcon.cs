namespace Wails.Net.Application.Icons;

/// <summary>
/// 多尺寸图标，表示一个 ICO 文件中的所有图标条目。
/// 对应 Go 版 pkg/icons 中的 MultiSizeIcon 结构。
/// 提供按尺寸查找、添加和遍历图标条目的能力。
/// </summary>
public sealed class MultiSizeIcon
{
    /// <summary>
    /// 图标条目列表，按 ICO 文件中的顺序排列。
    /// </summary>
    private readonly List<IconEntry> _entries;

    /// <summary>
    /// 构造 <see cref="MultiSizeIcon"/> 实例。
    /// </summary>
    /// <param name="entries">初始图标条目列表。</param>
    internal MultiSizeIcon(List<IconEntry> entries)
    {
        _entries = entries ?? new List<IconEntry>();
    }

    /// <summary>
    /// 构造空的 <see cref="MultiSizeIcon"/> 实例。
    /// </summary>
    public MultiSizeIcon()
    {
        _entries = new List<IconEntry>();
    }

    /// <summary>
    /// 获取图标条目数量。
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// 获取所有图标条目的只读集合。
    /// </summary>
    public IReadOnlyList<IconEntry> Entries => _entries;

    /// <summary>
    /// 添加一个图标条目。
    /// </summary>
    /// <param name="entry">要添加的图标条目。</param>
    public void Add(IconEntry entry) => _entries.Add(entry);

    /// <summary>
    /// 添加一个 PNG 图像作为指定尺寸的图标条目。
    /// </summary>
    /// <param name="size">图标尺寸（宽高相同，如 16、32、48、256）。</param>
    /// <param name="pngData">PNG 图像数据。</param>
    /// <param name="bitCount">每像素位数（通常 32）。</param>
    public void AddPng(int size, byte[] pngData, int bitCount = 32)
    {
        _entries.Add(new IconEntry
        {
            Width = size >= 256 ? 0 : size,
            Height = size >= 256 ? 0 : size,
            BitCount = bitCount,
            Data = pngData,
            Format = IconImageFormat.Png
        });
    }

    /// <summary>
    /// 查找最接近指定尺寸的图标条目。
    /// </summary>
    /// <param name="size">目标尺寸（宽高相同）。</param>
    /// <returns>最接近的图标条目，若无条目则返回 null。</returns>
    public IconEntry? FindClosestSize(int size)
    {
        if (_entries.Count == 0)
        {
            return null;
        }

        IconEntry? best = null;
        var bestDiff = int.MaxValue;

        foreach (var entry in _entries)
        {
            var entrySize = Math.Max(entry.ActualWidth, entry.ActualHeight);
            var diff = Math.Abs(entrySize - size);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = entry;
            }
        }

        return best;
    }

    /// <summary>
    /// 查找指定尺寸的图标条目（精确匹配）。
    /// </summary>
    /// <param name="size">目标尺寸。</param>
    /// <returns>匹配的图标条目，若无则返回 null。</returns>
    public IconEntry? FindExactSize(int size)
    {
        foreach (var entry in _entries)
        {
            if (entry.ActualWidth == size && entry.ActualHeight == size)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// 编码为 ICO 文件字节数组。
    /// </summary>
    /// <returns>ICO 文件字节数据。</returns>
    public byte[] ToIcoBytes() => IcoEncoder.Encode(_entries);
}
