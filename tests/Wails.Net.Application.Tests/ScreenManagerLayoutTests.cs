using TUnit.Core;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Screens;

namespace Wails.Net.Application.Tests;

/// <summary>
/// <see cref="ScreenManager.LayoutScreens"/> 的单元测试（TUnit）。
/// 对应项 A：Chromium 风格多屏幕布局算法。
/// </summary>
[NotInParallel]
public sealed class ScreenManagerLayoutTests
{
    /// <summary>
    /// 构造一个 ScaleFactor=1 的屏幕，物理与 DIP 坐标一致。
    /// </summary>
    private static Screen CreateScreen(string id, int x, int y, int width, int height, bool isPrimary = false)
    {
        return new Screen
        {
            Id = id,
            Name = id,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            WorkAreaX = x,
            WorkAreaY = y,
            WorkAreaWidth = width,
            WorkAreaHeight = height,
            PhysicalX = x,
            PhysicalY = y,
            PhysicalWidth = width,
            PhysicalHeight = height,
            PhysicalWorkAreaX = x,
            PhysicalWorkAreaY = y,
            PhysicalWorkAreaWidth = width,
            PhysicalWorkAreaHeight = height,
            ScaleFactor = 1f,
            IsPrimary = isPrimary,
        };
    }

    /// <summary>
    /// 构造一个 ScaleFactor=2 的屏幕，DIP 坐标为物理坐标的一半。
    /// </summary>
    private static Screen CreateScaledScreen(string id, int physicalX, int physicalY, int physicalWidth, int physicalHeight, bool isPrimary = false)
    {
        // DIP 坐标初始与物理一致，ApplyDPIScaling 会按 ScaleFactor 计算 DIP 宽高。
        // 注意：ApplyDPIScaling 只更新 Width/Height/WorkAreaWidth/WorkAreaHeight 和 WorkArea 偏移，
        // 不更新 X/Y/PhysicalX/PhysicalY。
        return new Screen
        {
            Id = id,
            Name = id,
            X = physicalX,
            Y = physicalY,
            Width = physicalWidth,
            Height = physicalHeight,
            WorkAreaX = physicalX,
            WorkAreaY = physicalY,
            WorkAreaWidth = physicalWidth,
            WorkAreaHeight = physicalHeight,
            PhysicalX = physicalX,
            PhysicalY = physicalY,
            PhysicalWidth = physicalWidth,
            PhysicalHeight = physicalHeight,
            PhysicalWorkAreaX = physicalX,
            PhysicalWorkAreaY = physicalY,
            PhysicalWorkAreaWidth = physicalWidth,
            PhysicalWorkAreaHeight = physicalHeight,
            ScaleFactor = 2f,
            IsPrimary = isPrimary,
        };
    }

    // ─── 参数验证 ───

    /// <summary>
    /// null 参数抛出 ArgumentNullException。
    /// </summary>
    [Test]
    public async Task LayoutScreens_Null_ThrowsArgumentNullException()
    {
        var manager = new ScreenManager(null);

        await Assert.That(() => manager.LayoutScreens(null!)).Throws<ArgumentNullException>();
    }

    /// <summary>
    /// 空数组抛出 ArgumentException。
    /// </summary>
    [Test]
    public async Task LayoutScreens_EmptyArray_ThrowsArgumentException()
    {
        var manager = new ScreenManager(null);

        await Assert.That(() => manager.LayoutScreens(Array.Empty<Screen>())).Throws<ArgumentException>();
    }

    /// <summary>
    /// 无主屏幕抛出 InvalidOperationException。
    /// </summary>
    [Test]
    public async Task LayoutScreens_NoPrimary_ThrowsInvalidOperationException()
    {
        var manager = new ScreenManager(null);
        var screens = new[]
        {
            CreateScreen("A", 0, 0, 100, 100, isPrimary: false),
        };

        await Assert.That(() => manager.LayoutScreens(screens)).Throws<InvalidOperationException>();
    }

    /// <summary>
    /// 多个主屏幕抛出 InvalidOperationException。
    /// </summary>
    [Test]
    public async Task LayoutScreens_MultiplePrimary_ThrowsInvalidOperationException()
    {
        var manager = new ScreenManager(null);
        var screens = new[]
        {
            CreateScreen("A", 0, 0, 100, 100, isPrimary: true),
            CreateScreen("B", 100, 0, 100, 100, isPrimary: true),
        };

        await Assert.That(() => manager.LayoutScreens(screens)).Throws<InvalidOperationException>();
    }

