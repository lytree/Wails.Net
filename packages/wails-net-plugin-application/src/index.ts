/**
 * @wails-net/plugin-application — application 插件前端封装。
 * 命令前缀 `application.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";
import type { ScreenInfo } from "@wails-net/runtime";

/** `application.setIcon` 命令参数。对应后端 `ApplicationIconOptions`（byte[] 以 Base64 字符串表示）。 */
export interface ApplicationIconOptions {
  /** 图标二进制数据（Base64 字符串），可为空。 */
  iconData?: string;
}

/**
 * application 插件前端 API（命令前缀 `application.*`）。
 * 对应后端 Wails.Net.Plugins.Application.ApplicationPlugin 注册的命令。
 * 屏幕信息复用运行时共享类型 {@link ScreenInfo}。
 */
export const application = {
  /** 退出应用。 */
  quit: () => call<void>("application.quit", []),
  /** 隐藏主窗口。 */
  hide: () => call<void>("application.hide", []),
  /** 显示主窗口。 */
  show: () => call<void>("application.show", []),
  /** 获取应用名称。 */
  getName: () => call<string>("application.getName", []),
  /** 获取应用版本。 */
  getVersion: () => call<string>("application.getVersion", []),
  /** 获取应用描述。 */
  getDescription: () => call<string>("application.getDescription", []),
  /** 设置应用图标（iconData 为 Base64 编码的图标二进制数据）。 */
  setIcon: (options: ApplicationIconOptions) => call<void>("application.setIcon", [options]),
  /** 查询当前是否为深色模式。 */
  isDarkMode: () => call<boolean>("application.isDarkMode", []),
  /** 获取系统强调色（如 `"#FF0000"`）。 */
  getAccentColor: () => call<string>("application.getAccentColor", []),
  /** 获取主屏幕信息（无主屏时为 null）。 */
  getPrimaryScreen: () => call<ScreenInfo>("application.getPrimaryScreen", []),
  /** 获取所有屏幕信息。 */
  getScreens: () => call<ScreenInfo[]>("application.getScreens", []),
  /** 显示"关于"对话框。 */
  showAboutDialog: () => call<void>("application.showAboutDialog", []),
};