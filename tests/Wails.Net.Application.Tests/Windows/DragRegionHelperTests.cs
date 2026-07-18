using System.Text.RegularExpressions;
using TUnit.Core;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Tests.Windows;

/// <summary>
/// <see cref="DragRegionHelper"/> 的单元测试（P1-5：Frameless 拖拽 CSS 变量统一）。
/// 验证生成的 JS/CSS 代码同时支持 Wails v3 (<c>--wails-draggable</c>) 和
/// Tauri v2/Electron (<c>-webkit-app-region</c>) 两种 CSS 变量约定。
/// </summary>
public sealed class DragRegionHelperTests
{
    // ---------------------------------------------------------------------
    // GetDragRegionScript
    // ---------------------------------------------------------------------

    [Test]
    public async Task GetDragRegionScript_ContainsWailsDraggableVariableCheck()
    {
        var script = DragRegionHelper.GetDragRegionScript();

        // 必须检查 --wails-draggable CSS 变量（Wails v3 标准）
        await Assert.That(script.Contains("--wails-draggable")).IsTrue();
    }

    [Test]
    public async Task GetDragRegionScript_ContainsWebkitAppRegionFallbackCheck()
    {
        var script = DragRegionHelper.GetDragRegionScript();

        // 必须检查 -webkit-app-region CSS 变量（Tauri/Electron 兼容回退）
        await Assert.That(script.Contains("-webkit-app-region")).IsTrue();
    }

    [Test]
    public async Task GetDragRegionScript_WailsDraggableTakesPrecedenceOverAppRegion()
    {
        var script = DragRegionHelper.GetDragRegionScript();

        // --wails-draggable 应出现在 -webkit-app-region 之前，确保优先级
        var wailsIdx = script.IndexOf("--wails-draggable", StringComparison.Ordinal);
        var appRegionIdx = script.IndexOf("-webkit-app-region", StringComparison.Ordinal);

        await Assert.That(wailsIdx).IsGreaterThan(-1);
        await Assert.That(appRegionIdx).IsGreaterThan(-1);
        await Assert.That(wailsIdx).IsLessThan(appRegionIdx);
    }

    [Test]
    public async Task GetDragRegionScript_ContainsIdempotencyGuard()
    {
        var script = DragRegionHelper.GetDragRegionScript();

        // 幂等标记，避免重复注入
        await Assert.That(script.Contains("__wailsDragRegionInitialized")).IsTrue();
    }

    [Test]
    public async Task GetDragRegionScript_ContainsMousedownListener()
    {
        var script = DragRegionHelper.GetDragRegionScript();

        await Assert.That(script.Contains("mousedown")).IsTrue();
    }

    [Test]
    public async Task GetDragRegionScript_ContainsButtonZeroCheck()
    {
        var script = DragRegionHelper.GetDragRegionScript();

        // 仅响应左键（button === 0）
        await Assert.That(script.Contains("e.button !== 0")).IsTrue();
    }

    [Test]
    public async Task GetDragRegionScript_ContainsNoDragOverrideSupport()
    {
        var script = DragRegionHelper.GetDragRegionScript();

        // 必须支持 'no-drag' 值以覆盖祖先的 'drag' 设置
        await Assert.That(script.Contains("no-drag")).IsTrue();
    }

    [Test]
    public async Task GetDragRegionScript_ContainsScrollbarBoundsCheck()
    {
        var script = DragRegionHelper.GetDragRegionScript();

        // Wails v3 drag.ts 的边界检查：offsetX - paddingLeft < clientWidth
        await Assert.That(script.Contains("paddingLeft")).IsTrue();
        await Assert.That(script.Contains("paddingTop")).IsTrue();
        await Assert.That(script.Contains("clientWidth")).IsTrue();
        await Assert.That(script.Contains("clientHeight")).IsTrue();
    }

    [Test]
    public async Task GetDragRegionScript_ContainsDragstartSuppression()
    {
        var script = DragRegionHelper.GetDragRegionScript();

        // 阻止 drag 区域的默认 dragstart 事件
        await Assert.That(script.Contains("dragstart")).IsTrue();
    }

