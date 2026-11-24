/**
 * 后端返回的业务错误。继承自 `Error`，额外携带 `kind` 与 `cause`，
 * 便于调用方区分 `ReferenceError` / `TypeError` / `RuntimeError`（含权限拒绝）。
 */
export class CallError extends Error {
    constructor(error) {
        super(error.message);
        /** 错误种类。 */
        Object.defineProperty(this, "kind", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: void 0
        });
        /** 底层原因（可能为 null）。 */
        Object.defineProperty(this, "callCause", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: void 0
        });
        this.name = "CallError";
        this.kind = error.kind;
        this.callCause = error.cause ?? null;
        // 保持原型链（ES5 继承兼容）。
        Object.setPrototypeOf(this, CallError.prototype);
    }
}
/**
 * 将后端返回的错误对象归一化为 `CallError`。
 * @param raw 可能为 `WailsCallError` 或字符串。
 */
export function toCallError(raw) {
    if (raw == null) {
        return new CallError({ message: "未知错误", cause: null, kind: "RuntimeError" });
    }
    if (typeof raw === "string") {
        return new CallError({ message: raw, cause: null, kind: "RuntimeError" });
    }
    return new CallError(raw);
}
//# sourceMappingURL=call-error.js.map