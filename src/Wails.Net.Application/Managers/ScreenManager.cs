using Wails.Net.Application.Platform;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Managers;

/// <summary>
/// 屏幕管理器，负责获取屏幕信息及 DIP/物理像素坐标转换。
/// 对应 Wails v3 Go 版本 screenmanager.go 中的 ScreenManager。
/// <para>
/// 通过 IPlatformApp 委托给平台特定的屏幕实现获取屏幕列表；
/// DPI 转换基于每个屏幕的 <see cref="Screen.ScaleFactor"/> 在逻辑像素（DIP）与物理像素之间换算，
/// 使用与 Wails v3 一致的混合舍入策略（除法向上取整、乘法向下取整）以减少往返漂移。
/// </para>
/// <para>
/// 与 Wails v3 的差异：Wails v3 通过 LayoutScreens 算法在虚拟空间布局屏幕并计算 DIP 坐标，
/// 此实现依赖平台实现已提供的 DIP/物理两套坐标（<see cref="Screen.X"/> 等 DIP 属性
/// 与 <see cref="Screen.PhysicalX"/> 等物理属性）。
/// </para>
/// </summary>
public class ScreenManager : IScreenManager
{
    /// <summary>
    /// 平台应用实例。
    /// </summary>
    private readonly IPlatformApp? _platformApp;

    /// <summary>
    /// 使用指定的平台应用构造 ScreenManager 实例。
    /// </summary>
    /// <param name="platformApp">平台应用实例，可为 null（Server 模式）。</param>
    public ScreenManager(IPlatformApp? platformApp)
    {
        _platformApp = platformApp;
    }

    /// <summary>
    /// 获取主屏幕。
    /// </summary>
    /// <returns>主屏幕实例，若平台应用未设置则返回 null。</returns>
    public Screen? GetPrimaryScreen()
    {
        return _platformApp?.GetPrimaryScreen();
    }

    /// <summary>
    /// 获取所有屏幕。
    /// </summary>
    /// <returns>屏幕数组，若平台应用未设置则返回空数组。</returns>
    public Screen[] GetAllScreens()
    {
        if (_platformApp is null)
        {
            return Array.Empty<Screen>();
        }

        return _platformApp.GetScreens();
    }

    /// <inheritdoc />
    public Point DipToPhysicalPoint(Point dipPoint)
    {
        var screen = ScreenNearestDipPoint(dipPoint);
        if (screen is null)
        {
            return dipPoint;
        }

        return screen.DipToPhysicalPoint(dipPoint);
    }

    /// <inheritdoc />
    public Point PhysicalToDipPoint(Point physicalPoint)
    {
        var screen = ScreenNearestPhysicalPoint(physicalPoint);
        if (screen is null)
        {
            return physicalPoint;
        }

        return screen.PhysicalToDipPoint(physicalPoint);
    }

    /// <inheritdoc />
    public Rect DipToPhysicalRect(Rect dipRect)
    {
        var screen = ScreenNearestDipRect(dipRect);
        if (screen is null)
        {
            return dipRect;
        }

        return screen.DipToPhysicalRect(dipRect);
    }

    /// <inheritdoc />
    public Rect PhysicalToDipRect(Rect physicalRect)
    {
        var screen = ScreenNearestPhysicalRect(physicalRect);
        if (screen is null)
        {
            return physicalRect;
        }

        return screen.PhysicalToDipRect(physicalRect);
    }

    /// <inheritdoc />
    public Screen? ScreenNearestDipPoint(Point dipPoint)
    {
        var screens = GetAllScreens();
        if (screens.Length == 0)
        {
            return null;
        }

        // 优先返回包含该点的屏幕，否则返回主屏幕（对齐 Wails v3 screenNearestPoint 兜底语义）。
        foreach (var screen in screens)
        {
            if (screen.ContainsDipPoint(dipPoint))
            {
                return screen;
            }
        }

        return GetPrimaryScreen() ?? screens[0];
    }

