/**
 * 窗口 / 多窗口 / 屏幕 / 托盘 / 菜单 / 定位 / 窗口状态 / DPI 相关命令封装。
 *
 * ## 线协议约定
 *
 * 全部走 `call("<ns>.<method>", args)`，`args` 最终以 `{ name, args: [...] }` 发送。
 * 后端 `MessageProcessor.TryDispatchCommandAsync` 按参数个数分发：
 *
 * - `args.length === 1` → 取 `args[0]` **整体**反序列化为该命令的唯一业务参数；
 * - `args.length > 1`  → 整个数组按位置逐个反序列化；
 * - `args.length === 0` → 传 `default`。
 *
 * 因此当后端签名为 `Action<ICommandContext, XxxOptions>` 时，前端必须发送
 * `call("ns.method", [{ field: value }])`——即把选项对象**包在数组里**，
 * 而不是把字段平铺成多个位置参数。
 */
import { call } from "../core/runtime.js";
import type {
  AboutMetadata,
  Menu,
  MenuRole,
  Point,
  ScreenInfo,
  Size,
  WindowInfo,
} from "./common.js";

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

/** 多窗口管理（命令前缀 `windows.*`）。 */
export const windows = {
  getCurrent: () => call<WindowInfo | null>("windows.getCurrent", []),
  getAll: () => call<WindowInfo[]>("windows.getAll", []),
  getById: (id: number) => call<WindowInfo | null>("windows.getById", [{ id }]),
  getByName: (name: string) => call<WindowInfo | null>("windows.getByName", [{ name }]),
  /**
   * 向指定窗口（或全部窗口）广播事件。
   * @param name 事件名。
   * @param data 事件数据。
   * @param targetWindowId 目标窗口 ID；省略则广播到所有窗口。
   */
  emit: (name: string, data?: unknown, targetWindowId?: number) =>
    call<void>("windows.emit", [{ name, data, targetWindowId }]),
};

/** 屏幕查询（命令前缀 `screen.*`）。 */
export const screen = {
  getAll: () => call<ScreenInfo[]>("screen.getAll", []),
  getPrimary: () => call<ScreenInfo | null>("screen.getPrimary", []),
};

/** 托盘（命令前缀 `tray.*`）。 */
export const tray = {
  show: () => call<void>("tray.show", []),
  hide: () => call<void>("tray.hide", []),
  isVisible: () => call<boolean>("tray.isVisible", []),
  /** @param iconData 图标数据（Base64 字符串，对应后端 `byte[]`）。 */
  setIcon: (iconData: string | null) => call<void>("tray.setIcon", [{ iconData }]),
  setLabel: (label: string) => call<void>("tray.setLabel", [{ label }]),
  setMenu: (menu: Menu | null) => call<void>("tray.setMenu", [{ menu }]),
  setTooltip: (tooltip: string) => call<void>("tray.setTooltip", [{ tooltip }]),
  destroy: () => call<void>("tray.destroy", []),
};

/** 应用菜单（命令前缀 `menu.*`）。 */
export const menu = {
  setApplicationMenu: (menu: Menu | null) => call<void>("menu.setApplicationMenu", [{ menu }]),
  getApplicationMenu: () => call<Menu | null>("menu.getApplicationMenu", []),
  /**
   * 注册一个具名上下文菜单，供元素通过 `data-contextmenu="<id>"` 引用。
   * @param id 上下文菜单标识。
   * @param menu 菜单定义。
   */
  setContextMenu: (id: string, menu: Menu | null) =>
    call<void>("menu.setContextMenu", [{ id, menu }]),
  updateMenuItem: (id: string, properties: Record<string, unknown>) =>
    call<void>("menu.updateMenuItem", [{ id, properties }]),
  /**
   * 在指定坐标弹出上下文菜单。
   * @param id 已注册的上下文菜单 ID；省略则使用默认上下文菜单。
   * @param x 屏幕/窗口坐标 X。
   * @param y 坐标 Y。
   * @param data 透传给菜单回调的附加数据。
   */
  popup: (id: string | null, x: number, y: number, data?: string) =>
    call<void>("menu.popup", [{ id, x, y, data }]),
  /**
   * 向指定父菜单追加一个预定义角色项。
   * @returns 新建菜单项的 ID。
   */
  addRoleItem: (parentId: string, role: MenuRole, label?: string) =>
    call<string>("menu.addRoleItem", [{ parentId, role, label }]),
  addStandardEditMenu: (parentId: string) =>
    call<void>("menu.addStandardEditMenu", [{ parentId }]),
  addStandardWindowMenu: (parentId: string) =>
    call<void>("menu.addStandardWindowMenu", [{ parentId }]),
  addStandardHelpMenu: (parentId: string, metadata?: AboutMetadata, label?: string) =>
    call<void>("menu.addStandardHelpMenu", [{ parentId, metadata, label }]),
};

/**
 * 窗口定位（命令前缀 `positioner.*`）。
 *
 * 后端每个命令的**首个参数都是 `windowName`**（空串表示第一个窗口），
 * 因此所有方法都接受可选的 `windowName`，默认 `""`。
 */
export const positioner = {
  center: (windowName = "") => call<void>("positioner.center", [windowName]),
  move: (x: number, y: number, windowName = "") =>
    call<void>("positioner.move", [windowName, x, y]),
  /**
   * 将窗口移动到屏幕的相对位置。
   * @param position 位置关键字：`topLeft` / `topRight` / `bottomLeft` / `bottomRight` /
   *                 `center` / `top` / `bottom` / `left` / `right`。
   */
  moveRelativeTo: (position: RelativePosition, windowName = "") =>
    call<void>("positioner.moveRelativeTo", [windowName, position]),
  moveToCursor: (windowName = "") => call<void>("positioner.moveToCursor", [windowName]),
  /**
   * 获取窗口当前位置。
   * 后端返回的是 JSON **字符串**（如 `{"x":10,"y":20}`），这里解析后返回对象。
   */
  getPosition: async (windowName = ""): Promise<Point> => {
    const raw = await call<string>("positioner.getPosition", [windowName]);
    try {
      const parsed = JSON.parse(raw) as Partial<Point>;
      return { x: parsed.x ?? 0, y: parsed.y ?? 0 };
    } catch {
      return { x: 0, y: 0 };
    }
  },
};

/** `positioner.moveRelativeTo` 支持的位置关键字。 */
export type RelativePosition =
  | "topLeft"
  | "topRight"
  | "bottomLeft"
  | "bottomRight"
  | "center"
  | "top"
  | "bottom"
  | "left"
  | "right";

/** 窗口状态持久化（命令前缀 `windowstate.*`）。 */
export const windowstate = {
  save: () => call<void>("windowstate.save", []),
  restore: () => call<void>("windowstate.restore", []),
  clear: () => call<void>("windowstate.clear", []),
};

/** DPI 缩放（命令前缀 `dpi-scale.*`）。 */
export const dpiScale = {
  getScaleFactor: () => call<number>("dpi-scale.getScaleFactor", []),
  /** @param zoom 缩放因子（后端字段名为 `zoom`）。 */
  setZoomFactor: (zoom: number) => call<void>("dpi-scale.setZoomFactor", [{ zoom }]),
  reset: () => call<void>("dpi-scale.reset", []),
};
