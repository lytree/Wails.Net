using TUnit.Core;
using Wails.Net.Application.Menus;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.MacOS.Tests;

/// <summary>
/// <see cref="MacOSWebviewWindow"/> 在 net10.0 骨架目标（<c>#else</c> 分支）下的行为测试。
/// <para>
/// macOS 的 AppKit/WKWebView 实现由 <c>#if MACOS</c> 保护，无法在 Windows/Linux 主机编译，
/// 因此这里守住的是骨架契约：窗口 ID 稳定、查询类方法返回中性默认值、
/// 所有变更类方法为 no-op 且不抛异常。这些行为保证 ServerMode 回退与跨平台源码兼容"不炸"。
/// </para>
/// <para>
/// 真实窗口创建 / 缩放 / 拖拽语义必须在 macOS 主机上以 <c>net10.0-macos</c> 目标端到端验证（见 AGENTS.md）。
/// </para>
/// </summary>
public sealed class MacOSWebviewWindowSkeletonTests
{
    /// <summary>
    /// 构造后窗口 ID 应与传入值一致（窗口管理器以此为唯一标识）。
    /// </summary>
    [Test]
    [Arguments(0u)]
    [Arguments(7u)]
    [Arguments(uint.MaxValue)]
    public async Task Constructor_PreservesWindowId(uint id)
    {
        var window = CreateWindow(id);

        await Assert.That(window.Id).IsEqualTo(id);
    }

    /// <summary>
    /// 骨架目标的尺寸 / 位置查询应返回零值，而不是抛出或返回未初始化垃圾值。
    /// </summary>
    [Test]
    public async Task Getters_Skeleton_ReturnNeutralDefaults()
    {
        var window = CreateWindow(1);

        await Assert.That(window.GetSize()).IsEqualTo((0, 0));
        await Assert.That(window.GetContentSize()).IsEqualTo((0, 0));
        await Assert.That(window.GetMinSize()).IsEqualTo((0, 0));
        await Assert.That(window.GetMaxSize()).IsEqualTo((0, 0));
        await Assert.That(window.GetPosition()).IsEqualTo((0, 0));
    }

    /// <summary>
    /// 缩放查询在骨架目标固定为 1.0（未缩放），避免调用方做除零或 NaN 运算。
    /// </summary>
    [Test]
    public async Task ZoomGetters_Skeleton_ReturnOne()
    {
        var window = CreateWindow(1);

        await Assert.That(window.GetZoom()).IsEqualTo(1.0f);
        await Assert.That(window.GetZoomLevel()).IsEqualTo(1.0f);
    }

    /// <summary>
    /// 窗口状态查询在骨架目标全部为 false（不存在原生窗口）。
    /// </summary>
    [Test]
    public async Task StateQueries_Skeleton_ReturnFalse()
    {
        var window = CreateWindow(1);

        await Assert.That(window.IsFullscreen()).IsFalse();
        await Assert.That(window.IsMaximised()).IsFalse();
        await Assert.That(window.IsMinimised()).IsFalse();
        await Assert.That(window.IsVisible()).IsFalse();
        await Assert.That(window.IsFocused()).IsFalse();
    }

    /// <summary>
    /// 截图预览在骨架目标返回 null（无 WKWebView 可用于 TakeSnapshot）。
    /// </summary>
    [Test]
    public async Task CapturePreview_Skeleton_ReturnsNull()
    {
        var window = CreateWindow(1);

        await Assert.That(await window.CapturePreviewAsync()).IsNull();
    }

    /// <summary>
    /// 生命周期与几何变更方法在骨架目标应为 no-op，不抛异常。
    /// </summary>
    [Test]
    public async Task LifecycleAndGeometryMethods_Skeleton_AreSafeNoOps()
    {
        var window = CreateWindow(1);

        await Assert.That(() =>
        {
            window.Create();
            window.Show();
            window.Hide();
            window.Focus();
            window.Restore();
            window.Maximise();
            window.UnMaximise();
            window.Minimise();
            window.UnMinimise();
            window.Fullscreen();
            window.UnFullscreen();
            window.Close();

            window.SetTitle("title");
            window.SetSize(800, 600);
            window.SetMinSize(320, 240);
            window.SetMaxSize(1920, 1080);
            window.SetPosition(10, 20);
            window.SetResizable(true);
            window.SetMaximisable(true);
            window.SetFrameless(true);
            window.SetAlwaysOnTop(true);
            window.SetBackgroundColour(1, 2, 3, 255);
            window.SetZoom(1.5f);
            window.SetZoomLevel(2.0f);
        }).ThrowsNothing();
    }

    /// <summary>
    /// 菜单 / 拖放 / 内容保护 / 模态等 macOS 专属能力在骨架目标应安全降级。
    /// </summary>
    [Test]
    public async Task PlatformSpecificMethods_Skeleton_AreSafeNoOps()
    {
        var window = CreateWindow(1);

        await Assert.That(() =>
        {
            window.SetMenu(null);
            window.OpenContextMenu(new ContextMenuData { Id = "test", X = 1, Y = 2 });
            window.SetFileDropEnabled(true);
            window.StartDrag();
            window.StartResize();
            window.SetEnabled(true);
            window.SetContentProtection(true);
            window.AttachAsModal(2);
            window.ShowMenuBar();
            window.HideMenuBar();
            window.ToggleMenuBar();
            window.Flash(true);
        }).ThrowsNothing();
    }

    /// <summary>
    /// 导航与脚本注入在骨架目标应为 no-op（无真实 WebView），且不得抛异常。
    /// </summary>
    [Test]
    public async Task NavigationAndScriptMethods_Skeleton_AreSafeNoOps()
    {
        var window = CreateWindow(1);

        await Assert.That(() =>
        {
            window.SetURL("https://example.com");
            window.SetHTML("<html><body>hi</body></html>");
            window.ExecJS("console.log(1)");
            window.GoBack();
            window.GoForward();
            window.Reload();
            window.ForceReload();
            window.Print();
            window.PrintToPDF(Path.Combine(Path.GetTempPath(), "wails-mac-skeleton-test.pdf"));
        }).ThrowsNothing();
    }

    /// <summary>
    /// 创建带默认选项的骨架窗口实例。
    /// </summary>
    /// <param name="id">窗口 ID。</param>
    /// <returns>窗口实例。</returns>
    private static MacOSWebviewWindow CreateWindow(uint id)
        => new(id, new WebviewWindowOptions { Title = "Test", Width = 800, Height = 600 }, macOptions: null);
}
