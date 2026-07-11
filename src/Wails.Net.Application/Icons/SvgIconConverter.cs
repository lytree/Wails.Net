using System.Text;

namespace Wails.Net.Application.Icons;

/// <summary>
/// SVG 图标转换器，提供 SVG 格式检测和 ICO 封装支持。
/// 对应 Wails v3 Go 版本 pkg/icons 中的 SVG 处理。
/// Windows 10 1809+ 支持 ICO 文件中嵌入 SVG 数据。
/// </summary>
public static class SvgIconConverter
{
    /// <summary>
    /// SVG MIME 类型常量。
    /// </summary>
    public const string SvgMimeType = "image/svg+xml";

    /// <summary>
    /// ICO 头部固定大小（6 字节）。
    /// </summary>
    private const int HeaderSize = 6;

    /// <summary>
    /// 单个目录条目的大小（16 字节）。
    /// </summary>
    private const int DirectoryEntrySize = 16;

    /// <summary>
    /// SVG 数据在 ICO 中的起始偏移：6 字节头 + 16 字节目录条目。
    /// </summary>
    private const int SvgDataOffset = HeaderSize + DirectoryEntrySize;

    /// <summary>
    /// 检测字节数据是否为 SVG 格式。
    /// 通过检查数据开头是否包含 XML 声明或 &lt;svg&gt; 标签判断。
    /// </summary>
    /// <param name="data">图像字节数据。</param>
    /// <returns>如果是 SVG 返回 true，否则返回 false。</returns>
    public static bool IsSvg(byte[] data)
    {
        if (data is null || data.Length < 5)
        {
            return false;
        }

        // 检查 XML 声明或 SVG 标签
        var header = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 512));
        return header.Contains("<svg") || header.Contains("<?xml");
    }

    /// <summary>
    /// 将 SVG 字节数据封装为 ICO 文件格式。
    /// Windows 10 1809+ 支持 ICO 中嵌入 SVG 矢量数据。
    /// 生成的 ICO 文件包含单条目目录，数据部分为原始 SVG 字节。
    /// </summary>
    /// <param name="svgData">SVG 图像字节数据。</param>
    /// <returns>ICO 文件字节数据；若输入为空则返回空数组。</returns>
    public static byte[] ConvertSvgToIco(byte[] svgData)
    {
        if (svgData is null || svgData.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // ICO 文件头（6 字节）
        writer.Write((ushort)0);  // 保留字段
        writer.Write((ushort)1);  // 类型：1 = ICO
        writer.Write((ushort)1);  // 图像数量：1

        // 目录条目（16 字节）
        writer.Write((byte)0);    // 宽度：0 表示 256
        writer.Write((byte)0);    // 高度：0 表示 256
        writer.Write((byte)0);    // 颜色数：0
        writer.Write((byte)0);    // 保留
        writer.Write((ushort)1);  // 色彩平面数
        writer.Write((ushort)32); // 每像素位数
        writer.Write((uint)svgData.Length);  // 图像数据大小
        writer.Write((uint)SvgDataOffset);   // 图像数据偏移：6 + 16 = 22

        // SVG 图像数据
        writer.Write(svgData);

        return ms.ToArray();
    }

    /// <summary>
    /// 从 SVG 文本提取 viewBox 或 width/height 属性值。
    /// 优先使用 width/height 属性，若不存在则回退到 viewBox。
    /// </summary>
    /// <param name="svgText">SVG 文本内容。</param>
    /// <returns>宽度和高度的元组，解析失败返回 (0, 0)。</returns>
    public static (int Width, int Height) GetSvgDimensions(string svgText)
    {
        if (string.IsNullOrEmpty(svgText))
        {
            return (0, 0);
        }

        var width = ExtractAttribute(svgText, "width");
        var height = ExtractAttribute(svgText, "height");

        if (width > 0 && height > 0)
        {
            return (width, height);
        }

        // 尝试 viewBox
        return ExtractViewBox(svgText);
    }

    /// <summary>
    /// 从 SVG 文本中提取指定属性的整数值。
    /// 支持 "100"、"100px"、"100pt" 等带单位的形式。
    /// </summary>
    /// <param name="svgText">SVG 文本内容。</param>
    /// <param name="attributeName">属性名（如 width、height）。</param>
    /// <returns>属性整数值；解析失败返回 0。</returns>
    private static int ExtractAttribute(string svgText, string attributeName)
    {
        var pattern = attributeName + "=\"";
        var index = svgText.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return 0;
        }

        index += pattern.Length;
        var endIndex = svgText.IndexOf('"', index);
        if (endIndex < 0)
        {
            return 0;
        }

        var valueStr = svgText.AsSpan(index, endIndex - index);
        return int.TryParse(GetLeadingInteger(valueStr), out var result) ? result : 0;
    }

    /// <summary>
    /// 从 span 中提取前导整数部分（含可选负号）。
    /// 例如 "100px" 提取 "100"，"-5.5" 提取 "-5"。
    /// </summary>
    /// <param name="span">输入字符范围。</param>
    /// <returns>前导整数字符串。</returns>
    private static string GetLeadingInteger(ReadOnlySpan<char> span)
    {
        var sb = new StringBuilder();
        foreach (var c in span)
        {
            if (char.IsDigit(c) || (sb.Length == 0 && c == '-'))
            {
                sb.Append(c);
            }
            else
            {
                break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 从 SVG 文本中提取 viewBox 属性值。
    /// viewBox 格式为 "minX minY width height"。
    /// </summary>
    /// <param name="svgText">SVG 文本内容。</param>
    /// <returns>宽度和高度的元组；解析失败返回 (0, 0)。</returns>
    private static (int Width, int Height) ExtractViewBox(string svgText)
    {
        var pattern = "viewBox=\"";
        var index = svgText.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return (0, 0);
        }

        index += pattern.Length;
        var endIndex = svgText.IndexOf('"', index);
        if (endIndex < 0)
        {
            return (0, 0);
        }

        var viewBoxStr = svgText.Substring(index, endIndex - index);
        var parts = viewBoxStr.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 &&
            int.TryParse(GetLeadingInteger(parts[2].AsSpan()), out var width) &&
            int.TryParse(GetLeadingInteger(parts[3].AsSpan()), out var height))
        {
            return (width, height);
        }

        return (0, 0);
    }
}
