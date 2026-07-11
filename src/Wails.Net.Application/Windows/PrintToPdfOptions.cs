namespace Wails.Net.Application.Windows;

/// <summary>
/// PDF 导出选项，对应 Tauri v2 的 printToPDF 选项和 Chromium 的打印设置。
/// </summary>
public class PrintToPdfOptions
{
    /// <summary>
    /// 获取或设置是否横向打印。默认为 false（纵向）。
    /// </summary>
    public bool Landscape { get; set; }

    /// <summary>
    /// 获取或设置是否打印背景图形。默认为 false。
    /// </summary>
    public bool PrintBackground { get; set; } = true;

    /// <summary>
    /// 获取或设置缩放比例（0.1 - 2.0）。默认为 1.0。
    /// </summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>
    /// 获取或设置纸张尺寸字符串（如 "A4"、"Letter"、"Legal"）。默认为 "Letter"。
    /// </summary>
    public string PaperSize { get; set; } = "Letter";

    /// <summary>
    /// 获取或设置页边距（点），为 null 时使用默认边距。
    /// </summary>
    public PrintToPdfMargins? Margins { get; set; }

    /// <summary>
    /// 获取或设置是否首选 CSS 页面尺寸。默认为 false。
    /// </summary>
    public bool PreferCssPageSize { get; set; }

    /// <summary>
    /// 获取或设置页眉模板 HTML。为 null 或空时不打印页眉。
    /// </summary>
    public string? HeaderTemplate { get; set; }

    /// <summary>
    /// 获取或设置页脚模板 HTML。为 null 或空时不打印页脚。
    /// </summary>
    public string? FooterTemplate { get; set; }

    /// <summary>
    /// 获取或设置是否显示页眉页脚。默认为 false。
    /// </summary>
    public bool DisplayHeaderFooter { get; set; }
}

/// <summary>
/// PDF 导出页边距（单位：点，1 点 = 1/72 英寸）。
/// </summary>
public class PrintToPdfMargins
{
    /// <summary>上边距（点）。</summary>
    public double Top { get; set; } = 36;

    /// <summary>下边距（点）。</summary>
    public double Bottom { get; set; } = 36;

    /// <summary>左边距（点）。</summary>
    public double Left { get; set; } = 36;

    /// <summary>右边距（点）。</summary>
    public double Right { get; set; } = 36;
}
