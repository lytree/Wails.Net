/**
 * 网络相关命令封装：HTTP 客户端、上传下载、WebSocket、本地服务器、Cookie、深链、外部打开。
 */
import { call } from "../core/runtime.js";
import type { HttpResponse, TransferProgress } from "./common.js";

/** HTTP 客户端（命令前缀 `http.*`）。 */
export const http = {
  fetch: (url: string, options?: unknown) => call<HttpResponse>("http.fetch", [url, options]),
  get: (url: string, headers?: Record<string, string>) => call<HttpResponse>("http.get", [url, headers]),
  post: (url: string, body?: unknown, headers?: Record<string, string>) =>
    call<HttpResponse>("http.post", [url, body, headers]),
  put: (url: string, body?: unknown, headers?: Record<string, string>) =>
    call<HttpResponse>("http.put", [url, body, headers]),
  delete: (url: string, headers?: Record<string, string>) => call<HttpResponse>("http.delete", [url, headers]),
};

/** 上传 / 下载（命令前缀 `upload.*`）。 */
export const upload = {
  upload: (url: string, options?: unknown) => call<unknown>("upload.upload", [url, options]),
  uploadWithProgress: (url: string, options: unknown, onProgress: (p: TransferProgress) => void) =>
    call<unknown>("upload.uploadWithProgress", [url, options, onProgress]),
  download: (url: string, destPath?: string) => call<unknown>("upload.download", [url, destPath]),
  downloadWithProgress: (url: string, destPath: string, onProgress: (p: TransferProgress) => void) =>
    call<unknown>("upload.downloadWithProgress", [url, destPath, onProgress]),
};

/** WebSocket（命令前缀 `websocket.*`，按连接 ID 管理）。 */
export const websocket = {
  connect: (url: string, protocols?: string[]) => call<string>("websocket.connect", [url, protocols]),
  send: (connectionId: string, message: string) => call<void>("websocket.send", [connectionId, message]),
  sendBinary: (connectionId: string, base64Data: string) =>
    call<void>("websocket.sendBinary", [connectionId, base64Data]),
  close: (connectionId: string) => call<void>("websocket.close", [connectionId]),
  getState: (connectionId: string) => call<string>("websocket.getState", [connectionId]),
};

/** 本地 HTTP 服务器（命令前缀 `localhost.*`）。 */
export const localhost = {
  start: (port?: number, rootDir?: string) => call<void>("localhost.start", [port, rootDir]),
  stop: () => call<void>("localhost.stop", []),
  getUrl: () => call<string>("localhost.getUrl", []),
  isRunning: () => call<boolean>("localhost.isRunning", []),
  setRoot: (rootDir: string) => call<void>("localhost.setRoot", [rootDir]),
  addRoute: (route: string, handlerName?: string) => call<void>("localhost.addRoute", [route, handlerName]),
  removeRoute: (route: string) => call<void>("localhost.removeRoute", [route]),
  listRoutes: () => call<string[]>("localhost.listRoutes", []),
};

/** Cookie（命令前缀 `cookie.*`）。 */
export const cookie = {
  get: (url: string, name?: string) => call<unknown>("cookie.get", [url, name]),
  set: (url: string, name: string, value: string, options?: unknown) =>
    call<void>("cookie.set", [url, name, value, options]),
  delete: (url: string, name: string) => call<void>("cookie.delete", [url, name]),
  clear: (url: string) => call<void>("cookie.clear", [url]),
};

/** 深度链接（命令前缀 `deeplink.*`）。 */
export const deeplink = {
  getCurrent: () => call<string | null>("deeplink.getCurrent", []),
  register: (scheme: string) => call<void>("deeplink.register", [scheme]),
  unregister: (scheme: string) => call<void>("deeplink.unregister", [scheme]),
};

/** 外部打开（命令前缀 `opener.*`）。 */
export const opener = {
  openUrl: (url: string) => call<void>("opener.openUrl", [url]),
  openPath: (path: string) => call<void>("opener.openPath", [path]),
  revealInFolder: (path: string) => call<void>("opener.revealInFolder", [path]),
  isUrlAllowed: (url: string) => call<boolean>("opener.isUrlAllowed", [url]),
  verifyUrl: (url: string) => call<boolean>("opener.verifyUrl", [url]),
};