    // ─── 缓存行为 ───

    /// <summary>
    /// LayoutScreens 后 GetAllScreens 返回缓存的屏幕列表。
    /// </summary>
    [Test]
    public async Task LayoutScreens_SetsCachedScreens()
    {
        var manager = new ScreenManager(null);
        var screens = new[]
        {
            CreateScreen("P", 0, 0, 100, 100, isPrimary: true),
        };

        manager.LayoutScreens(screens);

        var all = manager.GetAllScreens();
        await Assert.That(all.Length).IsEqualTo(1);
        await Assert.That(all[0].Id).IsEqualTo("P");
    }

    /// <summary>
    /// LayoutScreens 后 GetPrimaryScreen 返回缓存的主屏幕。
    /// </summary>
    [Test]
    public async Task LayoutScreens_SetsCachedPrimaryScreen()
    {
        var manager = new ScreenManager(null);
        var screens = new[]
        {
            CreateScreen("P", 0, 0, 100, 100, isPrimary: true),
        };

        manager.LayoutScreens(screens);

        var primary = manager.GetPrimaryScreen();
        await Assert.That(primary).IsNotNull();
        await Assert.That(primary!.Id).IsEqualTo("P");
        await Assert.That(primary.IsPrimary).IsTrue();
    }

    /// <summary>
    /// 未调用 LayoutScreens 且无平台应用时，GetAllScreens 返回空数组。
    /// </summary>
    [Test]
    public async Task GetAllScreens_NoLayoutNoPlatform_ReturnsEmpty()
    {
        var manager = new ScreenManager(null);

        await Assert.That(manager.GetAllScreens().Length).IsEqualTo(0);
    }

    /// <summary>
    /// 未调用 LayoutScreens 且无平台应用时，GetPrimaryScreen 返回 null。
    /// </summary>
    [Test]
    public async Task GetPrimaryScreen_NoLayoutNoPlatform_ReturnsNull()
    {
        var manager = new ScreenManager(null);

        await Assert.That(manager.GetPrimaryScreen()).IsNull();
    }

    // ─── 单主屏幕 ───

    /// <summary>
    /// 单主屏幕 ScaleFactor=1：ApplyDPIScaling 无变化。
    /// </summary>
    [Test]
    public async Task LayoutScreens_SinglePrimaryNoScaling_NoChanges()
    {
        var manager = new ScreenManager(null);
        var primary = CreateScreen("P", 10, 20, 100, 200, isPrimary: true);
        var screens = new[] { primary };

        manager.LayoutScreens(screens);

        await Assert.That(primary.X).IsEqualTo(10);
        await Assert.That(primary.Y).IsEqualTo(20);
        await Assert.That(primary.Width).IsEqualTo(100);
        await Assert.That(primary.Height).IsEqualTo(200);
    }

    /// <summary>
    /// 单主屏幕 ScaleFactor=2：ApplyDPIScaling 将物理 200x400 转为 DIP 100x200。
    /// </summary>
    [Test]
    public async Task LayoutScreens_SinglePrimaryWithScaling_AppliesDPIScaling()
    {
        var manager = new ScreenManager(null);
        // 物理 200x400，ScaleFactor=2，DIP 应为 100x200。
        var primary = CreateScaledScreen("P", 0, 0, 200, 400, isPrimary: true);
        var screens = new[] { primary };

        manager.LayoutScreens(screens);

        // X/Y 不变（ApplyDPIScaling 不改 X/Y）。
        await Assert.That(primary.X).IsEqualTo(0);
        await Assert.That(primary.Y).IsEqualTo(0);
        // Width/Height 按物理像素 / ScaleFactor 向上取整。
        await Assert.That(primary.Width).IsEqualTo(100);
        await Assert.That(primary.Height).IsEqualTo(200);
        await Assert.That(primary.WorkAreaWidth).IsEqualTo(100);
        await Assert.That(primary.WorkAreaHeight).IsEqualTo(200);
    }

    // ─── 双屏幕布局（无 DPI 缩放） ───

