using TUnit.Core;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Screens;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests;

/// <summary>
/// WebviewWindow 边界/位置 API 的单元测试（TUnit）。
/// 对应项 1：Bounds/PhysicalBounds/RelativePosition/GetBorderSizes/GetScreen/Width/Height/Resizable/IsNormal/IsIgnoreMouseEvents/IsAlwaysOnTop。
/// 通过 ServerWebviewWindow（桩实现）验证接口默认实现契约。
/// </summary>
[NotInParallel]
public sealed class WindowBoundsApiTests
{
    /// <summary>
    /// 创建测试用的接口实例（接口默认实现需要通过接口类型调用）。
    /// </summary>
    private static IWebviewWindowImpl CreateImpl() => new ServerWebviewWindow();

    /// <summary>
    /// 默认实现的 GetBounds 应基于 GetPosition 和 GetSize 返回组合矩形。
    /// ServerWebviewWindow 默认返回 (0,0) 和 (0,0)，因此 Rect 全为 0。
    /// </summary>
    [Test]
    public async Task GetBounds_DefaultImpl_ReturnsCombinedPositionAndSize()
    {
        // 安排
        var window = CreateImpl();

        // 操作
        var bounds = window.GetBounds();

        // 断言：默认实现组合 GetPosition(0,0) 和 GetSize(0,0)
        await Assert.That(bounds.X).IsEqualTo(0);
        await Assert.That(bounds.Y).IsEqualTo(0);
        await Assert.That(bounds.Width).IsEqualTo(0);
        await Assert.That(bounds.Height).IsEqualTo(0);
    }

