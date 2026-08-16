/**
 * @wails-net/plugin-app-info — app-info 插件前端封装。
 * 命令前缀 `app.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * app-info 插件前端 API（命令前缀 `app.*`）。
 * 对应后端 Wails.Net.Plugins.AppInfo.AppInfoPlugin 注册的命令。
 */
export const app = {
  /** 获取应用名称。 */
  getName: () => call<string>("app.getName", []),
  /** 获取应用版本。 */
  getVersion: () => call<string>("app.getVersion", []),
  /** 获取应用描述。 */
  getDescription: () => call<string>("app.getDescription", []),
  /** 获取框架（Wails.Net）版本。 */
  getTauriVersion: () => call<string>("app.getTauriVersion", []),
};