    /// <summary>
    /// 主屏幕 + 右屏幕（ScaleFactor=1）：子屏幕位于主屏幕右侧，Y 对齐。
    /// </summary>
    [Test]
    public async Task LayoutScreens_PrimaryAndRight_NoScaling()
    {
        var manager = new ScreenManager(null);
        // 主屏 (0,0) 100x100；右屏物理坐标 (100,0) 50x50，与主屏右边缘 (100) 接触。
        var primary = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        var right = CreateScreen("R", 100, 0, 50, 50);
        var screens = new[] { primary, right };

        manager.LayoutScreens(screens);

        // 主屏不变。
        await Assert.That(primary.X).IsEqualTo(0);
        await Assert.That(primary.Y).IsEqualTo(0);
        // 右屏：RIGHT 对齐，offset=0，X = 0 + 100 = 100；Y = 0 + 0 = 0。
        await Assert.That(right.X).IsEqualTo(100);
        await Assert.That(right.Y).IsEqualTo(0);
    }

    /// <summary>
    /// 主屏幕 + 下屏幕（ScaleFactor=1）：子屏幕位于主屏幕下方，X 对齐。
    /// </summary>
    [Test]
    public async Task LayoutScreens_PrimaryAndBottom_NoScaling()
    {
        var manager = new ScreenManager(null);
        var primary = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        var bottom = CreateScreen("B", 0, 100, 80, 50);
        var screens = new[] { primary, bottom };

        manager.LayoutScreens(screens);

        // 下屏：BOTTOM 对齐，offset=0，X = 0 + 0 = 0；Y = 0 + 100 = 100。
        await Assert.That(bottom.X).IsEqualTo(0);
        await Assert.That(bottom.Y).IsEqualTo(100);
    }

    /// <summary>
    /// 主屏幕 + 左屏幕：子屏幕位于主屏幕左侧。
    /// </summary>
    [Test]
    public async Task LayoutScreens_PrimaryAndLeft_NoScaling()
    {
        var manager = new ScreenManager(null);
        // 主屏 (100,0) 100x100；左屏 (50,0) 50x50，与主屏左边缘 (100) 接触。
        var primary = CreateScreen("P", 100, 0, 100, 100, isPrimary: true);
        var left = CreateScreen("L", 50, 0, 50, 50);
        var screens = new[] { primary, left };

        manager.LayoutScreens(screens);

        // 左屏：LEFT 对齐，offset=0，X = 100 - 50 = 50；Y = 0 + 0 = 0。
        await Assert.That(left.X).IsEqualTo(50);
        await Assert.That(left.Y).IsEqualTo(0);
    }

    /// <summary>
    /// 主屏幕 + 上屏幕：子屏幕位于主屏幕上方。
    /// </summary>
    [Test]
    public async Task LayoutScreens_PrimaryAndTop_NoScaling()
    {
        var manager = new ScreenManager(null);
        var primary = CreateScreen("P", 0, 100, 100, 100, isPrimary: true);
        var top = CreateScreen("T", 0, 50, 80, 50);
        var screens = new[] { primary, top };

        manager.LayoutScreens(screens);

        // 上屏：TOP 对齐，offset=0，X = 0 + 0 = 0；Y = 100 - 50 = 50。
        await Assert.That(top.X).IsEqualTo(0);
        await Assert.That(top.Y).IsEqualTo(50);
    }

    // ─── 双屏幕带偏移量 ───

    /// <summary>
    /// 主屏幕 + 右屏幕带 Y 偏移：子屏幕 Y 轴偏移保持。
    /// </summary>
    [Test]
    public async Task LayoutScreens_PrimaryAndRightWithOffset_NoScaling()
    {
        var manager = new ScreenManager(null);
        var primary = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        // 右屏位于 (100, 30)，与主屏右边缘接触，Y 偏移 30。
        var right = CreateScreen("R", 100, 30, 50, 50);
        var screens = new[] { primary, right };

        manager.LayoutScreens(screens);

        // 右屏：RIGHT 对齐，BEGIN 参考，offset = scaleOffset(100, 1, 30) = 30。
        // X = 0 + 100 = 100；Y = 0 + 30 = 30。
        await Assert.That(right.X).IsEqualTo(100);
        await Assert.That(right.Y).IsEqualTo(30);
    }