    /// <summary>
    /// SetBounds 默认实现委托到 SetPosition 和 SetSize，不抛异常即通过。
    /// </summary>
    [Test]
    public async Task SetBounds_DefaultImpl_DelegatesToSetPositionAndSize()
    {
        // 安排
        var window = CreateImpl();
        var bounds = new Rect(10, 20, 300, 200);

        // 操作 + 断言：不抛异常
        Exception? caught = null;
        try { window.SetBounds(bounds); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// GetPhysicalBounds 默认实现应返回与 GetBounds 相同的值。
    /// </summary>
    [Test]
    public async Task GetPhysicalBounds_DefaultImpl_EqualsGetBounds()
    {
        // 安排
        var window = CreateImpl();

        // 操作
        var physical = window.GetPhysicalBounds();
        var dip = window.GetBounds();

        // 断言
        await Assert.That(physical.X).IsEqualTo(dip.X);
        await Assert.That(physical.Y).IsEqualTo(dip.Y);
        await Assert.That(physical.Width).IsEqualTo(dip.Width);
        await Assert.That(physical.Height).IsEqualTo(dip.Height);
    }

    /// <summary>
    /// SetPhysicalBounds 默认实现委托到 SetBounds，不抛异常。
    /// </summary>
    [Test]
    public async Task SetPhysicalBounds_DefaultImpl_DelegatesToSetBounds()
    {
        // 安排
        var window = CreateImpl();

        // 操作 + 断言：不抛异常
        Exception? caught = null;
        try { window.SetPhysicalBounds(new Rect(0, 0, 100, 100)); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// GetRelativePosition 默认实现应返回与 GetPosition 相同的值。
    /// </summary>
    [Test]
    public async Task GetRelativePosition_DefaultImpl_EqualsGetPosition()
    {
        // 安排
        var window = CreateImpl();

        // 操作
        var relative = window.GetRelativePosition();
        var absolute = window.GetPosition();

        // 断言
        await Assert.That(relative.X).IsEqualTo(absolute.X);
        await Assert.That(relative.Y).IsEqualTo(absolute.Y);
    }

    /// <summary>
    /// SetRelativePosition 默认实现委托到 SetPosition，不抛异常。
    /// </summary>
    [Test]
    public async Task SetRelativePosition_DefaultImpl_DelegatesToSetPosition()
    {
        // 安排
        var window = CreateImpl();

        // 操作 + 断言：不抛异常
        Exception? caught = null;
        try { window.SetRelativePosition(50, 60); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// GetBorderSizes 默认实现返回全零 LRTB。
    /// </summary>
    [Test]
    public async Task GetBorderSizes_DefaultImpl_ReturnsZeros()
    {
        // 安排
        var window = CreateImpl();

        // 操作
        var borders = window.GetBorderSizes();

        // 断言
        await Assert.That(borders.Left).IsEqualTo(0);
        await Assert.That(borders.Right).IsEqualTo(0);
        await Assert.That(borders.Top).IsEqualTo(0);
        await Assert.That(borders.Bottom).IsEqualTo(0);
    }

    /// <summary>
    /// GetScreen 默认实现返回 null。
    /// </summary>
    [Test]
    public async Task GetScreen_DefaultImpl_ReturnsNull()
    {
        // 安排
        var window = CreateImpl();

        // 操作
        var screen = window.GetScreen();

        // 断言
        await Assert.That(screen).IsNull();
    }

    /// <summary>
    /// GetWidth/GetHeight 默认实现应从 GetSize 提取。
    /// </summary>
    [Test]
    public async Task GetWidthAndGetHeight_DefaultImpl_ExtractFromGetSize()
    {
        // 安排
        var window = CreateImpl();
        var (w, h) = window.GetSize();

        // 操作
        var width = window.GetWidth();
        var height = window.GetHeight();

        // 断言
        await Assert.That(width).IsEqualTo(w);
        await Assert.That(height).IsEqualTo(h);
    }

    /// <summary>
    /// IsResizable 默认实现返回 true。
    /// </summary>
    [Test]
    public async Task IsResizable_DefaultImpl_ReturnsTrue()
    {
        // 安排
        var window = CreateImpl();

        // 操作 + 断言
        await Assert.That(window.IsResizable()).IsTrue();
    }

    /// <summary>
    /// IsNormal 默认实现基于 IsMaximised/IsMinimised/IsFullscreen。
    /// ServerWebviewWindow 这三者均返回 false，故 IsNormal 应返回 true。
    /// </summary>
    [Test]
    public async Task IsNormal_DefaultImpl_ReturnsTrueWhenNotMaximisedMinimisedFullscreen()
    {
        // 安排
        var window = CreateImpl();

        // 操作 + 断言
        await Assert.That(window.IsNormal()).IsTrue();
    }

    /// <summary>
    /// IsIgnoreMouseEvents 默认实现返回 false。
    /// </summary>
    [Test]
    public async Task IsIgnoreMouseEvents_DefaultImpl_ReturnsFalse()
    {
        // 安排
        var window = CreateImpl();

        // 操作 + 断言
        await Assert.That(window.IsIgnoreMouseEvents()).IsFalse();
    }

    /// <summary>
    /// IsAlwaysOnTop 默认实现返回 false。
    /// </summary>
    [Test]
    public async Task IsAlwaysOnTop_DefaultImpl_ReturnsFalse()
    {
        // 安排
        var window = CreateImpl();

        // 操作 + 断言
        await Assert.That(window.IsAlwaysOnTop()).IsFalse();
    }

    /// <summary>
    /// Rect 结构构造与属性访问。
    /// </summary>
    [Test]
    public async Task Rect_Constructor_SetsPropertiesCorrectly()
    {
        // 安排 + 操作
        var rect = new Rect(10, 20, 300, 200);

        // 断言
        await Assert.That(rect.X).IsEqualTo(10);
        await Assert.That(rect.Y).IsEqualTo(20);
        await Assert.That(rect.Width).IsEqualTo(300);
        await Assert.That(rect.Height).IsEqualTo(200);
    }

    /// <summary>
    /// LRTB 结构构造与属性访问。
    /// </summary>
    [Test]
    public async Task LRTB_Constructor_SetsPropertiesCorrectly()
    {
        // 安排 + 操作
        var lrtb = new LRTB(1, 2, 3, 4);

        // 断言
        await Assert.That(lrtb.Left).IsEqualTo(1);
        await Assert.That(lrtb.Right).IsEqualTo(2);
        await Assert.That(lrtb.Top).IsEqualTo(3);
        await Assert.That(lrtb.Bottom).IsEqualTo(4);
    }

    /// <summary>
    /// Point 结构构造与属性访问。
    /// </summary>
    [Test]
    public async Task Point_Constructor_SetsPropertiesCorrectly()
    {
        // 安排 + 操作
        var point = new Point(15, 25);

        // 断言
        await Assert.That(point.X).IsEqualTo(15);
        await Assert.That(point.Y).IsEqualTo(25);
    }
}
