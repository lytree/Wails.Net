/**
 * 应用 / 应用信息 / 开机自启 / 进程 / 系统 / 电源 / 操作系统信息 命令封装。
 */
import { call } from "../core/runtime.js";
/** 应用运行信息（命令前缀 `application.*`）。 */
export const application = {
    getName: () => call("application.getName", []),
    getVersion: () => call("application.getVersion", []),
    getDescription: () => call("application.getDescription", []),
    getAccentColor: () => call("application.getAccentColor", []),
    getPrimaryScreen: () => call("application.getPrimaryScreen", []),
    getScreens: () => call("application.getScreens", []),
    hide: () => call("application.hide", []),
    show: () => call("application.show", []),
    quit: () => call("application.quit", []),
    isDarkMode: () => call("application.isDarkMode", []),
    /** 设置应用图标（Base64 字符串，对应后端 `ApplicationIconOptions.IconData`）。 */
    setIcon: (base64Data) => call("application.setIcon", [{ iconData: base64Data }]),
    showAboutDialog: () => call("application.showAboutDialog", []),
};
/** 应用基本信息（AppInfo 插件，命令前缀 `app.*`）。 */
export const app = {
    getName: () => call("app.getName", []),
    getVersion: () => call("app.getVersion", []),
    getDescription: () => call("app.getDescription", []),
    getTauriVersion: () => call("app.getTauriVersion", []),
};
/** 开机自启（命令前缀 `autostart.*`）。 */
export const autostart = {
    enable: () => call("autostart.enable", []),
    disable: () => call("autostart.disable", []),
    isEnabled: () => call("autostart.isEnabled", []),
};
/** 进程控制（命令前缀 `process.*`）。 */
export const process = {
    exit: (code = 0) => call("process.exit", [code]),
    restart: () => call("process.restart", []),
    relaunch: () => call("process.relaunch", []),
    getPid: () => call("process.getPid", []),
};
/** 操作系统信息（OsInfo 插件，命令前缀 `os.*`）。 */
export const os = {
    platform: () => call("os.platform", []),
    arch: () => call("os.arch", []),
    version: () => call("os.version", []),
    type: () => call("os.type", []),
    hostname: () => call("os.hostname", []),
    locale: () => call("os.locale", []),
};
/** 系统信息（OsInfo 插件的 `system.*` 别名）。 */
export const system = {
    platform: () => call("system.platform", []),
    arch: () => call("system.arch", []),
    version: () => call("system.version", []),
    type: () => call("system.type", []),
    hostname: () => call("system.hostname", []),
    locale: () => call("system.locale", []),
    timezone: () => call("system.timezone", []),
};
/**
 * 电源管理（命令前缀 `power.*`）。
 * 后端 `PowerManagementPlugin` 的唤醒锁为进程级全局锁，命令不接收参数。
 */
export const power = {
    requestWakeLock: () => call("power.requestWakeLock", []),
    releaseWakeLock: () => call("power.releaseWakeLock", []),
    isWakeLockHeld: () => call("power.isWakeLockHeld", []),
};
//# sourceMappingURL=application.js.map