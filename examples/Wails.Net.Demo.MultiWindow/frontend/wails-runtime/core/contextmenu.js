/**
 * 上下文菜单钩子（对应 C# 端 `RuntimeGenerator.GenerateContextMenuHook`）。
 *
 * 设计要点：
 * - 监听 `window.contextmenu` 事件，依据目标元素的 CSS 变量决定行为：
 *   - `--custom-contextmenu`：自定义菜单 ID。命中时 `preventDefault()` 并向上发送
 *     `contextmenu` 消息（type="contextmenu"），后端 `MessageProcessor.ProcessContextMenu`
 *     按 ID 弹出已注册的 `ContextMenu`。
 *   - `--custom-contextmenu-data`：附加数据字符串，原样透传到后端。
 *   - `--default-contextmenu`：默认菜单策略，取值 `auto`（默认）/ `show` / `hide`。
 * - Debug 模式下始终放行默认右键菜单（便于开发者使用浏览器原生右键菜单）。
 *
 * 与 C# 注入脚本一致：导入本模块即自动安装（幂等），无需手动调用。
 */
import { transport } from "../internal/transport.js";
import { readRuntimeFlags } from "../internal/types.js";
/**
 * 向前端请求弹出已注册的上下文菜单。
 * 通过 `transport.send("contextmenu", ...)` 发送无需响应的消息。
 * @param id 自定义菜单 ID（对应 `menu.setContextMenu` 注册时使用的 ID）。
 * @param x 视口 X 坐标（clientX）。
 * @param y 视口 Y 坐标（clientY）。
 * @param data 附加数据（可选）。
 */
export function openContextMenu(id, x, y, data) {
    const payload = { id, x, y, data: data ?? null };
    transport.send("contextmenu", payload);
}
function getDebugMode(opts) {
    if (opts?.debug !== undefined)
        return opts.debug;
    return readRuntimeFlags()?.isDebug === true;
}
/**
 * 安装上下文菜单钩子（幂等）。
 *
 * 多次调用安全：首次安装后挂 `window.__wailsContextMenuHooked = true` 标记，
 * 后续调用直接返回。导入本模块时已自动调用一次，通常无需手动调用。
 * @param options 可选配置。
 */
export function installContextMenu(options) {
    const w = globalThis;
    // 仅浏览器环境安装
    if (typeof w.window === "undefined" || w.__wailsContextMenuHooked)
        return;
    w.__wailsContextMenuHooked = true;
    const isDebug = getDebugMode(options);
    const open = (id, x, y, data) => openContextMenu(id, x, y, data || null);
    const processDefaultContextMenu = (event, target) => {
        // Debug 构建始终放行默认菜单，便于使用浏览器开发者上下文
        if (isDebug)
            return;
        let defaultPolicy = "";
        try {
            defaultPolicy = target.style
                ? (window.getComputedStyle(target).getPropertyValue("--default-contextmenu") || "").trim()
                : "";
        }
        catch {
            /* ignore */
        }
        switch (defaultPolicy) {
            case "show":
                return;
            case "hide":
                event.preventDefault();
                return;
        }
        // 可编辑元素（contentEditable）保留默认菜单
        if (target.isContentEditable)
            return;
        // 选中文本时保留默认菜单（仅当选择范围位于当前目标内）
        const selection = window.getSelection ? window.getSelection() : null;
        const hasSelection = !!(selection && selection.toString().length > 0);
        if (hasSelection) {
            try {
                for (let i = 0; i < selection.rangeCount; i++) {
                    const range = selection.getRangeAt(i);
                    const rects = range.getClientRects();
                    for (let j = 0; j < rects.length; j++) {
                        const rect = rects[j];
                        if (document.elementFromPoint(rect.left, rect.top) === target)
                            return;
                    }
                }
            }
            catch {
                /* ignore */
            }
        }
        // input/textarea 在可编辑状态保留默认菜单
        if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement) {
            if (hasSelection || (!target.readOnly && !target.disabled)) {
                return;
            }
        }
        // 默认隐藏原生右键菜单
        event.preventDefault();
    };
    const handler = (event) => {
        const target = (event.target || event.srcElement);
        if (!target)
            return;
        let customContextMenu = "";
        let data = "";
        try {
            const cs = window.getComputedStyle(target);
            customContextMenu = (cs.getPropertyValue("--custom-contextmenu") || "").trim();
            data = cs.getPropertyValue("--custom-contextmenu-data") || "";
        }
        catch {
            /* ignore */
        }
        if (customContextMenu) {
            event.preventDefault();
            open(customContextMenu, event.clientX, event.clientY, data);
        }
        else {
            processDefaultContextMenu(event, target);
        }
    };
    window.addEventListener("contextmenu", handler);
}
// 导入即自动安装（幂等），与 C# 注入脚本的自执行 IIFE 行为一致。
installContextMenu();
//# sourceMappingURL=contextmenu.js.map