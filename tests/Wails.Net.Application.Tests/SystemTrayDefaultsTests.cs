using TUnit.Core;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Screens;
using Wails.Net.Application.SystemTray;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests;

/// <summary>
/// SystemTray 接口默认实现的单元测试（TUnit）。
/// 对应项 2：AttachWindow/PositionWindow/ToggleWindow/ShowWindow/HideWindow/ShowMenu/OpenMenu/GetBounds 等
/// 高级方法的接口默认实现契约。通过最小桩实现验证默认行为不抛异常且返回约定值。
/// </summary>
[NotInParallel]
public sealed class SystemTrayDefaultsTests
{
    /// <summary>
    /// 创建测试用的接口实例（接口默认实现需要通过接口类型调用）。
    /// StubSystemTray 仅实现必要方法，其余方法走接口默认实现。
    /// </summary>
    private static ISystemTrayImpl CreateImpl() => new StubSystemTray();

    /// <summary>
    /// SetIconPosition 默认实现为 no-op，不应抛异常。
    /// </summary>
    [Test]
    public async Task SetIconPosition_DefaultImpl_DoesNotThrow()
    {
        // 安排
        var tray = CreateImpl();

        // 操作 + 断言
        Exception? caught = null;
        try { tray.SetIconPosition(TrayIconPosition.Right); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// SetTemplateIcon 默认实现为 no-op（仅 macOS 支持），不应抛异常。
    /// </summary>
    [Test]
    public async Task SetTemplateIcon_DefaultImpl_DoesNotThrow()
    {
        // 安排
        var tray = CreateImpl();

        // 操作 + 断言
        Exception? caught = null;
        try { tray.SetTemplateIcon(new byte[] { 0x01, 0x02 }); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// AttachWindow 默认实现为 no-op，不应抛异常。
    /// </summary>
    [Test]
    public async Task AttachWindow_DefaultImpl_DoesNotThrow()
    {
        // 安排
        var tray = CreateImpl();
        var window = new WebviewWindow(1, "Test", new WebviewWindowOptions { Name = "Test" });

        // 操作 + 断言
        Exception? caught = null;
        try { tray.AttachWindow(window); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// WindowOffset 默认实现为 no-op，不应抛异常。
    /// </summary>
    [Test]
    public async Task WindowOffset_DefaultImpl_DoesNotThrow()
    {
        // 安排
        var tray = CreateImpl();

        // 操作 + 断言
        Exception? caught = null;
        try { tray.WindowOffset(20); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// WindowDebounce 默认实现为 no-op，不应抛异常。
    /// </summary>
    [Test]
    public async Task WindowDebounce_DefaultImpl_DoesNotThrow()
    {
        // 安排
        var tray = CreateImpl();

        // 操作 + 断言
        Exception? caught = null;
        try { tray.WindowDebounce(300); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// PositionWindow 默认实现为 no-op，不应抛异常。
    /// </summary>
    [Test]
    public async Task PositionWindow_DefaultImpl_DoesNotThrow()
    {
        // 安排
        var tray = CreateImpl();
        var window = new WebviewWindow(1, "Test", new WebviewWindowOptions { Name = "Test" });

        // 操作 + 断言
        Exception? caught = null;
        try { tray.PositionWindow(window, 10); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// ToggleWindow 默认实现为 no-op，不应抛异常。
    /// </summary>
    [Test]
    public async Task ToggleWindow_DefaultImpl_DoesNotThrow()
    {
        // 安排
        var tray = CreateImpl();

        // 操作 + 断言
        Exception? caught = null;
        try { tray.ToggleWindow(); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// ShowWindow 默认实现为 no-op，不应抛异常。
    /// </summary>
    [Test]
    public async Task ShowWindow_DefaultImpl_DoesNotThrow()
    {
        // 安排
        var tray = CreateImpl();

        // 操作 + 断言
        Exception? caught = null;
        try { tray.ShowWindow(); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// HideWindow 默认实现为 no-op，不应抛异常。
    /// </summary>
    [Test]
    public async Task HideWindow_DefaultImpl_DoesNotThrow()
    {
        // 安排
        var tray = CreateImpl();

        // 操作 + 断言
        Exception? caught = null;
        try { tray.HideWindow(); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// ShowMenu 默认实现为 no-op，不应抛异常。
    /// </summary>
    [Test]
    public async Task ShowMenu_DefaultImpl_DoesNotThrow()
    {
        // 安排
        var tray = CreateImpl();

        // 操作 + 断言
        Exception? caught = null;
        try { tray.ShowMenu(); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// OpenMenu 默认实现为 no-op，不应抛异常。
    /// </summary>
    [Test]
    public async Task OpenMenu_DefaultImpl_DoesNotThrow()
    {
        // 安排
        var tray = CreateImpl();

        // 操作 + 断言
        Exception? caught = null;
        try { tray.OpenMenu(); }
        catch (Exception ex) { caught = ex; }
        await Assert.That(caught).IsNull();
    }

    /// <summary>
    /// GetBounds 默认实现返回 null（不支持的平台）。
    /// </summary>
    [Test]
    public async Task GetBounds_DefaultImpl_ReturnsNull()
    {
        // 安排
        var tray = CreateImpl();

        // 操作
        var bounds = tray.GetBounds();

        // 断言
        await Assert.That(bounds).IsNull();
    }
}

/// <summary>
/// ISystemTrayImpl 的最小桩实现，仅实现必要方法，其余走接口默认实现。
/// 用于测试接口默认实现契约。
/// </summary>
#pragma warning disable CS0067 // 事件在桩实现中未被订阅，符合预期
internal sealed class StubSystemTray : ISystemTrayImpl
{
    public event Action? OnTrayClick;
    public event Action? OnTrayRightClick;
    public event Action? OnTrayDoubleClick;
    public event Action? OnTrayRightDoubleClick;
    public event Action? OnTrayMouseEnter;
    public event Action? OnTrayMouseLeave;

    public void SetIcon(byte[] iconData) { }
    public void SetLabel(string label) { }
    public void SetMenu(Menu? menu) { }
    public void Show() { }
    public void Hide() { }
    public void Destroy() { }
    public void SetTooltip(string tooltip) { }
    public void SetDarkModeIcon(byte[] iconData) { }
}
#pragma warning restore CS0067