    [Test]
    public async Task GetDragRegionScript_CallsWailsStartDragCallback()
    {
        var script = DragRegionHelper.GetDragRegionScript();

        await Assert.That(script.Contains("__wails_startDrag__")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // GetDragRegionCss
    // ---------------------------------------------------------------------

    [Test]
    public async Task GetDragRegionCss_ContainsWailsDraggableSelector()
    {
        var css = DragRegionHelper.GetDragRegionCss();

        // 必须匹配 --wails-draggable: drag 选择器
        await Assert.That(css.Contains("--wails-draggable: drag")).IsTrue();
    }

    [Test]
    public async Task GetDragRegionCss_ContainsWebkitAppRegionSelector()
    {
        var css = DragRegionHelper.GetDragRegionCss();

        // 必须匹配 -webkit-app-region: drag 选择器（兼容回退）
        await Assert.That(css.Contains("-webkit-app-region: drag")).IsTrue();
    }

    [Test]
    public async Task GetDragRegionCss_ContainsUserSelectNone()
    {
        var css = DragRegionHelper.GetDragRegionCss();

        // 拖拽区域应禁用文本选择
        await Assert.That(css.Contains("user-select: none")).IsTrue();
    }

    [Test]
    public async Task GetDragRegionCss_ContainsCursorDefault()
    {
        var css = DragRegionHelper.GetDragRegionCss();

        // 拖拽区域使用默认光标
        await Assert.That(css.Contains("cursor: default")).IsTrue();
    }

    [Test]
    public async Task GetDragRegionCss_ContainsDataWailsDragAttributeSelector()
    {
        var css = DragRegionHelper.GetDragRegionCss();

        // 保留 data-wails-drag 属性选择器，向后兼容
        await Assert.That(css.Contains("[data-wails-drag]")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // GetStartDragCallbackScript
    // ---------------------------------------------------------------------

    [Test]
    public async Task GetStartDragCallbackScript_ContainsWindowId()
    {
        const uint windowId = 42u;
        var script = DragRegionHelper.GetStartDragCallbackScript(windowId);

        // 注入的 windowId 必须出现在 payload 中（JS 对象字面量语法：windowId: 42）
        await Assert.That(script.Contains("windowId: 42")).IsTrue();
    }

    [Test]
    public async Task GetStartDragCallbackScript_DifferentWindowIdsProduceDifferentScripts()
    {
        var script1 = DragRegionHelper.GetStartDragCallbackScript(1u);
        var script2 = DragRegionHelper.GetStartDragCallbackScript(2u);

        await Assert.That(script1).IsNotEqualTo(script2);
    }

    [Test]
    public async Task GetStartDragCallbackScript_RegistersWailsStartDragFunction()
    {
        var script = DragRegionHelper.GetStartDragCallbackScript(1u);

        // 必须将回调挂载到 window.__wails_startDrag__
        await Assert.That(script.Contains("window.__wails_startDrag__")).IsTrue();
    }

    [Test]
    public async Task GetStartDragCallbackScript_PrefersChromeWebviewPostMessage()
    {
        var script = DragRegionHelper.GetStartDragCallbackScript(1u);

        // 优先使用 chrome.webview.postMessage（WebView2）
        await Assert.That(script.Contains("chrome.webview.postMessage")).IsTrue();
    }

    [Test]
    public async Task GetStartDragCallbackScript_ContainsWailsPostMessageFallback()
    {
        var script = DragRegionHelper.GetStartDragCallbackScript(1u);

        // 回退到 __wails_postMessage__（WebKitGTK / Linux）
        await Assert.That(script.Contains("__wails_postMessage__")).IsTrue();
    }

    [Test]
    public async Task GetStartDragCallbackScript_ContainsDragMessageType()
    {
        var script = DragRegionHelper.GetStartDragCallbackScript(1u);

        // 消息类型必须为 "wails:drag"，与 Win32WebviewWindow.DragMessageType 一致
        await Assert.That(script.Contains("wails:drag")).IsTrue();
    }

    [Test]
    public async Task GetStartDragCallbackScript_ChromeWebviewBeforeWailsPostMessage()
    {
        var script = DragRegionHelper.GetStartDragCallbackScript(1u);

        // chrome.webview.postMessage 必须先于 __wails_postMessage__ 出现
        var chromeIdx = script.IndexOf("chrome.webview.postMessage", StringComparison.Ordinal);
        var wailsIdx = script.IndexOf("__wails_postMessage__", StringComparison.Ordinal);

        await Assert.That(chromeIdx).IsGreaterThan(-1);
        await Assert.That(wailsIdx).IsGreaterThan(-1);
        await Assert.That(chromeIdx).IsLessThan(wailsIdx);
    }
}
