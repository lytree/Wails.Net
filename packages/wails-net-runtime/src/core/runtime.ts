/**
 * Wails.Net 运行时核心对象（`wails`）。
 *
 * 提供通用调用入口 `call` / `bindings.call`，事件系统 `events`，元数据查询 `query`，
 * 以及底层 `invoke` / `cancel`。所有插件与核心命名空间最终都通过 `call` 与后端通信。
 */

import { bindingId, transport, type CancellablePromise } from "../internal/transport.js";

/** 事件订阅回调。 */
export type EventCallback<D = unknown> = (data: D, senderWindowId?: number | null) => void;

/** 事件系统：纯前端订阅 + 向后端发射。 */
export interface WailsEvents {
  /** 订阅事件，返回取消订阅函数。 */
  on<D = unknown>(name: string, callback: EventCallback<D>): () => void;
  /** 订阅一次，触发后自动取消。 */
  once<D = unknown>(name: string, callback: EventCallback<D>): () => void;
  /** 取消指定回调订阅。 */
  off<D = unknown>(name: string, callback: EventCallback<D>): void;
  /** 向后端发射事件（fire-and-forget）。 */
  emit<D = unknown>(name: string, data: D): void;
}

/** 绑定调用子系统。 */
export interface WailsBindings {
  /**
   * 按 FNV-1a ID 调用绑定方法（对应 `wails.bindings.call(id, args)`）。
   * @param id 由 `bindings.id(fullName)` 或 C# 源生成器产出的方法 ID 得到。
   * @param args 位置参数数组。
   */
  call<T = unknown>(id: number, args?: unknown[]): CancellablePromise<T>;
  /** 计算绑定方法全名的 FNV-1a ID（与后端一致）。 */
  id(fullName: string): number;
}

/** 运行时核心对象。 */
export interface WailsRuntime {
  /**
   * 按名称调用绑定方法或插件命令（对应 `type: "call"`，payload `{ name, args }`）。
   * @param name 形如 `"GreetingService.Greet"` 或 `"clipboard.getText"`。
   * @param args 位置参数数组（多参数必须为数组，单参数直接透传）。
   */
  call<T = unknown>(name: string, args?: unknown[]): CancellablePromise<T>;
  /** 绑定子系统。 */
  readonly bindings: WailsBindings;
  /** 事件子系统。 */
  readonly events: WailsEvents;
  /**
   * 查询后端元数据。常见：`bindings`（已注册方法名）、`events`。
   */
  query<T = unknown>(query: "bindings" | "events" | string): CancellablePromise<T>;
  /** 底层调用：发送任意 `type` 并等待响应（如 `call`、`query`）。 */
  invoke<T = unknown>(type: string, payload: unknown): CancellablePromise<T>;
  /** 取消一个进行中的调用。 */
  cancel(callId: string): void;
}

/** 通用调用入口。 */
export function call<T = unknown>(name: string, args: unknown[] = []): CancellablePromise<T> {
  return transport.invoke<T>("call", { name, args });
}

/** 按 ID 调用绑定方法。 */
export function callWithId<T = unknown>(id: number, args: unknown[] = []): CancellablePromise<T> {
  return transport.invoke<T>("call", { id, args });
}

/** 查询后端元数据。 */
export function query<T = unknown>(q: "bindings" | "events" | string): CancellablePromise<T> {
  return transport.invoke<T>("query", { query: q });
}

/** 底层调用。 */
export function invoke<T = unknown>(type: string, payload: unknown): CancellablePromise<T> {
  return transport.invoke<T>(type, payload);
}

/** 取消调用。 */
export function cancel(callId: string): void {
  transport.cancel(callId);
}

/** 事件子系统实现（委托给 transport）。 */
export const events: WailsEvents = {
  on: (name, callback) => transport.on(name, callback as (d: unknown) => void),
  once: (name, callback) => transport.once(name, callback as (d: unknown) => void),
  off: (name, callback) => transport.off(name, callback as (d: unknown) => void),
  emit: (name, data) => transport.emit(name, data),
};

/** 绑定子系统实现。 */
export const bindings: WailsBindings = {
  call: callWithId,
  id: bindingId,
};

/** 运行时核心对象实例。 */
export const wails: WailsRuntime = {
  call,
  bindings,
  events,
  query,
  invoke,
  cancel,
};
