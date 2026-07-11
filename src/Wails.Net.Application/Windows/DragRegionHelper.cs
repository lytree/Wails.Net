namespace Wails.Net.Application.Windows;

/// <summary>
/// 无边框窗口 CSS 拖拽区域辅助类。
/// 对应 Tauri v2 / Electron 的 -webkit-app-region: drag CSS 属性支持。
/// 通过注入 JavaScript 监听 mousedown 事件，检查目标元素的计算样式，
/// 当值为 drag 时调用后端 StartDrag 方法启动窗口拖拽。
/// </summary>
public static class DragRegionHelper
{
    /// <summary>
    /// 获取要注入的 JavaScript 代码，用于实现 CSS 拖拽区域。
    /// 此代码应在页面加载完成后通过 ExecJS 注入。
    /// 监听 mousedown 事件，遍历目标元素祖先链查找 -webkit-app-region 计算样式：
    /// - 值为 drag 时调用 window.__wails_startDrag__() 触发后端拖拽；
    /// - 值为 no-drag 时允许默认交互（覆盖祖先的 drag 设置）。
    /// </summary>
    /// <returns>JavaScript 代码字符串。</returns>
    public static string GetDragRegionScript()
    {
        return """
            (function() {
                if (window.__wailsDragRegionInitialized) return;
                window.__wailsDragRegionInitialized = true;

                function getEffectiveDragRegion(element) {
                    var el = element;
                    while (el && el !== document.body) {
                        var style = window.getComputedStyle(el);
                        var region = style.getPropertyValue('-webkit-app-region');
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
                    if (region === 'drag') {
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
    /// 获取 CSS 规则，为 -webkit-app-region: drag 元素设置默认样式。
    /// </summary>
    /// <returns>CSS 字符串。</returns>
    public static string GetDragRegionCss()
    {
        return "[style*=\"-webkit-app-region: drag\"], [data-wails-drag] { -webkit-user-select: none; user-select: none; cursor: default; }";
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
