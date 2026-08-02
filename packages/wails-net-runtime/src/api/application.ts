/**
 * 应用 / 应用信息 / 开机自启 / 进程 / 系统 / 电源 / 操作系统信息 命令封装。
 */
import { call } from "../core/runtime.js";
import type { ScreenInfo } from "./common.js";

/** 应用运行信息（命令前缀 `application.*`）。 */
export const application = {
  getName: () => call<string>("application.getName", []),
  getVersion: () => call<string>("application.getVersion", []),
  getDescription: () => call<string>("application.getDescription", []),
  getAccentColor: () => call<string>("application.getAccentColor", []),
  getPrimaryScreen: () => call<ScreenInfo>("application.getPrimaryScreen", []),
  getScreens: () => call<ScreenInfo[]>("application.getScreens", []),
  hide: () => call<void>("application.hide", []),
  show: () => call<void>("application.show", []),
  quit: () => call<void>("application.quit", []),
  isDarkMode: () => call<boolean>("application.isDarkMode", []),
  /** 设置应用图标（Base64）。 */
  setIcon: (base64Data: string) => call<void>("application.setIcon", [base64Data]),
  showAboutDialog: () => call<void>("application.showAboutDialog", []),
};

/** 应用基本信息（AppInfo 插件，命令前缀 `app.*`）。 */
export const app = {
  getName: () => call<string>("app.getName", []),
  getVersion: () => call<string>("app.getVersion", []),
  getDescription: () => call<string>("app.getDescription", []),
  getTauriVersion: () => call<string>("app.getTauriVersion", []),
};

/** 开机自启（命令前缀 `autostart.*`）。 */
export const autostart = {
  enable: () => call<void>("autostart.enable", []),
  disable: () => call<void>("autostart.disable", []),
  isEnabled: () => call<boolean>("autostart.isEnabled", []),
};

/** 进程控制（命令前缀 `process.*`）。 */
export const process = {
  exit: (code = 0) => call<void>("process.exit", [code]),
  restart: () => call<void>("process.restart", []),
  relaunch: () => call<void>("process.relaunch", []),
  getPid: () => call<number>("process.getPid", []),
};

/** 操作系统信息（OsInfo 插件，命令前缀 `os.*`）。 */
export const os = {
  platform: () => call<string>("os.platform", []),
  arch: () => call<string>("os.arch", []),
  version: () => call<string>("os.version", []),
  type: () => call<string>("os.type", []),
  hostname: () => call<string>("os.hostname", []),
  locale: () => call<string>("os.locale", []),
  timezone: () => call<string>("os.timezone", []),
};

/** 系统信息（OsInfo 插件的 `system.*` 别名）。 */
export const system = {
  platform: () => call<string>("system.platform", []),
  arch: () => call<string>("system.arch", []),
  version: () => call<string>("system.version", []),
  type: () => call<string>("system.type", []),
  hostname: () => call<string>("system.hostname", []),
  locale: () => call<string>("system.locale", []),
  timezone: () => call<string>("system.timezone", []),
};

/** 电源管理（命令前缀 `power.*`）。 */
export const power = {
  requestWakeLock: (lockType?: string) => call<void>("power.requestWakeLock", [lockType]),
  releaseWakeLock: (lockType?: string) => call<void>("power.releaseWakeLock", [lockType]),
  isWakeLockHeld: (lockType?: string) => call<boolean>("power.isWakeLockHeld", [lockType]),
};
