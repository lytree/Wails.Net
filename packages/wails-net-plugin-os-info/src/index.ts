/**
 * @wails-net/plugin-os-info — os-info 插件前端封装。
 * 命令前缀 `os.*` 与 `system.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * OS 信息插件（`os.*` 命令集，历史名，保留向后兼容）。
 * 对应后端 Wails.Net.Plugins.OsInfo.OsInfoPlugin 注册的 `os.*` 命令。
 */
export const os = {
  /** 获取当前平台标识（如 `"windows"` / `"linux"`）。 */
  platform: () => call<string>("os.platform", []),
  /** 获取主机名。 */
  hostname: () => call<string>("os.hostname", []),
  /** 获取 CPU 架构（小写，如 `"x64"` / `"arm64"`）。 */
  arch: () => call<string>("os.arch", []),
  /** 获取当前区域设置（culture 名，如 `"zh-CN"`）。 */
  locale: () => call<string>("os.locale", []),
  /** 获取操作系统版本字符串。 */
  version: () => call<string>("os.version", []),
  /** 获取系统类型（如 `"Windows"` / `"Linux"`）。 */
  type: () => call<string>("os.type", []),
};

/**
 * 系统信息 API（`system.*` 命令集，与前端 `wails.system.*` API 一致）。
 * 对应后端 Wails.Net.Plugins.OsInfo.OsInfoPlugin 注册的 `system.*` 命令。
 */
export const system = {
  /** 获取当前平台标识（如 `"windows"` / `"linux"`）。 */
  platform: () => call<string>("system.platform", []),
  /** 获取主机名。 */
  hostname: () => call<string>("system.hostname", []),
  /** 获取 CPU 架构（小写，如 `"x64"` / `"arm64"`）。 */
  arch: () => call<string>("system.arch", []),
  /** 获取当前区域设置（culture 名，如 `"zh-CN"`）。 */
  locale: () => call<string>("system.locale", []),
  /** 获取操作系统版本字符串。 */
  version: () => call<string>("system.version", []),
  /** 获取系统类型（如 `"Windows"` / `"Linux"`）。 */
  type: () => call<string>("system.type", []),
  /** 获取系统时区 ID（如 `"Asia/Shanghai"`）。 */
  timezone: () => call<string>("system.timezone", []),
};