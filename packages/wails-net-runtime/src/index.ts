/**
 * @wails-net/runtime — Wails.Net 前端运行时 SDK（TypeScript）。
 *
 * 自包含 IPC 传输 + 核心 API（wails/call/events/defineCommand）+ 共享类型。
 * 桌面插件封装（window/windows/screen/…/log/store/net/fs 等）与移动端插件
 * （barcodeScanner/biometric/…）已随 M3 迁移至各自 `@wails-net/plugin-*` 包
 * （按需安装），本包仅保留核心运行时与 re-export 类型。
 *
 * 用法：
 * ```ts
 * import { wails } from "@wails-net/runtime";
 * import { window } from "@wails-net/plugin-window";
 *
 * const res = await wails.call("GreetingService.Greet", ["World"]);
 * await window.setTitle("hi");
 * const off = wails.events.on("wails:window:created", (e) => console.log(e));
 * ```
 *
 * 与 `Wails.Net.Generator` 的兼容：源生成器产出的 `wails.bindings.call(id, [args])`
 * 由本包的 `wails.bindings.call` 与 `wails.bindings.id(fullName)` 提供支持。
 */

import {
  bindings,
  call,
  cancel,
  events,
  invoke,
  query,
  wails as coreWails,
  type WailsBindings,
  type WailsEvents,
  type WailsRuntime,
} from "./core/runtime.js";
import {
  installContextMenu,
  openContextMenu,
  type ContextMenuPayload,
  type ContextMenuOptions,
} from "./core/contextmenu.js";

// 底层 / 错误
import { CallError, toCallError } from "./internal/call-error.js";
import { bindingId, transport, unpack, type CancellablePromise } from "./internal/transport.js";
import { fnv1a } from "./internal/fnv1a.js";
import type {
  CallErrorKind,
  WailsCallError,
  WailsEventPayload,
  WailsMessage,
  WailsResponse,
  WailsRuntimeFlags,
} from "./internal/types.js";

/** 命名空间集合（核心 API）。 */
const namespaces = {};

/** Wails.Net 前端 SDK 完整类型。 */
export type WailsSdk = WailsRuntime & typeof namespaces;

/** Wails.Net 前端 SDK 实例。 */
export const wails: WailsSdk = {
  ...coreWails,
  ...namespaces,
};

// ---- 具名导出（兼容 `import { wails } from "@wails-net/runtime"` 与高级用法）----

export { call, bindings, events, query, invoke, cancel };
export { defineCommand } from "./core/commands.js";
export type { PackMode } from "./core/commands.js";
export { CallError, toCallError };
export { transport, unpack, bindingId, fnv1a };
export { installContextMenu, openContextMenu };
export type {
  ContextMenuPayload,
  ContextMenuOptions,
  CancellablePromise,
  WailsBindings,
  WailsEvents,
  WailsRuntime,
  CallErrorKind,
  WailsCallError,
  WailsEventPayload,
  WailsMessage,
  WailsResponse,
  WailsRuntimeFlags,
};
export type { EventCallback } from "./core/runtime.js";
export * from "./api/common.js";

export default wails;
