/**
 * Wails.Net 运行时核心对象（`wails`）。
 *
 * 提供通用调用入口 `call` / `bindings.call`，事件系统 `events`，元数据查询 `query`，
 * 以及底层 `invoke` / `cancel`。所有插件与核心命名空间最终都通过 `call` 与后端通信。
 */

import { bindingId, transport, type CancellablePromise } from "../internal/transport.js";
import { createChannel } from "./channel.js";

/** 事件订阅回调。 */
export type EventCallback<D = unknown> = (
  data: D,
  senderWindowId?: number | null,
  targetWindowId?: number | null,
) => void;

/** 事件系统：纯前端订阅 + 向后端发射。 */
export interface WailsEvents {
  /** 订阅事件，返回取消订阅函数。支持通配符模式（`*` 后缀前缀匹配，如 `wails:window:*`）。 */
  on<D = unknown>(name: string, callback: EventCallback<D>): () => void;
  /** 订阅一次，触发后自动取消。支持通配符模式。 */
  once<D = unknown>(name: string, callback: EventCallback<D>): () => void;
  /** 取消指定回调订阅。 */
  off<D = unknown>(name: string, callback: EventCallback<D>): void;
  /** 向后端发射事件（fire-and-forget），广播到所有窗口。 */
  emit<D = unknown>(name: string, data: D): void;
  /**
   * 向后端发射跨窗口定向事件（OPT-EVENT-002）：仅派发到目标窗口的前端，其他窗口不收到。
   * 对应后端 <c>Application.EmitTo</c> / <c>WebviewWindow.DispatchWailsEvent</c>。
   */
  emitTo<D = unknown>(targetWindowId: number, name: string, data: D): void;
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

/** 通道子系统（RH-IPC-05）。 */
export interface WailsChannels {
  /** 创建与后端绑定的双向通道（详见 core/channel.ts）。 */
  create(options?: { id?: string }): Promise<import("./channel.js").WailsChannel>;
}

/** call 的可选参数。 */
export interface CallOptions {
  /**
   * 绑定通道（RH-IPC-05）：传通道句柄或通道 ID，payload 附带 `channelId`，
   * 后端命令可通过 `ICommandContext.Channel` 取得通道实例回传进度。
   */
  channel?: import("./channel.js").WailsChannel | string;
}

/** 运行时核心对象。 */
export interface WailsRuntime {
  /**
   * 按名称调用绑定方法或插件命令（对应 `type: "call"`，payload `{ name, args }`）。
   * @param name 形如 `"GreetingService.Greet"` 或 `"clipboard.getText"`。
   * @param args 位置参数数组（多参数必须为数组，单参数直接透传）。
   * @param opts 可选；`channel` 绑定双向通道（RH-IPC-05）。
   */
  call<T = unknown>(name: string, args?: unknown[], opts?: CallOptions): CancellablePromise<T>;
  /** 绑定子系统。 */
  readonly bindings: WailsBindings;
  /** 事件子系统。 */
  readonly events: WailsEvents;
  /**
   * 查询后端元数据。常见：`bindings`（已注册方法名）、`events`。
   */
  query<T = unknown>(query: "bindings" | "events" | string): CancellablePromise<T>;
  /** 底层调用：发送任意 `type` 并等待响应（如 `call`、`query`、`channel:open`）。 */
  invoke<T = unknown>(type: string, payload: unknown, opts?: CallOptions): CancellablePromise<T>;
  /** 取消一个进行中的调用。 */
  cancel(callId: string): void;
  /** 双向通道子系统（RH-IPC-05）。 */
  readonly channel: WailsChannels;
}

/** 通用调用入口。 */
export function call<T = unknown>(name: string, args: unknown[] = [], opts?: CallOptions): CancellablePromise<T> {
  return transport.invoke<T>("call", { name, args }, normalizeCallOptions(opts));
}

/**
 * 归一化 call 选项：通道句柄 → 通道 ID。
 */
function normalizeCallOptions(opts?: CallOptions): { channel?: string } | undefined {
  if (!opts) return undefined;
  const ch = opts.channel;
  if (ch == null) return undefined;
  return { channel: typeof ch === "string" ? ch : ch.id };
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
export function invoke<T = unknown>(type: string, payload: unknown, opts?: CallOptions): CancellablePromise<T> {
  return transport.invoke<T>(type, payload, normalizeCallOptions(opts));
}

/** 取消调用。 */
export function cancel(callId: string): void {
  transport.cancel(callId);
}

/** 事件子系统实现（委托给 transport）。 */
export const events: WailsEvents = {
  on: (name, callback) => transport.on(name, callback as (d: unknown, s?: number | null, t?: number | null) => void),
  once: (name, callback) => transport.once(name, callback as (d: unknown, s?: number | null, t?: number | null) => void),
  off: (name, callback) => transport.off(name, callback as (d: unknown, s?: number | null, t?: number | null) => void),
  emit: (name, data) => transport.emit(name, data),
  emitTo: (targetWindowId, name, data) => transport.emit(name, data, targetWindowId),
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
  channel: {
    create: (options) => createChannel(options),
  },
};
