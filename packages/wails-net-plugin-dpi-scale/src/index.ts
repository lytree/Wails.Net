/**
 * @wails-net/plugin-dpi-scale — dpi-scale 插件前端封装。
 * 命令前缀 `dpi-scale.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/** dpi-scale.setZoomFactor 命令参数（对应后端 DpiScaleZoomOptions）。 */
export interface DpiScaleZoomOptions {
  /** 缩放因子（1.0 为 100%）。 */
  zoom: number;
}

/**
 * dpi-scale 插件前端 API（命令前缀 `dpi-scale.*`）。
 * 对应后端 Wails.Net.Plugins.Dpiscale.DpiScalePlugin 注册的命令。
 */
export const dpiScale = {
  /** 查询当前窗口的缩放因子（对应 `dpi-scale.getScaleFactor`）。 */
  getScaleFactor: () => call<number>("dpi-scale.getScaleFactor", []),
  /** 设置当前窗口的缩放因子（对应 `dpi-scale.setZoomFactor`）。 */
  setZoomFactor: (options: DpiScaleZoomOptions) =>
    call<void>("dpi-scale.setZoomFactor", [options]),
  /** 重置当前窗口缩放为默认值 1.0（对应 `dpi-scale.reset`）。 */
  reset: () => call<void>("dpi-scale.reset", []),
};