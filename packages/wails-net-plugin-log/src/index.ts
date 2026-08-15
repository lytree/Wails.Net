/**
 * @wails-net/plugin-log — log 插件前端封装。
 * 命令前缀 `log.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/** 日志级别，与后端 LogPlugin 支持的命令一一对应。 */
export type LogLevel = "trace" | "debug" | "info" | "warn" | "error";

/** 结构化日志字段（键值对，值为 JSON 可序列化类型）。 */
export type LogFields = Record<string, unknown>;

/**
 * log 插件前端 API（命令前缀 `log.*`）。
 * 对应后端 Wails.Net.Plugins.Log.LogPlugin 注册的命令。
 */
export const log = {
  /** 输出 trace 级别日志。 */
  trace: (message: string) => call<void>("log.trace", [message]),
  /** 输出 debug 级别日志。 */
  debug: (message: string) => call<void>("log.debug", [message]),
  /** 输出 info 级别日志。 */
  info: (message: string) => call<void>("log.info", [message]),
  /** 输出 warn 级别日志。 */
  warn: (message: string) => call<void>("log.warn", [message]),
  /** 输出 error 级别日志。 */
  error: (message: string) => call<void>("log.error", [message]),
  /** 按指定级别输出日志（level 取值 trace/debug/info/warn/error）。 */
  log: (level: LogLevel, message: string) => call<void>("log.log", [level, message]),
  /** 输出结构化日志（fields 为键值对，后端以 JSON 形式记录）。 */
  logStructured: (level: LogLevel, message: string, fields: LogFields) =>
    call<void>("log.logStructured", [level, message, JSON.stringify(fields)]),
};
