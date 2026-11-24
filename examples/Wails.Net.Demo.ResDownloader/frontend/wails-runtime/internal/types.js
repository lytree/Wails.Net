/**
 * Wails.Net 前后端 IPC 协议类型定义。
 *
 * 协议与 Wails.Net 后端 `MessageProcessor` / `ResponseMessage` 严格对齐：
 * - 上行（前端 → 后端）：`WailsMessage { id, type, payload, windowId?, origin? }`
 * - 下行（后端 → 前端）：`WailsResponse { id, type, result: { result, error } }`
 *
 * 重要：响应是**双层**结构，`result.result` 才是业务返回值，`result.error` 为错误。
 * 部分路径下 `type` 会变成 `"error"`（协议/解析错误），需兼容处理。
 */
/** 读取 `window._wails` 运行时标志（若存在）。 */
export function readRuntimeFlags() {
    const w = globalThis;
    return w._wails;
}
//# sourceMappingURL=types.js.map