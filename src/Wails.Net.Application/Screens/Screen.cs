namespace Wails.Net.Application.Screens;

/// <summary>
/// 表示一个显示屏幕。
/// 对应 Wails v3 Go 版本 screenmanager.go 中的 Screen 结构。
/// </summary>
public class Screen
{
    /// <summary>
    /// 屏幕唯一标识符。
    /// 对应 Wails v3 Go 版本 <c>Screen.ID</c>。
    /// <para>
    /// 由平台填充：Windows 使用 <c>DeviceName</c>，Linux 使用 GdkMonitor 的连接名，
    /// Android 使用 DisplayId 字符串。用于 ScreenManager 多屏幕布局算法的去重和映射。
    /// </para>
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 屏幕名称。
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 屏幕 X 坐标（DIP 逻辑像素）。
    /// </summary>
    public int X { get; set; }

    /// <summary>
    /// 屏幕 Y 坐标（DIP 逻辑像素）。
    /// </summary>
    public int Y { get; set; }

    /// <summary>
    /// 屏幕宽度（DIP 逻辑像素）。
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 屏幕高度（DIP 逻辑像素）。
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 工作区 X 坐标（DIP 逻辑像素）。
    /// </summary>
    public int WorkAreaX { get; set; }

    /// <summary>
    /// 工作区 Y 坐标（DIP 逻辑像素）。
    /// </summary>
    public int WorkAreaY { get; set; }

    /// <summary>
    /// 工作区宽度（DIP 逻辑像素）。
    /// </summary>
    public int WorkAreaWidth { get; set; }

    /// <summary>
    /// 工作区高度（DIP 逻辑像素）。
    /// </summary>
    public int WorkAreaHeight { get; set; }

    /// <summary>
    /// 屏幕 X 坐标（物理像素）。
    /// 对应 Wails v3 Go 版本 <c>Screen.PhysicalBounds.X</c>。
    /// 默认 0 表示与逻辑坐标一致（未提供物理坐标的平台）。
    /// </summary>
    public int PhysicalX { get; set; }

    /// <summary>
    /// 屏幕 Y 坐标（物理像素）。
    /// </summary>
    public int PhysicalY { get; set; }

    /// <summary>
    /// 屏幕宽度（物理像素）。
    /// </summary>
    public int PhysicalWidth { get; set; }

    /// <summary>
    /// 屏幕高度（物理像素）。
    /// </summary>
    public int PhysicalHeight { get; set; }

    /// <summary>
    /// 工作区 X 坐标（物理像素）。
    /// </summary>
    public int PhysicalWorkAreaX { get; set; }

    /// <summary>
    /// 工作区 Y 坐标（物理像素）。
    /// </summary>
    public int PhysicalWorkAreaY { get; set; }

    /// <summary>
    /// 工作区宽度（物理像素）。
    /// </summary>
    public int PhysicalWorkAreaWidth { get; set; }

    /// <summary>
    /// 工作区高度（物理像素）。
    /// </summary>
    public int PhysicalWorkAreaHeight { get; set; }

    /// <summary>
    /// 缩放比例（DPI / 96）。
    /// </summary>
    public float ScaleFactor { get; set; }

    /// <summary>
    /// 是否为主屏幕。
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// 屏幕旋转角度（度数）。
    /// 对应 Wails v3 Go 版本 <c>Screen.Rotation</c>。
    /// <para>
    /// 常见值：0（正常）、90（顺时针 90°）、180（倒置）、270（逆时针 90°）。
    /// 由平台填充，未提供旋转信息的平台返回 0。
    /// </para>
    /// </summary>
    public float Rotation { get; set; }

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
    /// <param name="x">屏幕 X 坐标（DIP）。</param>
    /// <param name="y">屏幕 Y 坐标（DIP）。</param>
    /// <param name="width">屏幕宽度（DIP）。</param>
    /// <param name="height">屏幕高度（DIP）。</param>
    /// <param name="workAreaX">工作区 X 坐标（DIP）。</param>
    /// <param name="workAreaY">工作区 Y 坐标（DIP）。</param>
    /// <param name="workAreaWidth">工作区宽度（DIP）。</param>
    /// <param name="workAreaHeight">工作区高度（DIP）。</param>
    /// <param name="scaleFactor">缩放比例。</param>
    /// <param name="isPrimary">是否为主屏幕。</param>
    /// <param name="thumbnail">缩略图数据，可为 null。</param>
    public Screen(string name, int x, int y, int width, int height,
        int workAreaX, int workAreaY, int workAreaWidth, int workAreaHeight,
        float scaleFactor, bool isPrimary, byte[]? thumbnail = null)
        : this(id: name, name, x, y, width, height,
            workAreaX, workAreaY, workAreaWidth, workAreaHeight,
            scaleFactor, isPrimary, rotation: 0, thumbnail)
    {
    }

