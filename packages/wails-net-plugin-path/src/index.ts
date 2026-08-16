/**
 * @wails-net/plugin-path — path 插件前端封装。
 * 命令前缀 `path.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * path 插件前端 API（命令前缀 `path.*`）。
 * 对应后端 Wails.Net.Plugins.Path.PathPlugin 注册的命令。
 * 所有方法返回对应系统目录的绝对路径字符串。
 */
export const path = {
  /** 获取应用数据目录（AppData + 应用名）。 */
  appDataDir: () => call<string>("path.appDataDir", []),
  /** 获取应用配置目录（Windows: AppData；Unix: ~/.config + 应用名）。 */
  appConfigDir: () => call<string>("path.appConfigDir", []),
  /** 获取应用日志目录（AppData + 应用名 + logs）。 */
  appLogDir: () => call<string>("path.appLogDir", []),
  /** 获取应用缓存目录（系统临时目录 + 应用名）。 */
  appCacheDir: () => call<string>("path.appCacheDir", []),
  /** 获取下载目录。 */
  downloadDir: () => call<string>("path.downloadDir", []),
  /** 获取文档目录。 */
  documentDir: () => call<string>("path.documentDir", []),
  /** 获取用户主目录。 */
  homeDir: () => call<string>("path.homeDir", []),
  /** 获取系统临时目录。 */
  tempDir: () => call<string>("path.tempDir", []),
  /** 获取配置根目录（AppData，不含应用名）。 */
  configDir: () => call<string>("path.configDir", []),
  /** 获取数据根目录（AppData + 应用名）。 */
  dataDir: () => call<string>("path.dataDir", []),
  /** 获取运行时目录（系统临时目录）。 */
  runtimeDir: () => call<string>("path.runtimeDir", []),
};