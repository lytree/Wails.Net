/**
 * @wails-net/plugin-screen — screen 插件前端封装。
 * 命令前缀 `screen.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";
import type { ScreenInfo } from "@wails-net/runtime";

/**
 * screen 插件前端 API（命令前缀 `screen.*`）。
 * 对应后端 Wails.Net.Plugins.Screen.ScreenPlugin 注册的命令。
 * 返回的屏幕信息复用运行时共享类型 {@link ScreenInfo}。
 */
export const screen = {
  /** 获取所有屏幕信息。 */
  getAll: () => call<ScreenInfo[]>("screen.getAll", []),
  /** 获取主屏幕信息（无主屏时为 null）。 */
  getPrimary: () => call<ScreenInfo>("screen.getPrimary", []),
};