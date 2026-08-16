/**
 * @wails-net/plugin-autostart — autostart 插件前端封装。
 * 命令前缀 `autostart.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * autostart 插件前端 API（命令前缀 `autostart.*`）。
 * 对应后端 Wails.Net.Plugins.Autostart.AutostartPlugin 注册的命令。
 * Windows 通过注册表、Linux 通过 .desktop 文件实现开机自启动。
 */
export const autostart = {
  /** 启用开机自启动，成功返回 true。 */
  enable: () => call<boolean>("autostart.enable", []),
  /** 禁用开机自启动，成功返回 true。 */
  disable: () => call<boolean>("autostart.disable", []),
  /** 查询开机自启动是否已启用。 */
  isEnabled: () => call<boolean>("autostart.isEnabled", []),
};