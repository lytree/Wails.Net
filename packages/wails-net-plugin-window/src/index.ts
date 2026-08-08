/**
 * @wails-net/plugin-window — window 插件前端封装。
 * 命令前缀 `window.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos  桌面通用插件。
 */
import { call } from "@wails-net/runtime";
import type { Point, Size } from "@wails-net/runtime";

/** 单个窗口控制（命令前缀 `window.*`）。 */
export const window = {
  setTitle: (title: string) => call<void>("window.setTitle", [{ title }]),
  setSize: (width: number, height: number) => call<void>("window.setSize", [{ width, height }]),
  setMinSize: (width: number, height: number) =>
    call<void>("window.setMinSize", [{ width, height }]),
  setMaxSize: (width: number, height: number) =>
    call<void>("window.setMaxSize", [{ width, height }]),
  getSize: () => call<Size>("window.getSize", []),
  setPosition: (x: number, y: number) => call<void>("window.setPosition", [{ x, y }]),
  getPosition: () => call<Point>("window.getPosition", []),
  centre: () => call<void>("window.centre", []),
  show: () => call<void>("window.show", []),
  hide: () => call<void>("window.hide", []),
  close: () => call<void>("window.close", []),
  focus: () => call<void>("window.focus", []),
  isFocused: () => call<boolean>("window.isFocused", []),
  isVisible: () => call<boolean>("window.isVisible", []),
  minimize: () => call<void>("window.minimize", []),
  unminimize: () => call<void>("window.unminimize", []),
  isMinimised: () => call<boolean>("window.isMinimised", []),
  maximize: () => call<void>("window.maximize", []),
  unmaximize: () => call<void>("window.unmaximize", []),
  isMaximised: () => call<boolean>("window.isMaximised", []),
  restore: () => call<void>("window.restore", []),
  setFullscreen: (fullscreen: boolean) => call<void>("window.setFullscreen", [{ fullscreen }]),
  unfullscreen: () => call<void>("window.unfullscreen", []),
  isFullscreen: () => call<boolean>("window.isFullscreen", []),
  setAlwaysOnTop: (onTop: boolean) => call<void>("window.setAlwaysOnTop", [{ onTop }]),
  setResizable: (resizable: boolean) => call<void>("window.setResizable", [{ resizable }]),
  setFrameless: (frameless: boolean) => call<void>("window.setFrameless", [{ frameless }]),
  setSkipTaskbar: (skip: boolean) => call<void>("window.setSkipTaskbar", [{ skip }]),
  setIgnoreCursorEvents: (ignore: boolean) =>
    call<void>("window.setIgnoreCursorEvents", [{ ignore }]),
  setVisibleOnAllWorkspaces: (visible: boolean) =>
    call<void>("window.setVisibleOnAllWorkspaces", [{ visible }]),
  setBorderColor: (color: string | null) => call<void>("window.setBorderColor", [{ color }]),
  setFileDropEnabled: (enabled: boolean) => call<void>("window.setFileDropEnabled", [{ enabled }]),
  setOpacity: (opacity: number) => call<void>("window.setOpacity", [{ opacity }]),
  getOpacity: () => call<number>("window.getOpacity", []),
  setBadgeCount: (count: number) => call<void>("window.setBadgeCount", [{ count }]),
  setBadgeLabel: (label: string | null) => call<void>("window.setBadgeLabel", [{ label }]),
  setURL: (url: string) => call<void>("window.setURL", [{ url }]),
  getURL: () => call<string>("window.getURL", []),
  setHTML: (html: string) => call<void>("window.setHTML", [{ html }]),
  reload: () => call<void>("window.reload", []),
  goBack: () => call<void>("window.goBack", []),
  goForward: () => call<void>("window.goForward", []),
  execJS: (js: string) => call<void>("window.execJS", [{ js }]),
  injectCSS: (css: string) => call<void>("window.injectCSS", [{ css }]),
  setZoom: (zoom: number) => call<void>("window.setZoom", [{ zoom }]),
  getZoom: () => call<number>("window.getZoom", []),
  zoomIn: () => call<void>("window.zoomIn", []),
  zoomOut: () => call<void>("window.zoomOut", []),
  zoomReset: () => call<void>("window.zoomReset", []),
  openDevTools: () => call<void>("window.openDevTools", []),
  closeDevTools: () => call<void>("window.closeDevTools", []),
  print: () => call<void>("window.print", []),
  /**
   * 将当前窗口内容打印为 PDF 并写入指定路径。
   * 后端签名为 `Action<ICommandContext, WindowPrintToPdfOptions>`，无返回值。
   * @param path 目标 PDF 文件路径（必填）。
   * @param options 可选的打印参数。
   */
  printToPDF: (path: string, options?: PrintToPdfOptions) =>
    call<void>("window.printToPDF", [{ path, options }]),
  registerCustomScheme: (scheme: string) => call<void>("window.registerCustomScheme", [{ scheme }]),
};

/** `window.printToPDF` 的可选打印参数。 */
export interface PrintToPdfOptions {
  /** 是否横向。 */
  landscape?: boolean;
  /** 是否打印背景图形。 */
  printBackground?: boolean;
  /** 缩放比例。 */
  scale?: number;
  /** 纸张宽度（英寸）。 */
  paperWidth?: number;
  /** 纸张高度（英寸）。 */
  paperHeight?: number;
  /** 其他扩展字段。 */
  [key: string]: unknown;
}
