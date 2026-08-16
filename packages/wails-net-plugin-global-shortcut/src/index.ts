/**
 * @wails-net/plugin-global-shortcut — global-shortcut 插件前端封装。
 * 命令前缀 `globalshortcut.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * global-shortcut 插件前端 API（命令前缀 `globalshortcut.*`）。
 * 对应后端 Wails.Net.Plugins.GlobalShortcut.GlobalShortcutPlugin 注册的命令。
 * 快捷键触发时后端会发布 `globalshortcut:pressed` 事件。
 */
export const globalShortcut = {
  /** 注册全局快捷键（accelerator 如 `"Ctrl+Shift+P"`）。 */
  register: (accelerator: string) => call<void>("globalshortcut.register", [accelerator]),
  /** 注销指定全局快捷键。 */
  unregister: (accelerator: string) => call<void>("globalshortcut.unregister", [accelerator]),
  /** 注销全部已注册的全局快捷键。 */
  unregisterAll: () => call<void>("globalshortcut.unregisterAll", []),
  /** 查询指定快捷键是否已注册。 */
  isRegistered: (accelerator: string) => call<boolean>("globalshortcut.isRegistered", [accelerator]),
};