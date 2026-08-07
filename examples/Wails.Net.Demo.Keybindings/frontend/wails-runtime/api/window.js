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
/** 单个窗口控制（命令前缀 `window.*`）。 */
export const window = {
    setTitle: (title) => call("window.setTitle", [{ title }]),
    setSize: (width, height) => call("window.setSize", [{ width, height }]),
    setMinSize: (width, height) => call("window.setMinSize", [{ width, height }]),
    setMaxSize: (width, height) => call("window.setMaxSize", [{ width, height }]),
    getSize: () => call("window.getSize", []),
    setPosition: (x, y) => call("window.setPosition", [{ x, y }]),
    getPosition: () => call("window.getPosition", []),
    centre: () => call("window.centre", []),
    show: () => call("window.show", []),
    hide: () => call("window.hide", []),
    close: () => call("window.close", []),
    focus: () => call("window.focus", []),
    isFocused: () => call("window.isFocused", []),
    isVisible: () => call("window.isVisible", []),
    minimize: () => call("window.minimize", []),
    unminimize: () => call("window.unminimize", []),
    isMinimised: () => call("window.isMinimised", []),
    maximize: () => call("window.maximize", []),
    unmaximize: () => call("window.unmaximize", []),
    isMaximised: () => call("window.isMaximised", []),
    restore: () => call("window.restore", []),
    setFullscreen: (fullscreen) => call("window.setFullscreen", [{ fullscreen }]),
    unfullscreen: () => call("window.unfullscreen", []),
    isFullscreen: () => call("window.isFullscreen", []),
    setAlwaysOnTop: (onTop) => call("window.setAlwaysOnTop", [{ onTop }]),
    setResizable: (resizable) => call("window.setResizable", [{ resizable }]),
    setFrameless: (frameless) => call("window.setFrameless", [{ frameless }]),
    setSkipTaskbar: (skip) => call("window.setSkipTaskbar", [{ skip }]),
    setIgnoreCursorEvents: (ignore) => call("window.setIgnoreCursorEvents", [{ ignore }]),
    setVisibleOnAllWorkspaces: (visible) => call("window.setVisibleOnAllWorkspaces", [{ visible }]),
    setBorderColor: (color) => call("window.setBorderColor", [{ color }]),
    setFileDropEnabled: (enabled) => call("window.setFileDropEnabled", [{ enabled }]),
    setOpacity: (opacity) => call("window.setOpacity", [{ opacity }]),
    getOpacity: () => call("window.getOpacity", []),
    setBadgeCount: (count) => call("window.setBadgeCount", [{ count }]),
    setBadgeLabel: (label) => call("window.setBadgeLabel", [{ label }]),
    setURL: (url) => call("window.setURL", [{ url }]),
    getURL: () => call("window.getURL", []),
    setHTML: (html) => call("window.setHTML", [{ html }]),
    reload: () => call("window.reload", []),
    goBack: () => call("window.goBack", []),
    goForward: () => call("window.goForward", []),
    execJS: (js) => call("window.execJS", [{ js }]),
    injectCSS: (css) => call("window.injectCSS", [{ css }]),
    setZoom: (zoom) => call("window.setZoom", [{ zoom }]),
    getZoom: () => call("window.getZoom", []),
    zoomIn: () => call("window.zoomIn", []),
    zoomOut: () => call("window.zoomOut", []),
    zoomReset: () => call("window.zoomReset", []),
    openDevTools: () => call("window.openDevTools", []),
    closeDevTools: () => call("window.closeDevTools", []),
    print: () => call("window.print", []),
    /**
     * 将当前窗口内容打印为 PDF 并写入指定路径。
     * 后端签名为 `Action<ICommandContext, WindowPrintToPdfOptions>`，无返回值。
     * @param path 目标 PDF 文件路径（必填）。
     * @param options 可选的打印参数。
     */
    printToPDF: (path, options) => call("window.printToPDF", [{ path, options }]),
    registerCustomScheme: (scheme) => call("window.registerCustomScheme", [{ scheme }]),
};
/** 多窗口管理（命令前缀 `windows.*`）。 */
export const windows = {
    getCurrent: () => call("windows.getCurrent", []),
    getAll: () => call("windows.getAll", []),
    getById: (id) => call("windows.getById", [{ id }]),
    getByName: (name) => call("windows.getByName", [{ name }]),
    /**
     * 向指定窗口（或全部窗口）广播事件。
     * @param name 事件名。
     * @param data 事件数据。
     * @param targetWindowId 目标窗口 ID；省略则广播到所有窗口。
     */
    emit: (name, data, targetWindowId) => call("windows.emit", [{ name, data, targetWindowId }]),
};
/** 屏幕查询（命令前缀 `screen.*`）。 */
export const screen = {
    getAll: () => call("screen.getAll", []),
    getPrimary: () => call("screen.getPrimary", []),
};
/** 托盘（命令前缀 `tray.*`）。 */
export const tray = {
    show: () => call("tray.show", []),
    hide: () => call("tray.hide", []),
    isVisible: () => call("tray.isVisible", []),
    /** @param iconData 图标数据（Base64 字符串，对应后端 `byte[]`）。 */
    setIcon: (iconData) => call("tray.setIcon", [{ iconData }]),
    setLabel: (label) => call("tray.setLabel", [{ label }]),
    setMenu: (menu) => call("tray.setMenu", [{ menu }]),
    setTooltip: (tooltip) => call("tray.setTooltip", [{ tooltip }]),
    destroy: () => call("tray.destroy", []),
};
/** 应用菜单（命令前缀 `menu.*`）。 */
export const menu = {
    setApplicationMenu: (menu) => call("menu.setApplicationMenu", [{ menu }]),
    getApplicationMenu: () => call("menu.getApplicationMenu", []),
    /**
     * 注册一个具名上下文菜单，供元素通过 `data-contextmenu="<id>"` 引用。
     * @param id 上下文菜单标识。
     * @param menu 菜单定义。
     */
    setContextMenu: (id, menu) => call("menu.setContextMenu", [{ id, menu }]),
    updateMenuItem: (id, properties) => call("menu.updateMenuItem", [{ id, properties }]),
    /**
     * 在指定坐标弹出上下文菜单。
     * @param id 已注册的上下文菜单 ID；省略则使用默认上下文菜单。
     * @param x 屏幕/窗口坐标 X。
     * @param y 坐标 Y。
     * @param data 透传给菜单回调的附加数据。
     */
    popup: (id, x, y, data) => call("menu.popup", [{ id, x, y, data }]),
    /**
     * 向指定父菜单追加一个预定义角色项。
     * @returns 新建菜单项的 ID。
     */
    addRoleItem: (parentId, role, label) => call("menu.addRoleItem", [{ parentId, role, label }]),
    addStandardEditMenu: (parentId) => call("menu.addStandardEditMenu", [{ parentId }]),
    addStandardWindowMenu: (parentId) => call("menu.addStandardWindowMenu", [{ parentId }]),
    addStandardHelpMenu: (parentId, metadata, label) => call("menu.addStandardHelpMenu", [{ parentId, metadata, label }]),
};
/**
 * 窗口定位（命令前缀 `positioner.*`）。
 *
 * 后端每个命令的**首个参数都是 `windowName`**（空串表示第一个窗口），
 * 因此所有方法都接受可选的 `windowName`，默认 `""`。
 */
