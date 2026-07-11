using System.Buffers.Binary;

namespace Wails.Net.Application.Icons;

/// <summary>
/// ICO 文件解码器，将 ICO 字节数组解析为 <see cref="MultiSizeIcon"/>。
/// 对应 Go 版 pkg/icons 中的解码逻辑。
/// ICO 文件格式：6 字节 ICONDIR 头 + N × 16 字节 ICONDIRENTRY + 图像数据。
/// 图像数据可以是 PNG（直接内嵌）或 BMP（含 BITMAPINFOHEADER + 像素数据）。
/// </summary>
public static class IcoDecoder
{
    /// <summary>
    /// PNG 文件签名（8 字节），用于判断图像数据是否为 PNG 格式。
    /// </summary>
    private static ReadOnlySpan<byte> PngSignature => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// ICO 文件类型标识（1 = ICO，2 = CUR）。
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
    /// 解码 ICO 字节数据为 <see cref="MultiSizeIcon"/>。
    /// </summary>
    /// <param name="icoData">ICO 文件字节数据。</param>
    /// <returns>包含所有尺寸的 <see cref="MultiSizeIcon"/>。</returns>
    /// <exception cref="ArgumentNullException">icoData 为 null。</exception>
    /// <exception cref="ArgumentException">数据长度不足或类型不是 ICO。</exception>
    public static MultiSizeIcon Decode(byte[] icoData)
    {
        ArgumentNullException.ThrowIfNull(icoData);
        return Decode(icoData.AsSpan());
    }

    /// <summary>
    /// 解码 ICO 字节范围为 <see cref="MultiSizeIcon"/>。
    /// </summary>
    /// <param name="icoData">ICO 文件字节范围。</param>
    /// <returns>包含所有尺寸的 <see cref="MultiSizeIcon"/>。</returns>
    /// <exception cref="ArgumentException">数据长度不足或类型不是 ICO。</exception>
    public static MultiSizeIcon Decode(ReadOnlySpan<byte> icoData)
    {
        if (icoData.Length < HeaderSize)
        {
            throw new ArgumentException("ICO 数据长度不足，至少需要 6 字节头部。", nameof(icoData));
        }

        // 读取 ICONDIR 头部：reserved(2) + type(2) + count(2)。
        var type = BinaryPrimitives.ReadUInt16LittleEndian(icoData.Slice(2, 2));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(icoData.Slice(4, 2));

        if (type != IcoType)
        {
            throw new ArgumentException($"不支持的图标类型：{type}（仅支持 ICO 类型 {IcoType}）。", nameof(icoData));
        }

        var entries = new List<IconEntry>(count);

        for (int i = 0; i < count; i++)
        {
            var entryOffset = HeaderSize + i * DirectoryEntrySize;
            if (entryOffset + DirectoryEntrySize > icoData.Length)
            {
                break;
            }

            var entrySpan = icoData.Slice(entryOffset, DirectoryEntrySize);
            var entry = ParseDirectoryEntry(entrySpan, icoData);
            entries.Add(entry);
        }

        return new MultiSizeIcon(entries);
    }

    /// <summary>
    /// 解析单个 ICONDIRENTRY（16 字节）并提取图像数据。
    /// ICONDIRENTRY 布局：
    /// offset 0: width (1 byte, 0=256)
    /// offset 1: height (1 byte, 0=256)
    /// offset 2: colorCount (1 byte)
    /// offset 3: reserved (1 byte)
    /// offset 4-5: planes (2 bytes)
    /// offset 6-7: bitCount (2 bytes)
    /// offset 8-11: bytesInRes (4 bytes, 图像数据大小)
    /// offset 12-15: imageOffset (4 bytes, 图像数据偏移)
    /// </summary>
    /// <param name="entrySpan">16 字节目录条目范围。</param>
    /// <param name="fullData">完整 ICO 数据，用于按偏移读取图像。</param>
    /// <returns>解析后的 <see cref="IconEntry"/>。</returns>
    private static IconEntry ParseDirectoryEntry(ReadOnlySpan<byte> entrySpan, ReadOnlySpan<byte> fullData)
    {
        var width = entrySpan[0];
        var height = entrySpan[1];
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(entrySpan.Slice(6, 2));
        var bytesInRes = BinaryPrimitives.ReadInt32LittleEndian(entrySpan.Slice(8, 4));
        var imageOffset = BinaryPrimitives.ReadInt32LittleEndian(entrySpan.Slice(12, 4));

        // 提取图像数据。
        if (imageOffset < 0 || bytesInRes <= 0 || imageOffset + bytesInRes > fullData.Length)
        {
            return new IconEntry
            {
                Width = width,
                Height = height,
                BitCount = bitCount,
                Data = Array.Empty<byte>(),
                Format = IconImageFormat.Unknown
            };
        }

        var imageData = fullData.Slice(imageOffset, bytesInRes).ToArray();
        var format = DetectFormat(imageData);

        return new IconEntry
        {
            Width = width,
            Height = height,
            BitCount = bitCount,
            Data = imageData,
            Format = format
        };
    }

    /// <summary>
    /// 检测图像数据格式（PNG、BMP 或 SVG）。
    /// PNG 数据以 8 字节签名开头；BMP 数据以 BITMAPINFOHEADER 大小（通常 40）开头；
    /// SVG 数据以 XML 声明或 &lt;svg&gt; 标签开头（Windows 10 1809+）。
    /// </summary>
    /// <param name="data">图像数据。</param>
    /// <returns>图像格式。</returns>
    private static IconImageFormat DetectFormat(byte[] data)
    {
        if (data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(PngSignature))
        {
            return IconImageFormat.Png;
        }

        if (SvgIconConverter.IsSvg(data))
        {
            return IconImageFormat.Svg;
        }

        if (data.Length >= 4)
        {
            var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, 4));
            if (headerSize is 40 or 108 or 124)
            {
                return IconImageFormat.Bmp;
            }
        }

        return IconImageFormat.Unknown;
    }
}
