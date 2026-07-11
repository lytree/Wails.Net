using System.Buffers.Binary;

namespace Wails.Net.Application.Icons;

/// <summary>
/// ICO 文件编码器，将多个图标条目编码为 ICO 字节数组。
/// 对应 Go 版 pkg/icons 中的编码逻辑。
/// ICO 文件格式：6 字节 ICONDIR 头 + N × 16 字节 ICONDIRENTRY + 图像数据。
/// </summary>
public static class IcoEncoder
{
    /// <summary>
    /// ICO 文件类型标识。
    /// </summary>
    private const ushort IcoType = 1;

    /// <summary>
    /// ICO 头部固定大小（6 字节）。
    /// </summary>
    private const int HeaderSize = 6;

    /// <summary>
    /// 每个目录条目的大小（16 字节）。
    /// </summary>
    private const int DirectoryEntrySize = 16;

    /// <summary>
    /// 将多个图标条目编码为 ICO 文件字节数组。
    /// </summary>
    /// <param name="entries">图标条目列表。</param>
    /// <returns>ICO 文件字节数据。</returns>
    /// <exception cref="ArgumentNullException">entries 为 null。</exception>
    /// <exception cref="ArgumentException">条目数量超过 65535。</exception>
    public static byte[] Encode(IReadOnlyList<IconEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > ushort.MaxValue)
        {
            throw new ArgumentException($"图标条目数量超过上限 {ushort.MaxValue}。", nameof(entries));
        }

        var count = entries.Count;
        var imageDataOffset = HeaderSize + count * DirectoryEntrySize;

        // 预计算总大小。
        var totalSize = imageDataOffset;
        foreach (var entry in entries)
        {
            totalSize += entry.Data.Length;
        }

        var buffer = new byte[totalSize];
        var span = buffer.AsSpan();

        // 写入 ICONDIR 头部：reserved(2) + type(2) + count(2)。
        BinaryPrimitives.WriteUInt16LittleEndian(span[..2], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(2, 2), IcoType);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), (ushort)count);

        // 写入 ICONDIRENTRY 数组。
        var currentOffset = imageDataOffset;
        for (int i = 0; i < count; i++)
        {
            var entry = entries[i];
            var entrySpan = span.Slice(HeaderSize + i * DirectoryEntrySize, DirectoryEntrySize);

            // width (1 byte, 0=256)
            entrySpan[0] = (byte)(entry.ActualWidth >= 256 ? 0 : entry.ActualWidth);
            // height (1 byte, 0=256)
            entrySpan[1] = (byte)(entry.ActualHeight >= 256 ? 0 : entry.ActualHeight);
            // colorCount (1 byte, 0 for >=8bpp)
            entrySpan[2] = 0;
            // reserved (1 byte)
            entrySpan[3] = 0;
            // planes (2 bytes, 通常为 1)
            BinaryPrimitives.WriteUInt16LittleEndian(entrySpan.Slice(4, 2), 1);
            // bitCount (2 bytes)
            BinaryPrimitives.WriteUInt16LittleEndian(entrySpan.Slice(6, 2), (ushort)entry.BitCount);
            // bytesInRes (4 bytes, 图像数据大小)
            BinaryPrimitives.WriteInt32LittleEndian(entrySpan.Slice(8, 4), entry.Data.Length);
            // imageOffset (4 bytes, 图像数据偏移)
            BinaryPrimitives.WriteInt32LittleEndian(entrySpan.Slice(12, 4), currentOffset);

            currentOffset += entry.Data.Length;
        }

        // 写入图像数据。
        var dataOffsetInBuffer = imageDataOffset;
        for (int i = 0; i < count; i++)
        {
            var entry = entries[i];
            entry.Data.CopyTo(buffer, dataOffsetInBuffer);
            dataOffsetInBuffer += entry.Data.Length;
        }

        return buffer;
    }

    /// <summary>
    /// 将多个 PNG 图像编码为多尺寸 ICO 文件。
    /// </summary>
    /// <param name="pngImages">尺寸到 PNG 数据的映射（如 {16: png16, 32: png32, 256: png256}）。</param>
    /// <param name="bitCount">每像素位数（默认 32）。</param>
    /// <returns>ICO 文件字节数据。</returns>
    public static byte[] EncodeFromPngs(Dictionary<int, byte[]> pngImages, int bitCount = 32)
    {
        ArgumentNullException.ThrowIfNull(pngImages);

        var icon = new MultiSizeIcon();
        foreach (var (size, pngData) in pngImages)
        {
            icon.AddPng(size, pngData, bitCount);
        }

        return icon.ToIcoBytes();
    }

    /// <summary>
    /// 将单个 PNG 图像编码为单尺寸 ICO 文件。
    /// </summary>
    /// <param name="pngData">PNG 图像数据。</param>
    /// <param name="size">图标尺寸。</param>
    /// <param name="bitCount">每像素位数。</param>
    /// <returns>ICO 文件字节数据。</returns>
    public static byte[] EncodeSinglePng(byte[] pngData, int size, int bitCount = 32)
    {
        ArgumentNullException.ThrowIfNull(pngData);

        var icon = new MultiSizeIcon();
        icon.AddPng(size, pngData, bitCount);
        return icon.ToIcoBytes();
    }

    /// <summary>
    /// 将 SVG 图像数据编码为 ICO 文件。
    /// SVG 数据以原始形式嵌入 ICO（Windows 10 1809+ 支持的矢量图标格式）。
    /// 委托给 <see cref="SvgIconConverter.ConvertSvgToIco"/> 完成。
    /// </summary>
    /// <param name="svgData">SVG 图像字节数据。</param>
    /// <returns>ICO 文件字节数据；输入为空则返回空数组。</returns>
    public static byte[] EncodeFromSvg(byte[] svgData)
    {
        ArgumentNullException.ThrowIfNull(svgData);
        return SvgIconConverter.ConvertSvgToIco(svgData);
    }
}
