using System.Runtime.CompilerServices;

// 纯逻辑层无平台依赖，在 Windows 主机上也会编译；无条件暴露给测试程序集，
// 避免仅在 macos TFM 条件下声明 InternalsVisibleTo 导致跨平台测试无法访问。
[assembly: InternalsVisibleTo("Wails.Net.Application.MacOS.Tests")]

namespace Wails.Net.Application.Platform;

/// <summary>
/// macOS（AppKit）DIP（设备无关像素）与物理像素的坐标换算，以及多显示器下的目标屏幕选择。
/// 对应 Wails v3 PR #6346（<c>fix(v3): report macOS and GTK screens in DIP space</c>）。
/// <para>
/// AppKit 的 <c>NSScreen.Frame</c> / <c>VisibleFrame</c> / <c>NSWindow.Frame</c> 均以 point
/// 表示，而 point 与 DIP 一一对应；各显示器仅通过 <c>backingScaleFactor</c> 标记自己的缩放比例，
/// 物理像素矩形才是 DIP 矩形按该因子放大后的设备像素。
/// </para>
/// <para>
/// 屏幕与窗口的对外坐标统一为 top-left 原点 DIP 空间（主屏幕左上角为 (0,0)），
/// AppKit 的原生窗口坐标系为 bottom-left 原点，因此需要在两者之间做 Y 翻转：
/// <c>appKitY = primaryScreenHeight - dipHeight - dipTopY</c>。该换算是严格可逆的，
/// 保证 <c>SetPosition</c> 与 <c>GetPosition</c> 往返一致。
/// </para>
/// <para>
/// 舍入策略与 <see cref="Screens.Screen.Scale"/> 一致：
/// DIP → 物理像素向下取整，物理像素 → DIP 向上取整，以减少往返漂移。
/// </para>
/// <para>
/// 本类为纯函数实现，不依赖 AppKit，可在无 macOS 环境下单元测试。
/// </para>
/// </summary>
internal static class MacDipCoordinates
{
    /// <summary>
    /// 默认缩放因子（1.0，等价于未缩放的标准显示器）。
    /// </summary>
    public const double DefaultScaleFactor = 1.0;

    /// <summary>
    /// 归一化缩放因子：非有限值（NaN/Infinity）或小于等于 0 时回退为 <see cref="DefaultScaleFactor"/>。
    /// </summary>
    /// <param name="scaleFactor">原始缩放因子（通常为 <c>NSScreen.BackingScaleFactor</c>）。</param>
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
    /// <param name="value">DIP 坐标或尺寸，可为负值（例如位于主屏幕左侧的显示器）。</param>
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
    /// <param name="value">物理像素坐标或尺寸。</param>
    /// <param name="scaleFactor">目标显示器缩放因子；无效时按 1.0 处理。</param>
    /// <returns>DIP 值。</returns>
    public static int PhysicalToDip(int value, double scaleFactor)
    {
        var factor = NormalizeScaleFactor(scaleFactor);
        return (int)Math.Ceiling(value / factor);
    }

    /// <summary>
    /// 将 top-left 原点 DIP 空间的 Y 坐标转换为 AppKit bottom-left 原点的 Y 坐标。
    /// </summary>
    /// <param name="dipTopY">窗口顶部在全局 DIP 空间中的 Y 坐标。</param>
    /// <param name="dipHeight">窗口高度（DIP）。</param>
    /// <param name="primaryScreenHeightDip">主屏幕高度（DIP，即 <c>NSScreen.Screens[0].Frame.Height</c>）。</param>
    /// <returns>AppKit 窗口框架的 Y 坐标（窗口底边）。</returns>
    public static int ToAppKitBottomLeftY(int dipTopY, int dipHeight, int primaryScreenHeightDip)
    {
        return primaryScreenHeightDip - dipHeight - dipTopY;
    }

    /// <summary>
    /// 将 AppKit bottom-left 原点的 Y 坐标转换为 top-left 原点 DIP 空间的 Y 坐标。
    /// 为 <see cref="ToAppKitBottomLeftY"/> 的严格逆运算，保证位置读取与设置往返一致。
    /// </summary>
    /// <param name="appKitBottomLeftY">AppKit 窗口框架的 Y 坐标（窗口底边）。</param>
    /// <param name="dipHeight">窗口高度（DIP）。</param>
    /// <param name="primaryScreenHeightDip">主屏幕高度（DIP）。</param>
    /// <returns>窗口顶部在全局 DIP 空间中的 Y 坐标。</returns>
    public static int ToDipTopLeftY(int appKitBottomLeftY, int dipHeight, int primaryScreenHeightDip)
    {
        return primaryScreenHeightDip - dipHeight - appKitBottomLeftY;
    }