    /// <summary>
    /// 使用所有属性（含 ID 和旋转角度）构造屏幕实例。
    /// 对应 Wails v3 Go 版本 <c>Screen</c> 结构的完整字段。
    /// </summary>
    /// <param name="id">屏幕唯一标识符。</param>
    /// <param name="name">屏幕名称。</param>
    /// <param name="x">屏幕 X 坐标（DIP）。</param>
    /// <param name="y">屏幕 Y 坐标（DIP）。</param>
    /// <param name="width">屏幕宽度（DIP）。</param>
    /// <param name="height">屏幕高度（DIP）。</param>
    /// <param name="workAreaX">工作区 X 坐标（DIP）。</param>
    /// <param name="workAreaY">工作区 Y 坐标（DIP）。</param>
    /// <param name="workAreaWidth">工作区宽度（DIP）。</param>
    /// <param name="workAreaHeight">工作区高度（DIP）。</param>
    /// <param name="scaleFactor">缩放比例。</param>
    /// <param name="isPrimary">是否为主屏幕。</param>
    /// <param name="rotation">屏幕旋转角度（度数），默认 0。</param>
    /// <param name="thumbnail">缩略图数据，可为 null。</param>
    public Screen(string id, string name, int x, int y, int width, int height,
        int workAreaX, int workAreaY, int workAreaWidth, int workAreaHeight,
        float scaleFactor, bool isPrimary, float rotation = 0, byte[]? thumbnail = null)
    {
        Id = id;
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
        Rotation = rotation;
        Thumbnail = thumbnail;
    }

    /// <summary>
    /// 获取屏幕逻辑边界（DIP）。
    /// </summary>
    public Rect Bounds => new(X, Y, Width, Height);

    /// <summary>
    /// 获取屏幕物理边界。
    /// </summary>
    public Rect PhysicalBounds => new(PhysicalX, PhysicalY, PhysicalWidth, PhysicalHeight);

    /// <summary>
    /// 获取工作区逻辑边界（DIP）。
    /// </summary>
    public Rect WorkArea => new(WorkAreaX, WorkAreaY, WorkAreaWidth, WorkAreaHeight);

    /// <summary>
    /// 获取工作区物理边界。
    /// </summary>
    public Rect PhysicalWorkArea => new(PhysicalWorkAreaX, PhysicalWorkAreaY, PhysicalWorkAreaWidth, PhysicalWorkAreaHeight);

    /// <summary>
    /// 在 DIP 和物理像素之间缩放单个值。
    /// 对应 Wails v3 Go 版本 <c>Screen.scale</c>。
    /// <para>
    /// 采用混合舍入策略：从物理到 DIP（除法）时向上取整，从 DIP 到物理（乘法）时向下取整，
    /// 多次往返转换时减少漂移并提升精度。
    /// </para>
    /// </summary>
    /// <param name="value">要缩放的值。</param>
    /// <param name="toDip">true 表示从物理像素缩放到 DIP，false 表示从 DIP 缩放到物理像素。</param>
    /// <returns>缩放后的值。</returns>
    public int Scale(int value, bool toDip)
    {
        var factor = ScaleFactor <= 0 ? 1f : ScaleFactor;
        if (toDip)
        {
            return (int)Math.Ceiling(value / factor);
        }

        return (int)Math.Floor(value * factor);
    }

