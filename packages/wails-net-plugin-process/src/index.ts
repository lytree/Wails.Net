/**
 * @wails-net/plugin-process — process 插件前端封装。
 * 命令前缀 `process.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * process 插件前端 API（命令前缀 `process.*`）。
 * 对应后端 Wails.Net.Plugins.Process.ProcessPlugin 注册的命令。
 */
export const process = {
  /** 退出应用进程（exitCode 为退出码）。 */
  exit: (exitCode: number) => call<void>("process.exit", [exitCode]),
  /** 重启应用：以当前进程路径和参数启动新进程后退出当前进程。 */
  restart: () => call<void>("process.restart", []),
  /** 重新启动应用（与 restart 等价）。 */
  relaunch: () => call<void>("process.relaunch", []),
  /** 获取当前进程 ID。 */
  getPid: () => call<number>("process.getPid", []),
};