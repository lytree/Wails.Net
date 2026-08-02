/**
 * 窗口 / 多窗口 / 屏幕 / 托盘 / 菜单 / 定位 / 窗口状态 / DPI 相关命令封装。
 * 全部走 `wails.call("<ns>.<method>", [...args])`。
 */
import { call } from "../core/runtime.js";
import type { Menu, MenuItem, Point, ScreenInfo, WindowInfo } from "./common.js";

/** 单个窗口控制（命令前缀 `window.*`）。 */
export const window = {
  setTitle: (title: string) => call<void>("window.setTitle", [title]),
  setSize: (width: number, height: number) => call<void>("window.setSize", [width, height]),
  setMinSize: (width: number, height: number) => call<void>("window.setMinSize", [width, height]),
  setMaxSize: (width: number, height: number) => call<void>("window.setMaxSize", [width, height]),
  getSize: () => call<[number, number]>("window.getSize", []),
  setPosition: (x: number, y: number) => call<void>("window.setPosition", [x, y]),
  getPosition: () => call<[number, number]>("window.getPosition", []),
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
  setFullscreen: (fullscreen: boolean) => call<void>("window.setFullscreen", [fullscreen]),
  unfullscreen: () => call<void>("window.unfullscreen", []),
  isFullscreen: () => call<boolean>("window.isFullscreen", []),
  setAlwaysOnTop: (onTop: boolean) => call<void>("window.setAlwaysOnTop", [onTop]),
  setResizable: (resizable: boolean) => call<void>("window.setResizable", [resizable]),
  setFrameless: (frameless: boolean) => call<void>("window.setFrameless", [frameless]),
  setSkipTaskbar: (skip: boolean) => call<void>("window.setSkipTaskbar", [skip]),
  setIgnoreCursorEvents: (ignore: boolean) => call<void>("window.setIgnoreCursorEvents", [ignore]),
  setVisibleOnAllWorkspaces: (visible: boolean) => call<void>("window.setVisibleOnAllWorkspaces", [visible]),
  setBorderColor: (color: string) => call<void>("window.setBorderColor", [color]),
  setFileDropEnabled: (enabled: boolean) => call<void>("window.setFileDropEnabled", [enabled]),
  setOpacity: (opacity: number) => call<void>("window.setOpacity", [opacity]),
  getOpacity: () => call<number>("window.getOpacity", []),
  setBadgeCount: (count: number) => call<void>("window.setBadgeCount", [count]),
  setBadgeLabel: (label: string) => call<void>("window.setBadgeLabel", [label]),
  setURL: (url: string) => call<void>("window.setURL", [url]),
  getURL: () => call<string>("window.getURL", []),
  setHTML: (html: string) => call<void>("window.setHTML", [html]),
  reload: () => call<void>("window.reload", []),
  goBack: () => call<void>("window.goBack", []),
  goForward: () => call<void>("window.goForward", []),
  execJS: (js: string) => call<void>("window.execJS", [js]),
  injectCSS: (css: string) => call<void>("window.injectCSS", [css]),
  setZoom: (zoom: number) => call<void>("window.setZoom", [zoom]),
  getZoom: () => call<number>("window.getZoom", []),
  zoomIn: () => call<void>("window.zoomIn", []),
  zoomOut: () => call<void>("window.zoomOut", []),
  zoomReset: () => call<void>("window.zoomReset", []),
  openDevTools: () => call<void>("window.openDevTools", []),
  closeDevTools: () => call<void>("window.closeDevTools", []),
  print: () => call<void>("window.print", []),
  printToPDF: () => call<string>("window.printToPDF", []),
  registerCustomScheme: (scheme: string) => call<void>("window.registerCustomScheme", [scheme]),
};

/** 多窗口管理（命令前缀 `windows.*`）。 */
export const windows = {
  getCurrent: () => call<WindowInfo>("windows.getCurrent", []),
  getAll: () => call<WindowInfo[]>("windows.getAll", []),
  getById: (id: number) => call<WindowInfo>("windows.getById", [id]),
  getByName: (name: string) => call<WindowInfo>("windows.getByName", [name]),
  emit: (name: string, data: unknown, targetWindowId?: number) =>
    call<void>("windows.emit", [name, data, targetWindowId]),
};

/** 屏幕查询（命令前缀 `screen.*`）。 */
export const screen = {
  getAll: () => call<ScreenInfo[]>("screen.getAll", []),
  getPrimary: () => call<ScreenInfo>("screen.getPrimary", []),
};

/** 托盘（命令前缀 `tray.*`）。 */
export const tray = {
  show: () => call<void>("tray.show", []),
  hide: () => call<void>("tray.hide", []),
  isVisible: () => call<boolean>("tray.isVisible", []),
  setIcon: (iconData: string) => call<void>("tray.setIcon", [iconData]),
  setLabel: (label: string) => call<void>("tray.setLabel", [label]),
  setMenu: (menu: Menu) => call<void>("tray.setMenu", [menu]),
  setTooltip: (tooltip: string) => call<void>("tray.setTooltip", [tooltip]),
  destroy: () => call<void>("tray.destroy", []),
};

/** 应用菜单（命令前缀 `menu.*`）。 */
export const menu = {
  setApplicationMenu: (menu: Menu) => call<void>("menu.setApplicationMenu", [menu]),
  getApplicationMenu: () => call<Menu>("menu.getApplicationMenu", []),
  setContextMenu: (menu: Menu) => call<void>("menu.setContextMenu", [menu]),
  updateMenuItem: (id: string, properties: Partial<MenuItem>) =>
    call<void>("menu.updateMenuItem", [id, properties]),
  popup: (menu: Menu, x?: number, y?: number) => call<void>("menu.popup", [menu, x, y]),
  addRoleItem: (parentId: string, role: string, label?: string) =>
    call<void>("menu.addRoleItem", [parentId, role, label]),
  addStandardEditMenu: (parentId: string) => call<void>("menu.addStandardEditMenu", [parentId]),
  addStandardWindowMenu: (parentId: string) => call<void>("menu.addStandardWindowMenu", [parentId]),
  addStandardHelpMenu: (parentId: string) => call<void>("menu.addStandardHelpMenu", [parentId]),
};

/** 窗口定位（命令前缀 `positioner.*`）。 */
export const positioner = {
  center: () => call<Point>("positioner.center", []),
  move: (x: number, y: number) => call<Point>("positioner.move", [x, y]),
  moveRelativeTo: (rect: { x: number; y: number; width: number; height: number }) =>
    call<Point>("positioner.moveRelativeTo", [rect]),
  moveToCursor: () => call<Point>("positioner.moveToCursor", []),
  getPosition: () => call<Point>("positioner.getPosition", []),
};

/** 窗口状态持久化（命令前缀 `windowstate.*`）。 */
export const windowstate = {
  save: () => call<void>("windowstate.save", []),
  restore: () => call<void>("windowstate.restore", []),
  clear: () => call<void>("windowstate.clear", []),
};

/** DPI 缩放（命令前缀 `dpi-scale.*`）。 */
export const dpiScale = {
  getScaleFactor: () => call<number>("dpi-scale.getScaleFactor", []),
  setZoomFactor: (factor: number) => call<void>("dpi-scale.setZoomFactor", [factor]),
  reset: () => call<void>("dpi-scale.reset", []),
};