    /// <summary>
    /// 将 DIP 坐标点转换为物理像素坐标点。
    /// 对应 Wails v3 Go 版本 <c>Screen.dipToPhysicalPoint</c>。
    /// </summary>
    /// <param name="dipPoint">DIP 坐标点。</param>
    /// <returns>物理像素坐标点。</returns>
    public Point DipToPhysicalPoint(Point dipPoint)
    {
        var relativeX = dipPoint.X - X;
        var relativeY = dipPoint.Y - Y;
        var scaledX = Scale(relativeX, toDip: false) + PhysicalX;
        var scaledY = Scale(relativeY, toDip: false) + PhysicalY;
        return new Point(scaledX, scaledY);
    }

    /// <summary>
    /// 将物理像素坐标点转换为 DIP 坐标点。
    /// 对应 Wails v3 Go 版本 <c>Screen.physicalToDipPoint</c>。
    /// </summary>
    /// <param name="physicalPoint">物理像素坐标点。</param>
    /// <returns>DIP 坐标点。</returns>
    public Point PhysicalToDipPoint(Point physicalPoint)
    {
        var relativeX = physicalPoint.X - PhysicalX;
        var relativeY = physicalPoint.Y - PhysicalY;
        var scaledX = Scale(relativeX, toDip: true) + X;
        var scaledY = Scale(relativeY, toDip: true) + Y;
        return new Point(scaledX, scaledY);
    }

    /// <summary>
    /// 将 DIP 矩形转换为物理像素矩形。
    /// 对应 Wails v3 Go 版本 <c>Screen.dipToPhysicalRect</c>。
    /// </summary>
    /// <param name="dipRect">DIP 矩形。</param>
    /// <returns>物理像素矩形。</returns>
    public Rect DipToPhysicalRect(Rect dipRect)
    {
        var origin = DipToPhysicalPoint(new Point(dipRect.X, dipRect.Y));
        var corner = DipToPhysicalPoint(new Point(dipRect.X + dipRect.Width, dipRect.Y + dipRect.Height));
        return new Rect(origin.X, origin.Y, corner.X - origin.X, corner.Y - origin.Y);
    }

    /// <summary>
    /// 将物理像素矩形转换为 DIP 矩形。
    /// 对应 Wails v3 Go 版本 <c>Screen.physicalToDipRect</c>。
    /// </summary>
    /// <param name="physicalRect">物理像素矩形。</param>
    /// <returns>DIP 矩形。</returns>
    public Rect PhysicalToDipRect(Rect physicalRect)
    {
        var origin = PhysicalToDipPoint(new Point(physicalRect.X, physicalRect.Y));
        var corner = PhysicalToDipPoint(new Point(physicalRect.X + physicalRect.Width, physicalRect.Y + physicalRect.Height));
        return new Rect(origin.X, origin.Y, corner.X - origin.X, corner.Y - origin.Y);
    }

    /// <summary>
    /// 判断指定 DIP 点是否在屏幕边界内。
    /// </summary>
    /// <param name="point">要判断的点。</param>
    /// <returns>在边界内返回 true。</returns>
    public bool ContainsDipPoint(Point point)
    {
        return point.X >= X && point.X < X + Width && point.Y >= Y && point.Y < Y + Height;
    }

    /// <summary>
    /// 判断指定物理像素点是否在屏幕物理边界内。
    /// </summary>
    /// <param name="point">要判断的点。</param>
    /// <returns>在边界内返回 true。</returns>
    public bool ContainsPhysicalPoint(Point point)
    {
        return point.X >= PhysicalX && point.X < PhysicalX + PhysicalWidth
            && point.Y >= PhysicalY && point.Y < PhysicalY + PhysicalHeight;
    }

    /// <summary>
    /// 返回屏幕信息的字符串表示。
    /// </summary>
    /// <returns>屏幕信息字符串。</returns>
    public override string ToString()
    {
        return $"{Id} ({Name}): {Width}x{Height} @ ({X},{Y}) Scale={ScaleFactor} Rotation={Rotation} Primary={IsPrimary}";
    }
}