export const positioner = {
    center: (windowName = "") => call("positioner.center", [windowName]),
    move: (x, y, windowName = "") => call("positioner.move", [windowName, x, y]),
    /**
     * 将窗口移动到屏幕的相对位置。
     * @param position 位置关键字：`topLeft` / `topRight` / `bottomLeft` / `bottomRight` /
     *                 `center` / `top` / `bottom` / `left` / `right`。
     */
    moveRelativeTo: (position, windowName = "") => call("positioner.moveRelativeTo", [windowName, position]),
    moveToCursor: (windowName = "") => call("positioner.moveToCursor", [windowName]),
    /**
     * 获取窗口当前位置。
     * 后端返回的是 JSON **字符串**（如 `{"x":10,"y":20}`），这里解析后返回对象。
     */
    getPosition: async (windowName = "") => {
        const raw = await call("positioner.getPosition", [windowName]);
        try {
            const parsed = JSON.parse(raw);
            return { x: parsed.x ?? 0, y: parsed.y ?? 0 };
        }
        catch {
            return { x: 0, y: 0 };
        }
    },
};
/** 窗口状态持久化（命令前缀 `windowstate.*`）。 */
export const windowstate = {
    save: () => call("windowstate.save", []),
    restore: () => call("windowstate.restore", []),
    clear: () => call("windowstate.clear", []),
};
/** DPI 缩放（命令前缀 `dpi-scale.*`）。 */
export const dpiScale = {
    getScaleFactor: () => call("dpi-scale.getScaleFactor", []),
    /** @param zoom 缩放因子（后端字段名为 `zoom`）。 */
    setZoomFactor: (zoom) => call("dpi-scale.setZoomFactor", [{ zoom }]),
    reset: () => call("dpi-scale.reset", []),
};
//# sourceMappingURL=window.js.map