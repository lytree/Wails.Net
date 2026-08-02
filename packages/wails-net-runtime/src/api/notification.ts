/**
 * 通知 / Shell 命令封装。
 */
import { call } from "../core/runtime.js";
import type { NotificationOptions } from "./common.js";

/** 系统通知（命令前缀 `notification.*`）。 */
export const notification = {
  show: (options: NotificationOptions) => call<void>("notification.show", [options]),
  showWithId: (options: NotificationOptions, id?: string) =>
    call<string>("notification.showWithId", [options, id]),
  cancel: (id: string) => call<boolean>("notification.cancel", [id]),
  requestPermission: () => call<boolean>("notification.requestPermission", []),
  isPermissionGranted: () => call<boolean>("notification.isPermissionGranted", []),
  hasPermission: () => call<boolean>("notification.hasPermission", []),
};

/** 打开文件 / URL（命令前缀 `shell.*`）。 */
export const shell = {
  execute: (command: string, args?: string[], cwd?: string) => call<number>("shell.execute", [command, args, cwd]),
  executeAsync: (command: string, args?: string[], cwd?: string) =>
    call<number>("shell.executeAsync", [command, args, cwd]),
  open: (target: string) => call<void>("shell.open", [target]),
  openUrl: (url: string) => call<void>("shell.openUrl", [url]),
};
