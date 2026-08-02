/**
 * 日志 / 全局快捷键 / 更新器 / CLI 命令封装。
 */
import { call } from "../core/runtime.js";

/** 日志（命令前缀 `log.*`）。 */
export const log = {
  trace: (message: string) => call<void>("log.trace", [message]),
  debug: (message: string) => call<void>("log.debug", [message]),
  info: (message: string) => call<void>("log.info", [message]),
  warn: (message: string) => call<void>("log.warn", [message]),
  error: (message: string) => call<void>("log.error", [message]),
  log: (message: string) => call<void>("log.log", [message]),
  logStructured: (level: string, fields: Record<string, unknown>) =>
    call<void>("log.logStructured", [level, fields]),
};

/** 全局快捷键（命令前缀 `globalshortcut.*`）。 */
export const globalshortcut = {
  register: (accelerator: string, handlerName?: string) =>
    call<void>("globalshortcut.register", [accelerator, handlerName]),
  unregister: (accelerator: string) => call<void>("globalshortcut.unregister", [accelerator]),
  unregisterAll: () => call<void>("globalshortcut.unregisterAll", []),
  isRegistered: (accelerator: string) => call<boolean>("globalshortcut.isRegistered", [accelerator]),
};

/** 自动更新（命令前缀 `updater.*`）。 */
export const updater = {
  check: (options?: unknown) => call<unknown>("updater.check", [options]),
  download: (options?: unknown) => call<unknown>("updater.download", [options]),
  checkAndDownload: (options?: unknown) => call<unknown>("updater.checkAndDownload", [options]),
  install: (options?: unknown) => call<unknown>("updater.install", [options]),
};

/** CLI 参数匹配（命令前缀 `cli.*`）。 */
export const cli = {
  getMatches: () => call<unknown[]>("cli.getMatches", []),
};