    /// <summary>
    /// 在工作区（DIP）内计算窗口的居中 top-left 原点 DIP 坐标。
    /// 对应 Wails v3 macOS 窗口 <c>Centre</c> 语义：以 <c>NSScreen.VisibleFrame</c>
    /// 为准（已排除菜单栏与 Dock），全程 DIP 计算。
    /// </summary>
    /// <param name="workAreaDip">目标显示器工作区（top-left 原点 DIP 几何）。</param>
    /// <param name="windowWidthDip">窗口宽度（DIP）。</param>
    /// <param name="windowHeightDip">窗口高度（DIP）。</param>
    /// <returns>居中后的窗口左上角 DIP 坐标。</returns>
    public static (int X, int Y) CentreDipBounds(
        MacMonitorInfo workAreaDip, int windowWidthDip, int windowHeightDip)
    {
        var x = workAreaDip.DipX + (workAreaDip.DipWidth - windowWidthDip) / 2;
        var y = workAreaDip.DipY + (workAreaDip.DipHeight - windowHeightDip) / 2;
        return (x, y);
    }

    /// <summary>
    /// 选择给定 DIP 坐标所在的目标显示器索引：优先命中包含该点的显示器，
    /// 否则返回距离该点最近的显示器；无显示器时返回 -1。
    /// </summary>
    /// <param name="monitors">显示器 DIP 几何信息列表，可为 null 或空。</param>
    /// <param name="dipX">DIP X 坐标，可为负值。</param>
    /// <param name="dipY">DIP Y 坐标，可为负值。</param>
    /// <returns>目标显示器在列表中的索引；列表为空时返回 -1。</returns>
    public static int SelectTargetMonitorIndex(
        IReadOnlyList<MacMonitorInfo>? monitors, int dipX, int dipY)
    {
        if (monitors is null || monitors.Count == 0)
        {
            return -1;
        }

        // 优先：包含该 DIP 点的显示器（重叠歧义时取列表中第一个命中项）。
        for (var i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].ContainsDipPoint(dipX, dipY))
            {
                return i;
            }
        }

        // 兜底：按到显示器矩形的最短距离选择（与 ScreenManager 的最近矩形语义一致）。
        var nearestIndex = -1;
        var nearestDistance = long.MaxValue;
        for (var i = 0; i < monitors.Count; i++)
        {
            var distance = monitors[i].DistanceSquaredTo(dipX, dipY);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }
}

/// <summary>
/// 描述一个 macOS 显示器的 DIP 几何信息与缩放因子。
/// 对应 Wails v3 PR #6346 中 <c>NSScreen</c> 以 point（DIP）报告的几何。
/// </summary>
internal readonly struct MacMonitorInfo
{
    /// <summary>
    /// 构造显示器 DIP 几何信息。
    /// </summary>
    /// <param name="dipX">显示器左上角 X 坐标（DIP），可为负值。</param>
    /// <param name="dipY">显示器左上角 Y 坐标（DIP），可为负值。</param>
    /// <param name="dipWidth">显示器宽度（DIP）。</param>
    /// <param name="dipHeight">显示器高度（DIP）。</param>
    /// <param name="scaleFactor">显示器缩放因子（<c>backingScaleFactor</c>）；无效时按 1.0 处理。</param>
    public MacMonitorInfo(int dipX, int dipY, int dipWidth, int dipHeight, double scaleFactor)
    {
        DipX = dipX;
        DipY = dipY;
        DipWidth = dipWidth;
        DipHeight = dipHeight;
        ScaleFactor = MacDipCoordinates.NormalizeScaleFactor(scaleFactor);
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
    /// 判断指定 DIP 点是否落在此显示器边界内（左闭右开、上闭下开）。
    /// </summary>
    /// <param name="dipX">DIP X 坐标。</param>
    /// <param name="dipY">DIP Y 坐标。</param>
    /// <returns>命中返回 true。</returns>
    public bool ContainsDipPoint(int dipX, int dipY)
    {
        return dipX >= DipX && dipX < DipX + DipWidth &&
               dipY >= DipY && dipY < DipY + DipHeight;
    }

    /// <summary>
    /// 计算指定 DIP 点到此显示器矩形的最短距离平方（点在矩形内时为 0）。
    /// </summary>
    /// <param name="dipX">DIP X 坐标。</param>
    /// <param name="dipY">DIP Y 坐标。</param>
    /// <returns>距离平方（long，避免溢出）。</returns>
    public long DistanceSquaredTo(int dipX, int dipY)
    {
        var dx = dipX < DipX ? DipX - dipX : dipX > DipX + DipWidth ? dipX - (DipX + DipWidth) : 0;
        var dy = dipY < DipY ? DipY - dipY : dipY > DipY + DipHeight ? dipY - (DipY + DipHeight) : 0;
        return (long)dx * dx + (long)dy * dy;
    }
}
