namespace Wails.Net.Application.Windows;

/// <summary>
/// 无边框窗口 CSS 拖拽区域辅助类。
/// <para>
/// 对应 Wails v3 前端 <c>drag.ts</c> 的拖拽检测逻辑。
/// 通过注入 JavaScript 监听 <c>mousedown</c> 事件，检查目标元素的计算样式：
/// </para>
/// <para>
/// 支持两种 CSS 变量约定（同时识别，便于跨框架复用）：
/// <list type="bullet">
/// <item><c>--wails-draggable: drag</c> — Wails v3 标准，优先级最高。</item>
/// <item><c>-webkit-app-region: drag</c> — Tauri v2 / Electron 兼容回退。</item>
/// </list>
/// </para>
/// <para>
/// 元素祖先链中 <c>--wails-draggable: no-drag</c> / <c>-webkit-app-region: no-drag</c>
/// 会覆盖外层 <c>drag</c> 设置，允许在拖拽区域内嵌入可交互元素。
/// </para>
/// <para>
/// 为避免拖拽误触滚动条，参照 Wails v3 <c>drag.ts</c> 的边界检查：
/// 仅当 <c>offsetX - paddingLeft &lt; clientWidth</c> 且 <c>offsetY - paddingTop &lt; clientHeight</c>
/// 时才视为有效拖拽点。
/// </para>
/// </summary>
public static class DragRegionHelper
{
    /// <summary>
    /// 获取要注入的 JavaScript 代码，用于实现 CSS 拖拽区域。
    /// 此代码应在页面加载完成后通过 ExecJS 注入。
    /// </summary>
    /// <returns>JavaScript 代码字符串。</returns>
    public static string GetDragRegionScript()
    {
        return """
            (function() {
                if (window.__wailsDragRegionInitialized) return;
                window.__wailsDragRegionInitialized = true;

                // 读取元素的计算样式，按优先级返回 'drag' / 'no-drag' / null。
                // 优先 --wails-draggable（Wails v3 标准），回退 -webkit-app-region（Tauri/Electron 兼容）。
                function getDragRegionValue(el) {
                    var style = window.getComputedStyle(el);
                    var wailsDrag = style.getPropertyValue('--wails-draggable').trim();
                    if (wailsDrag === 'drag' || wailsDrag === 'no-drag') {
                        return wailsDrag;
                    }
                    var appRegion = style.getPropertyValue('-webkit-app-region').trim();
                    if (appRegion === 'drag' || appRegion === 'no-drag') {
                        return appRegion;
                    }
                    return null;
                }

                function getEffectiveDragRegion(element) {
                    var el = element;
                    while (el && el !== document.body) {
                        var region = getDragRegionValue(el);
                        if (region === 'drag') return 'drag';
                        if (region === 'no-drag') return 'no-drag';
                        el = el.parentElement;
                    }
                    return null;
                }

                document.addEventListener('mousedown', function(e) {
                    if (e.button !== 0) return;
                    if (e.target === document || e.target === document.documentElement) return;

                    var region = getEffectiveDragRegion(e.target);
                    if (region !== 'drag') return;

                    // Wails v3 边界检查：避免在滚动条区域触发拖拽。
                    // offsetX/Y 包含 padding，需减去 paddingLeft/Top 后与 clientWidth/Height 比较。
                    var target = e.target;
                    var style = window.getComputedStyle(target);
                    var paddingLeft = parseFloat(style.paddingLeft) || 0;
                    var paddingTop = parseFloat(style.paddingTop) || 0;
                    if (e.offsetX - paddingLeft < target.clientWidth &&
                        e.offsetY - paddingTop < target.clientHeight) {
                        e.preventDefault();
                        e.stopPropagation();
                        if (typeof window.__wails_startDrag__ === 'function') {
                            window.__wails_startDrag__();
                        }
                    }
                }, true);

                // 阻止 drag 区域的默认 dragstart 事件
                document.addEventListener('dragstart', function(e) {
                    var region = getEffectiveDragRegion(e.target);
                    if (region === 'drag') {
                        e.preventDefault();
                    }
                }, true);
            })();
        """;
    }

    /// <summary>
    /// 获取 CSS 规则，为拖拽区域元素设置默认样式。
    /// 同时匹配 Wails v3 (<c>--wails-draggable: drag</c>) 和 Tauri v2 (<c>-webkit-app-region: drag</c>) 约定。
    /// </summary>
    /// <returns>CSS 字符串。</returns>
    public static string GetDragRegionCss()
    {
        return """
            [style*="--wails-draggable: drag"],
            [style*="--wails-draggable:drag"],
            [style*="-webkit-app-region: drag"],
            [style*="-webkit-app-region:drag"],
            [data-wails-drag] {
                -webkit-user-select: none;
                user-select: none;
                cursor: default;
            }
            """;
    }

    /// <summary>
    /// 获取注册全局 __wails_startDrag__ 回调的 JavaScript 代码。
    /// 回调通过 chrome.webview.postMessage（Windows）或 window.webkit.messageHandlers（Linux）
    /// 向后端发送拖拽请求消息。后端在 WebMessageReceived 处理器中识别该消息并调用 StartDrag。
    /// </summary>
    /// <param name="windowId">窗口 ID，用于后端定位窗口实例。</param>
    /// <returns>注册回调的 JavaScript 代码字符串。</returns>
    public static string GetStartDragCallbackScript(uint windowId)
    {
        // 优先使用 chrome.webview.postMessage（WebView2），回退到 postMessage（WebKitGTK）。
        // 消息使用 JSON 字符串格式，type 为 wails:drag 以区别于其他 IPC 消息。
        return $$"""
            (function() {
                window.__wails_startDrag__ = function() {
                    var payload = JSON.stringify({ type: 'wails:drag', windowId: {{windowId}} });
                    if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                        window.chrome.webview.postMessage(payload);
                    } else if (window.__wails_postMessage__) {
                        window.__wails_postMessage__(payload);
                    }
                };
            })();
        """;
    }
}