    /// <summary>
    /// 主屏幕 + 右屏幕端对齐：子屏幕底部与主屏幕底部对齐，END 参考。
    /// </summary>
    [Test]
    public async Task LayoutScreens_PrimaryAndRightEndAligned_NoScaling()
    {
        var manager = new ScreenManager(null);
        var primary = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        // 右屏 (100, 50) 50x50，底部 (100) 与主屏底部 (100) 对齐。
        var right = CreateScreen("R", 100, 50, 50, 50);
        var screens = new[] { primary, right };

        manager.LayoutScreens(screens);

        // screenEnd (100) == parentEnd (100)，END 参考，offset=0。
        // X = 0 + 100 = 100；Y = 0 + (100 - 0 - 50) = 50。
        await Assert.That(right.X).IsEqualTo(100);
        await Assert.That(right.Y).IsEqualTo(50);
    }

    // ─── DPI 缩放场景 ───

    /// <summary>
    /// 主屏幕 1x + 右屏幕 2x：子屏幕物理坐标右接触主屏幕，DIP 缩放后位置正确。
    /// <para>
    /// 算法流程：
    /// 1. 原始坐标：主屏 (0,0) 100x100，右屏 (100,0) 100x100 物理。
    /// 2. 计算放置（使用原始坐标）：right.Bottom=100 == primary.Bottom=100 → END 对齐，offset=0。
    /// 3. 应用 DPI 缩放：右屏 100x100 物理 → 50x50 DIP。
    /// 4. 应用放置（使用缩放后坐标）：END 时 offset = parent.Height - 0 - child.Height = 100 - 50 = 50。
    ///    Y = 0 + 50 = 50；X = 0 + 100 = 100。
    /// 5. 结果：右屏底部与主屏底部对齐（保持原始物理布局的相对位置关系）。
    /// </para>
    /// </summary>
    [Test]
    public async Task LayoutScreens_Primary1xAndRight2x_ScalesOffset()
    {
        var manager = new ScreenManager(null);
        // 主屏物理 100x100，ScaleFactor=1。
        var primary = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        // 右屏物理 (100, 0) 100x100，ScaleFactor=2，DIP 应为 50x50。
        var right = CreateScaledScreen("R", 100, 0, 100, 100);
        var screens = new[] { primary, right };

        manager.LayoutScreens(screens);

        // 主屏 ScaleFactor=1，不变。
        await Assert.That(primary.X).IsEqualTo(0);
        await Assert.That(primary.Width).IsEqualTo(100);
        // 右屏：ApplyDPIScaling 将 100x100 物理 → 50x50 DIP。
        await Assert.That(right.Width).IsEqualTo(50);
        await Assert.That(right.Height).IsEqualTo(50);
        // 右屏：原始物理布局中两屏底部对齐（Bottom=100）。
        // END 对齐：DIP 缩放后 offset = parent.Height - 0 - child.Height = 100 - 50 = 50。
        // X = 0 + 100 = 100；Y = 0 + 50 = 50（保持底部对齐：Y + Height = 50 + 50 = 100 = primary.Bottom）。
        await Assert.That(right.X).IsEqualTo(100);
        await Assert.That(right.Y).IsEqualTo(50);
    }

    /// <summary>
    /// 主屏幕 2x + 右屏幕 1x：子屏幕偏移按主屏幕缩放百分比计算。
    /// <para>
    /// 算法流程：
    /// 1. 原始坐标：主屏 (0,0) 200x200 物理，右屏 (200,60) 100x100 物理。
    /// 2. 计算放置（使用原始坐标）：RIGHT 对齐，BEGIN 参考。
    ///    offset = scaleOffset(parentEnd=200, parent.ScaleFactor=2, screenBegin=60)
    ///           = floor((200/2) * (60/200)) = floor(100 * 0.3) = 30。
    /// 3. 应用 DPI 缩放：主屏 200x200 → 100x100 DIP；右屏 ScaleFactor=1 不变。
    /// 4. 应用放置：Y = 0 + 30 = 30；X = 0 + 100 = 100。
    /// </para>
    /// </summary>
    [Test]
    public async Task LayoutScreens_Primary2xAndRight1x_ScalesOffsetByPrimary()
    {
        var manager = new ScreenManager(null);
        // 主屏物理 200x200，ScaleFactor=2，DIP 100x100。
        var primary = CreateScaledScreen("P", 0, 0, 200, 200, isPrimary: true);
        // 右屏物理 (200, 60) 100x100，ScaleFactor=1。
        var right = CreateScreen("R", 200, 60, 100, 100);
        var screens = new[] { primary, right };

        manager.LayoutScreens(screens);

        // 主屏 ApplyDPIScaling：100x100 DIP。
        await Assert.That(primary.Width).IsEqualTo(100);
        await Assert.That(primary.Height).IsEqualTo(100);
        // 右屏 ScaleFactor=1，ApplyDPIScaling 无变化。
        // 右屏 RIGHT 对齐：BEGIN 参考，offset = scaleOffset(parentEnd=200, parent.ScaleFactor=2, screenBegin=60)。
        // scaledLength = 200/2 = 100；percent = 60/200 = 0.3；offset = floor(100 * 0.3) = 30。
        // X = 0 + 100 = 100；Y = 0 + 30 = 30。
        await Assert.That(right.X).IsEqualTo(100);
        await Assert.That(right.Y).IsEqualTo(30);
    }

