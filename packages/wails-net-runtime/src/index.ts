/**
 * @wails-net/runtime — Wails.Net 前端运行时 SDK（TypeScript）。
 *
 * 自包含 IPC 传输 + 全部核心命名空间与 46 个插件命令的强类型封装。
 *
 * 用法：
 * ```ts
 * import { wails } from "@wails-net/runtime";
 *
 * const res = await wails.call("GreetingService.Greet", ["World"]);
 * await wails.clipboard.setText("hi");
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

// 核心命名空间
import { clipboard } from "./api/clipboard.js";
import { dialog } from "./api/dialog.js";
import { window, windows, screen, tray, menu, positioner, windowstate, dpiScale } from "./api/window.js";
import {
  application,
  app,
  autostart,
  process,
  os,
  system,
  power,
} from "./api/application.js";
import { fs, fswatch, path, fileassociation } from "./api/fs.js";
import { http, upload, websocket, localhost, cookie, deeplink, opener } from "./api/net.js";
import {
  store,
  sqlite,
  keychain,
  stronghold,
  localization,
  scope,
} from "./api/data.js";
import { notification, shell } from "./api/notification.js";
import { log, globalshortcut, updater, cli } from "./api/log.js";
import {
  barcodeScanner,
  biometric,
  camera,
  geolocation,
  haptics,
  nfc,
  permissions,
  device,
} from "./api/mobile.js";

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

/** 命名空间集合（全部插件 + 核心 API）。 */
const namespaces = {
  window,
  windows,
  screen,
  tray,
  menu,
  positioner,
  windowstate,
  dpiScale,
  clipboard,
  dialog,
  application,
  app,
  autostart,
  process,
  os,
  system,
  power,
  fs,
  fswatch,
  path,
  fileassociation,
  http,
  upload,
  websocket,
  localhost,
  cookie,
  deeplink,
  opener,
  store,
  sqlite,
  keychain,
  stronghold,
  localization,
  scope,
  notification,
  shell,
  log,
  globalshortcut,
  updater,
  cli,
  barcodeScanner,
  biometric,
  camera,
  geolocation,
  haptics,
  nfc,
  permissions,
  device,
};

/** Wails.Net 前端 SDK 完整类型。 */
export type WailsSdk = WailsRuntime & typeof namespaces;

/** Wails.Net 前端 SDK 实例。 */
export const wails: WailsSdk = {
  ...coreWails,
  ...namespaces,
};

// ---- 具名导出（兼容 `import { wails } from "@wails-net/runtime"` 与高级用法）----

export { call, bindings, events, query, invoke, cancel };
export { CallError, toCallError };
export { transport, unpack, bindingId, fnv1a };
export type {
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
