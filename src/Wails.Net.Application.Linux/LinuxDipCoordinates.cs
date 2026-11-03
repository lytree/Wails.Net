namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux（GTK4）DIP（设备无关像素）与物理像素的坐标换算，以及多显示器下的目标显示器选择。
/// 对应 Wails v3 PR #6346（<c>fix(v3): report macOS and GTK screens in DIP space</c>）。
/// <para>
/// 依据该 PR：GDK 报告的显示器几何与工作区是逻辑坐标（DIP），
/// 屏幕的 Bounds/WorkArea 以及窗口位置都定义在同一 DIP 空间，
/// 各显示器仅通过 <c>ScaleFactor</c> 标记自己的缩放比例；
/// 物理像素矩形（Physical*）才是 DIP 矩形按缩放因子放大后的设备像素。
/// </para>
/// <para>
/// 因此本移植在窗口层对外一律使用 DIP 坐标，仅在调用 <c>wmctrl</c>
/// （X11 根窗口坐标，即物理像素）时按目标显示器缩放因子换算。
/// 舍入策略与 <see cref="Screens.Screen.Scale"/> 一致：
/// DIP → 物理像素向下取整，物理像素 → DIP 向上取整，以减少往返漂移。
/// </para>
/// <para>
/// 本类为纯函数实现，不依赖 GTK 原生库，可在无 GUI 环境下单元测试。
/// </para>
/// </summary>
internal static class LinuxDipCoordinates
{
    /// <summary>
    /// 默认缩放因子（1.0，等价于 96 DPI 或未缩放的显示器）。
    /// </summary>
    public const double DefaultScaleFactor = 1.0;

    /// <summary>
    /// 归一化缩放因子：非有限值（NaN/Infinity）或小于等于 0 时回退为 <see cref="DefaultScaleFactor"/>。
    /// </summary>
    /// <param name="scaleFactor">原始缩放因子。</param>
    /// <returns>可用的缩放因子（始终大于 0）。</returns>
    public static double NormalizeScaleFactor(double scaleFactor)
    {
        return double.IsFinite(scaleFactor) && scaleFactor > 0
            ? scaleFactor
            : DefaultScaleFactor;
    }

    /// <summary>
    /// 将 DIP 值换算为物理像素（向下取整，与 <see cref="Screens.Screen.Scale"/> 的 DIP → 物理策略一致）。
    /// </summary>
    /// <param name="value">DIP 坐标或尺寸，可为负值。</param>
    /// <param name="scaleFactor">目标显示器缩放因子；无效时按 1.0 处理。</param>
    /// <returns>物理像素值。</returns>
    public static int DipToPhysical(int value, double scaleFactor)
    {
        var factor = NormalizeScaleFactor(scaleFactor);
        return (int)Math.Floor(value * factor);
    }

    /// <summary>
    /// 将物理像素值换算为 DIP（向上取整，与 <see cref="Screens.Screen.Scale"/> 的物理 → DIP 策略一致）。
    /// </summary>
    /// <param name="value">物理像素坐标或尺寸，可为负值。</param>
    /// <param name="scaleFactor">目标显示器缩放因子；无效时按 1.0 处理。</param>
    /// <returns>DIP 值。</returns>
    public static int PhysicalToDip(int value, double scaleFactor)
    {
        var factor = NormalizeScaleFactor(scaleFactor);
        return (int)Math.Ceiling(value / factor);
    }

