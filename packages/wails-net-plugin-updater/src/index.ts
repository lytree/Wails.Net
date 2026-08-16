/**
 * @wails-net/plugin-updater — updater 插件前端封装。
 * 命令前缀 `updater.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/** 更新检查结果（`updater.check` 返回的 JSON 解析结果）。 */
export interface UpdateCheckResult {
  /** 最新版本号。 */
  version: string;
  /** 是否有可用更新。 */
  available: boolean;
}

/** 检查并下载结果（`updater.checkAndDownload` 返回的 JSON 解析结果）。 */
export interface CheckAndDownloadResult {
  /** 最新版本号。 */
  version: string;
  /** 是否有可用更新。 */
  available: boolean;
  /** 下载文件的本地路径；无可下载地址时为 null。 */
  path: string | null;
}

/**
 * updater 插件前端 API（命令前缀 `updater.*`）。
 * 对应后端 Wails.Net.Plugins.Updater.UpdaterPlugin 注册的命令。
 */
export const updater = {
  /** 检查是否有可用更新（对应 `updater.check`）。 */
  check: () =>
    call<string>("updater.check", []).then((json) => JSON.parse(json) as UpdateCheckResult),
  /** 下载更新包，返回下载文件的本地路径（对应 `updater.download`）。 */
  download: () => call<string>("updater.download", []),
  /** 安装指定路径的更新包（对应 `updater.install`）。 */
  install: (archivePath: string) => call<void>("updater.install", [archivePath]),
  /** 检查并下载更新，返回版本、可用性与下载路径（对应 `updater.checkAndDownload`）。 */
  checkAndDownload: () =>
    call<string>("updater.checkAndDownload", []).then(
      (json) => JSON.parse(json) as CheckAndDownloadResult,
    ),
};