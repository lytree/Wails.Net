namespace Wails.Net.Application.Icons;

/// <summary>
/// 表示 ICO 文件中的单个图标条目（一帧图像）。
/// 对应 Go 版 pkg/icons 中 Icon 结构的单个尺寸条目。
/// </summary>
public sealed class IconEntry
{
    /// <summary>
    /// 图标宽度（像素）。0 表示 256。
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 图标高度（像素）。0 表示 256。
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 每像素位数（bpp），如 8、24、32。
    /// </summary>
    public int BitCount { get; set; }

    /// <summary>
    /// 图像数据（PNG 或 BMP 格式）。
    /// </summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 图像格式。
    /// </summary>
    public IconImageFormat Format { get; set; }

    /// <summary>
    /// 实际宽度（将 0 解析为 256）。
    /// </summary>
    public int ActualWidth => Width == 0 ? 256 : Width;

    /// <summary>
    /// 实际高度（将 0 解析为 256）。
    /// </summary>
    public int ActualHeight => Height == 0 ? 256 : Height;
}
