/**
 * 通知 / Shell 命令封装。
 */
import { call } from "../core/runtime.js";
import type { NotificationOptions, ShellResult } from "./common.js";

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

/**
 * 打开文件 / URL（命令前缀 `shell.*`）。
 * `shell.execute` / `shell.executeAsync` 后端签名为 `(command, args?, cwd?)`，
 * 但 `cwd` 不被支持，故仅传 `(command, args)`。返回 `ShellResult`。
 */
export const shell = {
  execute: (command: string, args?: string[]) => call<ShellResult>("shell.execute", [command, args]),
  executeAsync: (command: string, args?: string[]) => call<ShellResult>("shell.executeAsync", [command, args]),
  open: (target: string) => call<void>("shell.open", [target]),
  openUrl: (url: string) => call<void>("shell.openUrl", [url]),
};