    /// <inheritdoc />
    public Screen? ScreenNearestPhysicalPoint(Point physicalPoint)
    {
        var screens = GetAllScreens();
        if (screens.Length == 0)
        {
            return null;
        }

        foreach (var screen in screens)
        {
            if (screen.ContainsPhysicalPoint(physicalPoint))
            {
                return screen;
            }
        }

        return GetPrimaryScreen() ?? screens[0];
    }

    /// <inheritdoc />
    public Screen? ScreenNearestDipRect(Rect dipRect)
    {
        return ScreenNearestRect(dipRect, isPhysical: false);
    }

    /// <inheritdoc />
    public Screen? ScreenNearestPhysicalRect(Rect physicalRect)
    {
        return ScreenNearestRect(physicalRect, isPhysical: true);
    }

    /// <summary>
    /// 找到距离指定矩形最近的屏幕（按距离平方比较）。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.screenNearestRect</c>。
    /// </summary>
    /// <param name="rect">目标矩形。</param>
    /// <param name="isPhysical">true 使用物理边界比较，false 使用 DIP 边界比较。</param>
    /// <returns>最近的屏幕实例；无屏幕时返回 null。</returns>
    private Screen? ScreenNearestRect(Rect rect, bool isPhysical)
    {
        var screens = GetAllScreens();
        if (screens.Length == 0)
        {
            return null;
        }

        Screen? nearest = null;
        var nearestDistance = int.MaxValue;

        foreach (var screen in screens)
        {
            var bounds = isPhysical ? screen.PhysicalBounds : screen.Bounds;
            var distance = DistanceFromRectSquared(rect, bounds);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = screen;
            }
        }

        return nearest ?? screens[0];
    }

    /// <summary>
    /// 计算两个矩形之间的距离平方。
    /// 相交时返回负的相交面积；否则返回 X/Y 方向最小间距的平方和。
    /// 对应 Wails v3 Go 版本 <c>Rect.distanceFromRectSquared</c>。
    /// </summary>
    /// <param name="a">第一个矩形。</param>
    /// <param name="b">第二个矩形。</param>
    /// <returns>距离平方（相交时为负面积）。</returns>
    private static int DistanceFromRectSquared(Rect a, Rect b)
    {
        var intersection = Intersect(a, b);
        if (intersection.Width > 0 && intersection.Height > 0)
        {
            return -(intersection.Width * intersection.Height);
        }

        var aRight = a.X + a.Width;
        var aBottom = a.Y + a.Height;
        var bRight = b.X + b.Width;
        var bBottom = b.Y + b.Height;

        var dX = Math.Max(0, Math.Max(a.X - bRight, b.X - aRight));
        var dY = Math.Max(0, Math.Max(a.Y - bBottom, b.Y - aBottom));
        return dX * dX + dY * dY;
    }

    /// <summary>
    /// 计算两个矩形的交集。
    /// 对应 Wails v3 Go 版本 <c>Rect.Intersect</c>。
    /// </summary>
    /// <param name="a">第一个矩形。</param>
    /// <param name="b">第二个矩形。</param>
    /// <returns>交集矩形；无交集时返回空矩形。</returns>
    private static Rect Intersect(Rect a, Rect b)
    {
        if (a.Width <= 0 || a.Height <= 0 || b.Width <= 0 || b.Height <= 0)
        {
            return default;
        }

        var maxLeft = Math.Max(a.X, b.X);
        var maxTop = Math.Max(a.Y, b.Y);
        var minRight = Math.Min(a.X + a.Width, b.X + b.Width);
        var minBottom = Math.Min(a.Y + a.Height, b.Y + b.Height);

        if (minRight > maxLeft && minBottom > maxTop)
        {
            return new Rect(maxLeft, maxTop, minRight - maxLeft, minBottom - maxTop);
        }

        return default;
    }
}
