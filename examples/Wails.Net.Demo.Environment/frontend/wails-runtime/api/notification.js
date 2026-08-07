/**
 * 通知 / Shell 命令封装。
 */
import { call } from "../core/runtime.js";
/** 系统通知（命令前缀 `notification.*`）。 */
export const notification = {
    show: (options) => call("notification.show", [options]),
    showWithId: (options, id) => call("notification.showWithId", [options, id]),
    cancel: (id) => call("notification.cancel", [id]),
    requestPermission: () => call("notification.requestPermission", []),
    isPermissionGranted: () => call("notification.isPermissionGranted", []),
    hasPermission: () => call("notification.hasPermission", []),
};
/**
 * 打开文件 / URL（命令前缀 `shell.*`）。
 * `shell.execute` / `shell.executeAsync` 后端签名为 `(command, args?, cwd?)`，
 * 但 `cwd` 不被支持，故仅传 `(command, args)`。返回 `ShellResult`。
 */
export const shell = {
    execute: (command, args) => call("shell.execute", [command, args]),
    executeAsync: (command, args) => call("shell.executeAsync", [command, args]),
    open: (target) => call("shell.open", [target]),
    openUrl: (url) => call("shell.openUrl", [url]),
};
//# sourceMappingURL=notification.js.map