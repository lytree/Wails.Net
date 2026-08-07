/**
 * 日志 / 全局快捷键 / 更新器 / CLI 命令封装。
 */
import { call } from "../core/runtime.js";
import type { CliMatches } from "./common.js";

/** 日志（命令前缀 `log.*`）。 */
export const log = {
  trace: (message: string) => call<void>("log.trace", [message]),
  debug: (message: string) => call<void>("log.debug", [message]),
  info: (message: string) => call<void>("log.info", [message]),
  warn: (message: string) => call<void>("log.warn", [message]),
  error: (message: string) => call<void>("log.error", [message]),
  // log.log(level, message)：按指定级别写入日志。
  log: (level: string, message: string) => call<void>("log.log", [level, message]),
  // log.logStructured(level, message, fieldsJson)：结构化日志，fields 字典序列化为 JSON 字符串。
  logStructured: (level: string, message: string, fields: Record<string, unknown>) =>
    call<void>("log.logStructured", [level, message, JSON.stringify(fields ?? {})]),
};

/** 全局快捷键（命令前缀 `globalshortcut.*`）。 */
export const globalshortcut = {
  // 后端 GlobalShortcutPlugin 仅接收 accelerator 字符串，无 handlerName 参数。
  register: (accelerator: string) => call<void>("globalshortcut.register", [accelerator]),
  unregister: (accelerator: string) => call<void>("globalshortcut.unregister", [accelerator]),
  unregisterAll: () => call<void>("globalshortcut.unregisterAll", []),
  isRegistered: (accelerator: string) => call<boolean>("globalshortcut.isRegistered", [accelerator]),
};

/** 自动更新（命令前缀 `updater.*`）。 */
export const updater = {
  // 返回可下载的 release 元数据（JSON 字符串）。
  check: () => call<string>("updater.check", []),
  // 返回已下载归档的本地路径。
  download: () => call<string>("updater.download", []),
  // 检查并下载，返回已下载归档的本地路径。
  checkAndDownload: () => call<string>("updater.checkAndDownload", []),
  // archivePath：已下载归档的本地路径（由 check/download/checkAndDownload 返回）。
  install: (archivePath: string) => call<void>("updater.install", [archivePath]),
};

/** CLI 参数匹配（命令前缀 `cli.*`）。 */
export const cli = {
  getMatches: () => call<CliMatches>("cli.getMatches", []),
};
