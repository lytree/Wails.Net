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
/// <see cref="LayoutScreens"/> 实现了 Chromium 风格的多屏幕布局算法，
/// 用于在虚拟空间布局屏幕并计算 DIP 坐标。该算法受 Chromium 项目启发
/// （screen_win.cc），通过屏幕树（<see cref="ScreenPlacement"/>）计算 DIP 坐标，
/// 处理 DPI 缩放和屏幕去相交。
/// </para>
/// </summary>
public class ScreenManager : IScreenManager
{
    /// <summary>
    /// 平台应用实例。
    /// </summary>
    private readonly IPlatformApp? _platformApp;

    /// <summary>
    /// 当前缓存的屏幕列表（由 <see cref="LayoutScreens"/> 设置或通过平台应用动态获取）。
    /// </summary>
    private Screen[]? _screens;

    /// <summary>
    /// 当前缓存的主屏幕。
    /// </summary>
    private Screen? _primaryScreen;

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
    /// 若 <see cref="LayoutScreens"/> 已设置缓存，返回缓存的主屏幕；否则委托给平台应用。
    /// </summary>
    /// <returns>主屏幕实例，若平台应用未设置且未调用 <see cref="LayoutScreens"/> 则返回 null。</returns>
    public Screen? GetPrimaryScreen()
    {
        if (_primaryScreen is not null)
        {
            return _primaryScreen;
        }

        return _platformApp?.GetPrimaryScreen();
    }

