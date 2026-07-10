namespace Wails.Net.Application.Screens;

/// <summary>
/// 表示一个显示屏幕。
/// </summary>
public class Screen
{
    /// <summary>
    /// 屏幕名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 屏幕 X 坐标。
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// 屏幕 Y 坐标。
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// 屏幕宽度。
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 屏幕高度。
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 工作区 X 坐标。
    /// </summary>
    public int WorkAreaX { get; set; }

    /// <summary>
    /// 工作区 Y 坐标。
    /// </summary>
    public int WorkAreaY { get; set; }

    /// <summary>
    /// 工作区宽度。
    /// </summary>
    public int WorkAreaWidth { get; set; }

    /// <summary>
    /// 工作区高度。
    /// </summary>
    public int WorkAreaHeight { get; set; }

    /// <summary>
    /// 缩放比例。
    /// </summary>
    public float ScaleFactor { get; set; }

    /// <summary>
    /// 是否为主屏幕。
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// 缩略图数据，可为 null。
    /// </summary>
    public byte[]? Thumbnail { get; set; }

    /// <summary>
    /// 默认构造函数，所有属性初始化为默认值。
    /// </summary>
    public Screen()
    {
    }

    /// <summary>
    /// 使用所有属性构造屏幕实例。
    /// </summary>
    /// <param name="name">屏幕名称。</param>
    /// <param name="x">屏幕 X 坐标。</param>
    /// <param name="y">屏幕 Y 坐标。</param>
    /// <param name="width">屏幕宽度。</param>
    /// <param name="height">屏幕高度。</param>
    /// <param name="workAreaX">工作区 X 坐标。</param>
    /// <param name="workAreaY">工作区 Y 坐标。</param>
    /// <param name="workAreaWidth">工作区宽度。</param>
    /// <param name="workAreaHeight">工作区高度。</param>
    /// <param name="scaleFactor">缩放比例。</param>
    /// <param name="isPrimary">是否为主屏幕。</param>
    /// <param name="thumbnail">缩略图数据，可为 null。</param>
    public Screen(string name, int x, int y, int width, int height,
        int workAreaX, int workAreaY, int workAreaWidth, int workAreaHeight,
        float scaleFactor, bool isPrimary, byte[]? thumbnail = null)
    {
        Name = name;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        WorkAreaX = workAreaX;
        WorkAreaY = workAreaY;
        WorkAreaWidth = workAreaWidth;
        WorkAreaHeight = workAreaHeight;
        ScaleFactor = scaleFactor;
        IsPrimary = isPrimary;
        Thumbnail = thumbnail;
    }

    /// <summary>
    /// 返回屏幕信息的字符串表示。
    /// </summary>
    /// <returns>屏幕信息字符串。</returns>
    public override string ToString()
    {
        return $"{Name}: {Width}x{Height} @ ({X},{Y}) Scale={ScaleFactor} Primary={IsPrimary}";
    }
}
