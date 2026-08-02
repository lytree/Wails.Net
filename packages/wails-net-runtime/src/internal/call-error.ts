import type { CallErrorKind, WailsCallError } from "./types.js";

/**
 * 后端返回的业务错误。继承自 `Error`，额外携带 `kind` 与 `cause`，
 * 便于调用方区分 `ReferenceError` / `TypeError` / `RuntimeError`（含权限拒绝）。
 */
export class CallError extends Error {
  /** 错误种类。 */
  public readonly kind: CallErrorKind;
  /** 底层原因（可能为 null）。 */
  public readonly callCause: string | null;

  constructor(error: WailsCallError) {
    super(error.message);
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
export function toCallError(raw: WailsCallError | string | null | undefined): CallError {
  if (raw == null) {
    return new CallError({ message: "未知错误", cause: null, kind: "RuntimeError" });
  }
  if (typeof raw === "string") {
    return new CallError({ message: raw, cause: null, kind: "RuntimeError" });
  }
  return new CallError(raw);
}