    /// <summary>
    /// 获取所有屏幕。
    /// 若 <see cref="LayoutScreens"/> 已设置缓存，返回缓存的屏幕列表；否则委托给平台应用。
    /// </summary>
    /// <returns>屏幕数组，若平台应用未设置且未调用 <see cref="LayoutScreens"/> 则返回空数组。</returns>
    public Screen[] GetAllScreens()
    {
        if (_screens is not null)
        {
            return _screens;
        }

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

    // ─── Chromium 风格多屏幕布局算法 ───
    // 对应 Wails v3 Go 版本 screenmanager.go 中 LayoutScreens 及相关方法。
    // 参考：https://source.chromium.org/chromium/chromium/src/+/main:ui/display/win/screen_win.cc

    /// <summary>
    /// 在虚拟空间布局屏幕并计算 DIP 坐标，缓存结果用于后续坐标转换。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.LayoutScreens</c>。
    /// <para>
    /// 算法流程：
    /// <list type="number">
    /// <item>查找主屏幕作为屏幕树根。</item>
    /// <item>BFS 遍历相邻屏幕构建屏幕树（通过边缘接触判定父子关系）。</item>
    /// <item>对每个非主屏幕计算相对于父屏幕的 <see cref="ScreenPlacement"/>（含 DPI 缩放）。</item>
    /// <item>对主屏幕和所有子屏幕应用 DPI 缩放和放置。</item>
    /// <item>检测并修复屏幕之间的相交（沿最小偏移轴推开）。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="screens">屏幕数组，必须包含且仅包含一个 <see cref="Screen.IsPrimary"/> 为 true 的屏幕。</param>
    /// <exception cref="ArgumentException">screens 为 null/空，或未找到主屏幕，或存在多个主屏幕。</exception>
    public void LayoutScreens(Screen[] screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (screens.Length == 0)
        {
            throw new ArgumentException("screens 参数为空数组", nameof(screens));
        }

        _screens = screens;
        CalculateScreensDipCoordinates();
    }

    /// <summary>
    /// 计算所有屏幕的 DIP 坐标。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.calculateScreensDipCoordinates</c>。
    /// <para>
    /// 流程：查找主屏幕 → BFS 构建屏幕树 → 对每个屏幕应用 DPI 缩放和放置 → 去相交。
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">未找到主屏幕，或主屏幕数量不等于 1。</exception>
    private void CalculateScreensDipCoordinates()
    {
        // 查找主屏幕。
        Screen? primary = null;
        var remaining = new List<Screen>();
        var primaryCount = 0;
        foreach (var screen in _screens!)
        {
            if (screen.IsPrimary)
            {
                primary = screen;
                primaryCount++;
            }
            else
            {
                remaining.Add(screen);
            }
        }

        if (primary is null)
        {
            throw new InvalidOperationException("未找到主屏幕（IsPrimary=true 的屏幕）");
        }

        if (primaryCount != 1 || remaining.Count != _screens!.Length - 1)
        {
            throw new InvalidOperationException($"无效的主屏幕数量：期望 1，实际 {primaryCount}");
        }

        _primaryScreen = primary;

        // BFS 构建屏幕树：从主屏幕开始，逐层查找相邻屏幕作为子节点。
        var placements = new List<ScreenPlacement>();
        var availableParents = new Queue<Screen>();
        availableParents.Enqueue(primary);

        while (availableParents.Count > 0)
        {
            var parent = availableParents.Dequeue();
            var touching = FindAndRemoveTouchingScreens(parent, remaining);
            foreach (var child in touching)
            {
                var placement = CalculateScreenPlacement(child, parent);
                placements.Add(placement);
                availableParents.Enqueue(child);
            }
        }

        // 应用 DPI 缩放和放置：先主屏幕，再各子屏幕（保持树顺序）。
        primary.ApplyDPIScaling();
        foreach (var placement in placements)
        {
            placement.Screen.ApplyDPIScaling();
            placement.Apply();
        }

        // 检测并修复屏幕之间的相交。
        DeIntersectScreens(placements);
    }

    /// <summary>
    /// 计算子屏幕相对于父屏幕的放置位置（含 DPI 缩放偏移量）。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.calculateScreenPlacement</c>。
    /// <para>
    /// 算法确定对齐方向（TOP/RIGHT/BOTTOM/LEFT）后，根据屏幕与父屏幕在垂直/水平方向上的
    /// 相对位置计算偏移量和偏移参考（BEGIN/END），并通过 <see cref="ScaleOffset"/>
    /// 按百分比缩放偏移量以保持相对位置关系（参考 Chromium scaling_util.cc）。
    /// </para>
    /// </summary>
    /// <param name="screen">子屏幕。</param>
    /// <param name="parent">父屏幕。</param>
    /// <returns>子屏幕的放置位置信息。</returns>
    private ScreenPlacement CalculateScreenPlacement(Screen screen, Screen parent)
    {
        var alignment = GetScreenAlignment(screen, parent);
        var placement = new ScreenPlacement(
            screen: screen,
            parent: parent,
            alignment: alignment,
            offset: 0,
            offsetReference: OffsetReference.Begin);

        // 根据对齐方向选取参与计算的坐标轴。
        int screenBegin, screenEnd;
        int parentBegin, parentEnd;
        if (alignment is Alignment.Top or Alignment.Bottom)
        {
            screenBegin = screen.X;
            screenEnd = screen.Right;
            parentBegin = parent.X;
            parentEnd = parent.Right;
        }
        else
        {
            screenBegin = screen.Y;
            screenEnd = screen.Bottom;
            parentBegin = parent.Y;
            parentEnd = parent.Bottom;
        }

        // 将所有坐标转换为相对于 parentBegin 的偏移，便于后续计算。
        parentEnd -= parentBegin;
        screenBegin -= parentBegin;
        screenEnd -= parentBegin;
        parentBegin = 0;

        // 计算偏移参考和偏移量：
        // 1. screenEnd == parentEnd：端对齐，END 参考，偏移 0。
        // 2. screenBegin >= parentBegin：基于起始位置，BEGIN 参考，按父屏幕缩放百分比。
        // 3. screenEnd <= parentEnd：基于结束位置，END 参考，按父屏幕缩放百分比。
        // 4. 父屏幕被子屏幕完全包含：基于子屏幕自身长度和缩放百分比。
        if (screenEnd == parentEnd)
        {
            placement.OffsetReference = OffsetReference.End;
            placement.Offset = 0;
        }
        else if (screenBegin >= parentBegin)
        {
            placement.OffsetReference = OffsetReference.Begin;
            placement.Offset = ScaleOffset(parentEnd, parent.ScaleFactor, screenBegin);
        }
        else if (screenEnd <= parentEnd)
        {
            placement.OffsetReference = OffsetReference.End;
            placement.Offset = ScaleOffset(parentEnd, parent.ScaleFactor, parentEnd - screenEnd);
        }
        else
        {
            placement.OffsetReference = OffsetReference.Begin;
            placement.Offset = ScaleOffset(screenEnd - screenBegin, screen.ScaleFactor, screenBegin);
        }

        return placement;
    }

    /// <summary>
    /// 判断子屏幕相对于父屏幕的对齐方向（TOP/RIGHT/BOTTOM/LEFT）。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.getScreenAlignment</c>。
    /// <para>
    /// 判定规则（基于边缘接触）：
    /// <list type="bullet">
    /// <item>角点接触：根据 screen.Y 和 parent.X 判定 BOTTOM/LEFT/TOP。</item>
    /// <item>垂直边接触：根据 screen.X 判定 RIGHT/LEFT。</item>
    /// <item>水平边接触：根据 screen.Y 判定 BOTTOM/TOP。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="screen">子屏幕。</param>
    /// <param name="parent">父屏幕。</param>
    /// <returns>对齐方向。</returns>
    /// <exception cref="InvalidOperationException">两屏幕无边缘接触（不应在算法中发生）。</exception>
    private static Alignment GetScreenAlignment(Screen screen, Screen parent)
    {
        var maxLeft = Math.Max(screen.X, parent.X);
        var maxTop = Math.Max(screen.Y, parent.Y);
        var minRight = Math.Min(screen.Right, parent.Right);
        var minBottom = Math.Min(screen.Bottom, parent.Bottom);

        // 角点接触。
        if (maxLeft == minRight && maxTop == minBottom)
        {
            if (screen.Y == maxTop)
            {
                return Alignment.Bottom;
            }

            if (parent.X == maxLeft)
            {
                return Alignment.Left;
            }

            return Alignment.Top;
        }

        // 垂直边接触。
        if (maxLeft == minRight)
        {
            return screen.X == maxLeft ? Alignment.Right : Alignment.Left;
        }

        // 水平边接触。
        if (maxTop == minBottom)
        {
            return screen.Y == maxTop ? Alignment.Bottom : Alignment.Top;
        }

        throw new InvalidOperationException(
            $"屏幕 {screen.Id} 与父屏幕 {parent.Id} 无边缘接触，无法判定对齐方向");
    }

    /// <summary>
    /// 检测并修复屏幕之间的相交。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.deIntersectScreens</c>。
    /// <para>
    /// 流程：
    /// <list type="number">
    /// <item>构建屏幕 ID 到父屏幕 ID 的映射。</item>
    /// <item>计算每个屏幕在屏幕树中的深度（距主屏幕的层数）。</item>
    /// <item>按深度（升序）和距主屏幕原点的距离（升序）排序屏幕。</item>
    /// <item>对每个屏幕，检查与所有先于它处理的屏幕是否相交，若相交则调用 <see cref="FixScreenIntersection"/> 推开。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="placements">所有非主屏幕的放置信息。</param>
    private void DeIntersectScreens(List<ScreenPlacement> placements)
    {
        // 构建屏幕 ID → 父屏幕 ID 映射。
        var parentIdMap = new Dictionary<string, string>(placements.Count);
        foreach (var placement in placements)
        {
            parentIdMap[placement.Screen.Id] = placement.Parent.Id;
        }

        // 计算每个屏幕在屏幕树中的深度。
        var treeDepthMap = new Dictionary<string, int>(_screens!.Length);
        const int maxDepth = 100;
        foreach (var screen in _screens)
        {
            var id = screen.Id;
            var depth = 0;
            while (id != _primaryScreen!.Id && depth < maxDepth)
            {
                depth++;
                if (parentIdMap.TryGetValue(id, out var parentId))
                {
                    id = parentId;
                }
                else
                {
                    depth = maxDepth;
                }
            }

            treeDepthMap[screen.Id] = depth;
        }

        // 复制屏幕列表并按深度（升序）和距主屏幕原点距离（升序）排序。
        var sortedScreens = new List<Screen>(_screens);
        sortedScreens.Sort((s1, s2) =>
        {
            var d1 = treeDepthMap[s1.Id];
            var d2 = treeDepthMap[s2.Id];
            if (d1 != d2)
            {
                return d1.CompareTo(d2);
            }

            // 距离平方（与主屏幕原点）。
            var dist1 = s1.X * s1.X + s1.Y * s1.Y;
            var dist2 = s2.X * s2.X + s2.Y * s2.Y;
            if (dist1 != dist2)
            {
                return dist1.CompareTo(dist2);
            }

            return string.Compare(s1.Id, s2.Id, StringComparison.Ordinal);
        });

        // 对每个屏幕，检查与所有先于它处理的屏幕是否相交。
        for (var i = 1; i < sortedScreens.Count; i++)
        {
            var target = sortedScreens[i];
            for (var j = 0; j < i; j++)
            {
                var source = sortedScreens[j];
                if (target.Intersects(source))
                {
                    FixScreenIntersection(target, source);
                }
            }
        }
    }

    /// <summary>
    /// 沿 X 或 Y 轴推开目标屏幕以消除与源屏幕的相交。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.fixScreenIntersection</c>。
    /// <para>
    /// 算法选择 X 和 Y 方向中较小的偏移量来推开目标屏幕：
    /// <list type="bullet">
    /// <item>若目标屏幕原点 X ≥ 0：向右推开（源屏幕右边缘 - 目标屏幕 X）。</item>
    /// <item>若目标屏幕原点 X &lt; 0：向左推开（-(目标屏幕右边缘 - 源屏幕 X)）。</item>
    /// <item>Y 方向同理。</item>
    /// </list>
    /// 最终选择 |offsetX| 和 |offsetY| 中较小的一个轴应用偏移，另一个轴偏移置 0。
    /// </para>
    /// </summary>
    /// <param name="target">要推开的目标屏幕。</param>
    /// <param name="source">相交的源屏幕。</param>
    private static void FixScreenIntersection(Screen target, Screen source)
    {
        int offsetX;
        if (target.X >= 0)
        {
            offsetX = source.Right - target.X;
        }
        else
        {
            offsetX = -(target.Right - source.X);
        }

        int offsetY;
        if (target.Y >= 0)
        {
            offsetY = source.Bottom - target.Y;
        }
        else
        {
            offsetY = -(target.Bottom - source.Y);
        }

        // 选择较小偏移量的轴。
        if (Math.Abs(offsetX) <= Math.Abs(offsetY))
        {
            offsetY = 0;
        }
        else
        {
            offsetX = 0;
        }

        target.Move(target.X + offsetX, target.Y + offsetY);
    }

    /// <summary>
    /// 从剩余屏幕列表中查找并移除所有与指定父屏幕边缘接触的屏幕。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.findAndRemoveTouchingScreens</c>。
    /// </summary>
    /// <param name="parent">父屏幕。</param>
    /// <param name="remaining">剩余屏幕列表，方法会将接触的屏幕从此列表中移除。</param>
    /// <returns>所有与父屏幕边缘接触的屏幕列表。</returns>
    private static List<Screen> FindAndRemoveTouchingScreens(Screen parent, List<Screen> remaining)
    {
        var touching = new List<Screen>();
        for (var i = remaining.Count - 1; i >= 0; i--)
        {
            var screen = remaining[i];
            if (AreScreensTouching(parent, screen))
            {
                touching.Add(screen);
                remaining.RemoveAt(i);
            }
        }

        return touching;
    }

    /// <summary>
    /// 判断两个屏幕是否边缘接触（共享一条边但不相交）。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.areScreensTouching</c>。
    /// <para>
    /// 接触条件：
    /// <list type="bullet">
    /// <item>垂直边接触：maxLeft == minRight 且 maxTop ≤ minBottom（左右相邻）。</item>
    /// <item>水平边接触：maxTop == minBottom 且 maxLeft ≤ minRight（上下相邻）。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="a">屏幕 A。</param>
    /// <param name="b">屏幕 B。</param>
    /// <returns>边缘接触返回 true。</returns>
    private static bool AreScreensTouching(Screen a, Screen b)
    {
        var maxLeft = Math.Max(a.X, b.X);
        var maxTop = Math.Max(a.Y, b.Y);
        var minRight = Math.Min(a.Right, b.Right);
        var minBottom = Math.Min(a.Bottom, b.Bottom);

        return (maxLeft == minRight && maxTop <= minBottom)
            || (maxTop == minBottom && maxLeft <= minRight);
    }

    /// <summary>
    /// 按百分比缩放偏移量，保持相对位置关系。
    /// 对应 Wails v3 Go 版本 <c>ScreenManager.scaleOffset</c>。
    /// <para>
    /// 计算 floor((unscaledLength / scaleFactor) * (unscaledOffset / unscaledLength))，
    /// 即先按缩放因子换算长度，再按偏移量占长度的百分比计算缩放后的偏移。
    /// </para>
    /// </summary>
    /// <param name="unscaledLength">未缩放的参考长度。</param>
    /// <param name="scaleFactor">缩放因子。</param>
    /// <param name="unscaledOffset">未缩放的偏移量。</param>
    /// <returns>缩放后的偏移量。</returns>
    private static int ScaleOffset(int unscaledLength, float scaleFactor, int unscaledOffset)
    {
        var factor = scaleFactor <= 0 ? 1f : scaleFactor;
        var scaledLength = unscaledLength / factor;
        var percent = (float)unscaledOffset / unscaledLength;
        return (int)Math.Floor(scaledLength * percent);
    }
}
