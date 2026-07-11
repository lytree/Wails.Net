namespace Wails.Net.Application.Icons;

/// <summary>
/// 图标图像数据格式。
/// ICO 文件中的图像可以是 PNG（内嵌）或 BMP（含 BITMAPINFOHEADER + 像素数据）格式。
/// </summary>
public enum IconImageFormat
{
    /// <summary>
    /// 未知格式。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// PNG 格式（ICO 文件直接内嵌 PNG 数据）。
    /// </summary>
    Png = 1,

    /// <summary>
    /// BMP 格式（包含 BITMAPINFOHEADER + 像素数据，不含 BITMAPFILEHEADER）。
    /// </summary>
    Bmp = 2
}