    /// <summary>
    /// 选择给定 DIP 坐标所在的目标显示器：优先命中包含该点的显示器，
    /// 否则返回距离该点最近的显示器（无显示器时返回 null）。
    /// </summary>
    /// <param name="monitors">显示器 DIP 几何信息列表，可为 null 或空。</param>
    /// <param name="dipX">DIP X 坐标，可为负值（例如位于主显示器左侧的屏幕）。</param>
    /// <param name="dipY">DIP Y 坐标，可为负值。</param>
    /// <returns>目标显示器信息；列表为空时返回 null。</returns>
    public static LinuxMonitorInfo? SelectTargetMonitor(
        IReadOnlyList<LinuxMonitorInfo>? monitors, int dipX, int dipY)
    {
        if (monitors is null || monitors.Count == 0)
        {
            return null;
        }

        // 优先：包含该 DIP 点的显示器（拒绝重叠区域的歧义时取列表中第一个命中项）。
        foreach (var monitor in monitors)
        {
            if (monitor.ContainsDipPoint(dipX, dipY))
            {
                return monitor;
            }
        }

        // 兜底：按到显示器矩形的最短距离选择（与 ScreenManager 的最近矩形语义一致）。
        LinuxMonitorInfo? nearest = null;
        var nearestDistance = long.MaxValue;
        foreach (var monitor in monitors)
        {
            var distance = monitor.DistanceSquaredTo(dipX, dipY);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = monitor;
            }
        }

        return nearest;
    }
}

/// <summary>
/// 描述一个 GTK 显示器的 DIP 几何信息与缩放因子。
/// 对应 Wails v3 PR #6346 中 GDK 以逻辑坐标报告的 <c>GdkMonitor</c> 几何。
/// </summary>
internal readonly struct LinuxMonitorInfo
{
    /// <summary>
    /// 构造显示器 DIP 几何信息。
    /// </summary>
    /// <param name="dipX">显示器左上角 X 坐标（DIP），可为负值。</param>
    /// <param name="dipY">显示器左上角 Y 坐标（DIP），可为负值。</param>
    /// <param name="dipWidth">显示器宽度（DIP）。</param>
    /// <param name="dipHeight">显示器高度（DIP）。</param>
    /// <param name="scaleFactor">显示器缩放因子；无效时按 1.0 处理。</param>
    /// <param name="isPrimary">是否为主显示器。</param>
    public LinuxMonitorInfo(
        int dipX, int dipY, int dipWidth, int dipHeight, double scaleFactor, bool isPrimary = false)
    {
        DipX = dipX;
        DipY = dipY;
        DipWidth = dipWidth;
        DipHeight = dipHeight;
        ScaleFactor = LinuxDipCoordinates.NormalizeScaleFactor(scaleFactor);
        IsPrimary = isPrimary;
    }

    /// <summary>
    /// 显示器左上角 X 坐标（DIP）。
    /// </summary>
    public int DipX { get; }

    /// <summary>
    /// 显示器左上角 Y 坐标（DIP）。
    /// </summary>
    public int DipY { get; }

    /// <summary>
    /// 显示器宽度（DIP）。
    /// </summary>
    public int DipWidth { get; }

    /// <summary>
    /// 显示器高度（DIP）。
    /// </summary>
    public int DipHeight { get; }

    /// <summary>
    /// 显示器缩放因子（已归一化，始终大于 0）。
    /// </summary>
    public double ScaleFactor { get; }

    /// <summary>
    /// 是否为主显示器。
    /// </summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// 判断指定 DIP 点是否落在此显示器边界内（左闭右开、上闭下开）。
    /// </summary>
    /// <param name="dipX">DIP X 坐标。</param>
    /// <param name="dipY">DIP Y 坐标。</param>
    /// <returns>在边界内返回 true。</returns>
    public bool ContainsDipPoint(int dipX, int dipY)
    {
        return dipX >= DipX && dipX < DipX + DipWidth
            && dipY >= DipY && dipY < DipY + DipHeight;
    }

    /// <summary>
    /// 计算指定 DIP 点到显示器矩形的最短距离平方（相交时为 0）。
    /// </summary>
    /// <param name="dipX">DIP X 坐标。</param>
    /// <param name="dipY">DIP Y 坐标。</param>
    /// <returns>距离平方，使用 <see cref="long"/> 以避免大坐标溢出。</returns>
    public long DistanceSquaredTo(int dipX, int dipY)
    {
        var left = DipX;
        var top = DipY;
        var right = DipX + DipWidth;
        var bottom = DipY + DipHeight;

        var dx = Math.Max(0, Math.Max(left - dipX, dipX - right));
        var dy = Math.Max(0, Math.Max(top - dipY, dipY - bottom));
        return (long)dx * dx + (long)dy * dy;
    }
}