    // ─── 多屏幕链式布局 ───

    /// <summary>
    /// 三屏幕横向链式布局：主 → 右 → 右右。
    /// </summary>
    [Test]
    public async Task LayoutScreens_ThreeScreensHorizontalChain()
    {
        var manager = new ScreenManager(null);
        var primary = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        var middle = CreateScreen("M", 100, 0, 80, 100);
        var far = CreateScreen("F", 180, 0, 60, 100);
        var screens = new[] { primary, middle, far };

        manager.LayoutScreens(screens);

        // 主屏不变。
        await Assert.That(primary.X).IsEqualTo(0);
        // 中屏：RIGHT 对齐，X = 0 + 100 = 100。
        await Assert.That(middle.X).IsEqualTo(100);
        // 远屏：RIGHT 对齐于中屏，X = 100 + 80 = 180。
        await Assert.That(far.X).IsEqualTo(180);
    }

    /// <summary>
    /// 主屏幕 + 四方向屏幕：上下左右各一个屏幕。
    /// </summary>
    [Test]
    public async Task LayoutScreens_FourDirectionScreens()
    {
        var manager = new ScreenManager(null);
        var primary = CreateScreen("P", 100, 100, 100, 100, isPrimary: true);
        var right = CreateScreen("R", 200, 100, 50, 100);
        var left = CreateScreen("L", 50, 100, 50, 100);
        var top = CreateScreen("T", 100, 0, 100, 100);
        var bottom = CreateScreen("B", 100, 200, 100, 50);
        var screens = new[] { primary, right, left, top, bottom };

        manager.LayoutScreens(screens);

        // 右屏：X = 100 + 100 = 200；Y = 100。
        await Assert.That(right.X).IsEqualTo(200);
        await Assert.That(right.Y).IsEqualTo(100);
        // 左屏：X = 100 - 50 = 50；Y = 100。
        await Assert.That(left.X).IsEqualTo(50);
        await Assert.That(left.Y).IsEqualTo(100);
        // 上屏：X = 100；Y = 100 - 100 = 0。
        await Assert.That(top.X).IsEqualTo(100);
        await Assert.That(top.Y).IsEqualTo(0);
        // 下屏：X = 100；Y = 100 + 100 = 200。
        await Assert.That(bottom.X).IsEqualTo(100);
        await Assert.That(bottom.Y).IsEqualTo(200);
    }

    // ─── 孤立屏幕处理 ───

    /// <summary>
    /// 孤立屏幕（不与任何屏幕接触）不参与布局树，但保留在屏幕列表中。
    /// </summary>
    [Test]
    public async Task LayoutScreens_OrphanScreen_RemainsInList()
    {
        var manager = new ScreenManager(null);
        var primary = CreateScreen("P", 0, 0, 100, 100, isPrimary: true);
        // 孤立屏幕位于 (1000, 1000)，与主屏无边缘接触。
        var orphan = CreateScreen("O", 1000, 1000, 50, 50);
        var screens = new[] { primary, orphan };

        manager.LayoutScreens(screens);

        // 主屏不变。
        await Assert.That(primary.X).IsEqualTo(0);
        // 孤立屏幕原位置不变（未参与布局树）。
        await Assert.That(orphan.X).IsEqualTo(1000);
        await Assert.That(orphan.Y).IsEqualTo(1000);
        // 仍在屏幕列表中。
        await Assert.That(manager.GetAllScreens().Length).IsEqualTo(2);
    }
}
