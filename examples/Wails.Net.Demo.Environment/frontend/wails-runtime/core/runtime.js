/**
 * Wails.Net 运行时核心对象（`wails`）。
 *
 * 提供通用调用入口 `call` / `bindings.call`，事件系统 `events`，元数据查询 `query`，
 * 以及底层 `invoke` / `cancel`。所有插件与核心命名空间最终都通过 `call` 与后端通信。
 */
import { bindingId, transport } from "../internal/transport.js";
/** 通用调用入口。 */
export function call(name, args = []) {
    return transport.invoke("call", { name, args });
}
/** 按 ID 调用绑定方法。 */
export function callWithId(id, args = []) {
    return transport.invoke("call", { id, args });
}
/** 查询后端元数据。 */
export function query(q) {
    return transport.invoke("query", { query: q });
}
/** 底层调用。 */
export function invoke(type, payload) {
    return transport.invoke(type, payload);
}
/** 取消调用。 */
export function cancel(callId) {
    transport.cancel(callId);
}
/** 事件子系统实现（委托给 transport）。 */
export const events = {
    on: (name, callback) => transport.on(name, callback),
    once: (name, callback) => transport.once(name, callback),
    off: (name, callback) => transport.off(name, callback),
    emit: (name, data) => transport.emit(name, data),
};
/** 绑定子系统实现。 */
export const bindings = {
    call: callWithId,
    id: bindingId,
};
/** 运行时核心对象实例。 */
export const wails = {
    call,
    bindings,
    events,
    query,
    invoke,
    cancel,
};
//# sourceMappingURL=runtime.js.map