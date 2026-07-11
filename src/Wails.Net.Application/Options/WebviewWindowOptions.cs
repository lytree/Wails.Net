using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Options;

/// <summary>
/// Webview 窗口配置选项。
/// </summary>
public class WebviewWindowOptions
{
    /// <summary>
    /// 窗口名称。
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 窗口标题。
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// 窗口宽度。
    /// </summary>
    public int Width { get; set; } = 800;

    /// <summary>
    /// 窗口高度。
    /// </summary>
    public int Height { get; set; } = 600;

    /// <summary>
    /// 最小宽度。
    /// </summary>
    public int MinWidth { get; set; } = 0;

    /// <summary>
    /// 最小高度。
    /// </summary>
    public int MinHeight { get; set; } = 0;

    /// <summary>
    /// 最大宽度。
    /// </summary>
    public int MaxWidth { get; set; } = 0;

    /// <summary>
    /// 最大高度。
    /// </summary>
    public int MaxHeight { get; set; } = 0;

    /// <summary>
    /// 窗口 X 坐标，-1 表示居中默认。
    /// </summary>
    public int X { get; set; } = -1;

    /// <summary>
    /// 窗口 Y 坐标，-1 表示居中默认。
    /// </summary>
    public int Y { get; set; } = -1;

    /// <summary>
    /// 是否无边框。
    /// </summary>
    public bool Frameless { get; set; } = false;

    /// <summary>
    /// 是否总置顶。
    /// </summary>
    public bool AlwaysOnTop { get; set; } = false;

    /// <summary>
    /// 是否隐藏。
    /// </summary>
    public bool Hidden { get; set; } = false;

    /// <summary>
    /// 是否可调整大小。
    /// </summary>
    public bool Resizable { get; set; } = true;

    /// <summary>
    /// 是否可最大化。
    /// </summary>
    public bool Maximisable { get; set; } = true;

    /// <summary>
    /// 是否可最小化。
    /// </summary>
    public bool Minimisable { get; set; } = true;

    /// <summary>
    /// 是否可关闭。
    /// </summary>
    public bool Closable { get; set; } = true;

    /// <summary>
    /// 是否全屏。
    /// </summary>
    public bool Fullscreen { get; set; } = false;

    /// <summary>
    /// 是否有阴影。
    /// </summary>
    public bool HasShadow { get; set; } = true;

    /// <summary>
    /// 背景色红色分量。
    /// </summary>
    public byte R { get; set; } = 255;

    /// <summary>
    /// 背景色绿色分量。
    /// </summary>
    public byte G { get; set; } = 255;

    /// <summary>
    /// 背景色蓝色分量。
    /// </summary>
    public byte B { get; set; } = 255;

    /// <summary>
    /// 背景色透明度分量。
    /// </summary>
    public byte A { get; set; } = 255;

    /// <summary>
    /// 窗口加载的 URL。
    /// </summary>
    public string URL { get; set; } = "";

    /// <summary>
    /// 窗口加载的 HTML 内容，可为 null。
    /// </summary>
    public string? HTML { get; set; }

    /// <summary>
    /// 注入的 JavaScript 代码，可为 null。
    /// </summary>
    public string? JS { get; set; }

    /// <summary>
    /// 注入的 CSS 样式，可为 null。
    /// </summary>
    public string? CSS { get; set; }

    /// <summary>
    /// 是否启用拖放功能。
    /// </summary>
    public bool EnableDragAndDrop { get; set; } = false;

    /// <summary>
    /// 是否显示菜单栏。
    /// </summary>
    public bool ShowMenuBar { get; set; } = false;

    /// <summary>
    /// 关闭窗口时是否隐藏而非销毁。
    /// </summary>
    public bool HideWindowOnClose { get; set; } = false;

    /// <summary>
    /// 窗口图标字节数据，可为 null。
    /// </summary>
    public byte[]? Icon { get; set; }

    /// <summary>
    /// 标题栏样式。
    /// </summary>
    public TitleBarStyle TitleBar { get; set; } = Wails.Net.Application.Windows.TitleBarStyle.Default;

    /// <summary>
    /// Windows 平台特定选项，暂用 object 类型。
    /// </summary>
    public object? Windows { get; set; }

    /// <summary>
    /// Linux 平台特定选项，暂用 object 类型。
    /// </summary>
    public object? Linux { get; set; }

    /// <summary>
    /// 是否居中显示。
    /// </summary>
    public bool Centered { get; set; } = false;

    /// <summary>
    /// 背景色 RGBA 元组，可为 null 表示使用默认值。
    /// </summary>
    public (byte R, byte G, byte B, byte A)? BackgroundColour { get; set; }

    /// <summary>
    /// 背景类型，可选值为 "transparent"、"translucent" 或 "solid"，可为 null。
    /// </summary>
    public string? BackgroundType { get; set; }

    /// <summary>
    /// 是否半透明。
    /// </summary>
    public bool Translucent { get; set; } = false;

    /// <summary>
    /// 标题栏样式字符串（如 "hidden"、"hiddenInset"、"unified"），可为 null。
    /// </summary>
    public string? TitleBarStyle { get; set; }

    /// <summary>
    /// 全屏按钮是否可用。
    /// </summary>
    public bool FullscreenButtonEnabled { get; set; } = true;

    /// <summary>
    /// 启动时是否最小化。
    /// </summary>
    public bool Minimised { get; set; } = false;

    /// <summary>
    /// 启动时是否最大化。
    /// </summary>
    public bool Maximised { get; set; } = false;

    /// <summary>
    /// 是否启用缩放。
    /// </summary>
    public bool ZoomEnabled { get; set; } = true;

    /// <summary>
    /// 初始缩放比例。
    /// </summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>
    /// 是否禁用右键菜单。
    /// </summary>
    public bool DisableContextMenu { get; set; } = false;

    /// <summary>
    /// 是否显示开发者工具。
    /// </summary>
    public bool ShowDevmodeEnabled { get; set; } = false;
}
