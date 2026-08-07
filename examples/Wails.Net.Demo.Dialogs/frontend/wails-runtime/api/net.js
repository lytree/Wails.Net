/**
 * 网络相关命令封装：HTTP 客户端、上传下载、WebSocket、本地服务器、Cookie、深链、外部打开。
 *
 * 参数顺序与个数严格对齐后端 `MapCommand` 注册签名：
 * 单参数命令发送 `[value]`（后端整体反序列化），多参数命令按位置发送。
 */
import { call } from "../core/runtime.js";
/**
 * HTTP 客户端（命令前缀 `http.*`）。
 *
 * 注意：后端 `http.get` / `http.delete` 仅接受 URL，`http.post` / `http.put`
 * 接受 URL 与可选请求体；**自定义请求头只能通过 `fetch` 传递**。
 */
export const http = {
    /** 通用请求，支持自定义方法与请求头。 */
    fetch: (options) => call("http.fetch", [options]),
    get: (url) => call("http.get", [url]),
    post: (url, body) => call("http.post", [url, body ?? null]),
    put: (url, body) => call("http.put", [url, body ?? null]),
    delete: (url) => call("http.delete", [url]),
};
/**
 * 上传 / 下载（命令前缀 `upload.*`）。
 *
 * 全部返回 `boolean` 表示成功与否。
 *
 * 注意：`*WithProgress` 变体目前后端**尚未发出进度事件**，行为与非 progress
 * 版本一致，保留是为了 API 兼容与后续实现。
 */
export const upload = {
    /** 上传本地文件到目标 URL。 */
    upload: (url, filePath) => call("upload.upload", [url, filePath]),
    /** 上传本地文件（预留进度上报）。 */
    uploadWithProgress: (url, filePath) => call("upload.uploadWithProgress", [url, filePath]),
    /** 从 URL 下载文件到本地路径。 */
    download: (url, path) => call("upload.download", [url, path]),
    /** 从 URL 下载文件（预留进度上报）。 */
    downloadWithProgress: (url, path) => call("upload.downloadWithProgress", [url, path]),
};
/**
 * WebSocket（命令前缀 `websocket.*`，按连接 ID 管理）。
 *
 * 收到的消息与关闭通知通过事件总线派发：
 * - `websocket:message` —— 收到消息
 * - `websocket:closed`  —— 连接关闭（数据为 connectionId）
 */
export const websocket = {
    /** 建立连接，返回连接 ID。 */
    connect: (url) => call("websocket.connect", [url]),
    send: (connectionId, message) => call("websocket.send", [connectionId, message]),
    sendBinary: (connectionId, base64Data) => call("websocket.sendBinary", [connectionId, base64Data]),
    close: (connectionId) => call("websocket.close", [connectionId]),
    getState: (connectionId) => call("websocket.getState", [connectionId]),
};
/**
 * 本地 HTTP 服务器（命令前缀 `localhost.*`）。
 *
 * 后端按**端口**索引多个服务器实例，因此除 `start` 外每个方法都要求传入 `port`。
 */
export const localhost = {
    /**
     * 启动本地服务器。
     * @param port 监听端口；传 `0` 由后端自动分配空闲端口。
     * @param rootDir 静态文件根目录。
     * @returns 服务器根 URL。
     */
    start: (port = 0, rootDir) => call("localhost.start", [port, rootDir ?? null]),
    stop: (port) => call("localhost.stop", [port]),
    getUrl: (port) => call("localhost.getUrl", [port]),
    isRunning: (port) => call("localhost.isRunning", [port]),
    setRoot: (port, rootDir) => call("localhost.setRoot", [port, rootDir]),
    /**
     * 注册路由。
     * @param method HTTP 方法（如 `"GET"`），后端会转为大写。
     */
    addRoute: (port, route, method) => call("localhost.addRoute", [port, route, method]),
    removeRoute: (port, route) => call("localhost.removeRoute", [port, route]),
    listRoutes: (port) => call("localhost.listRoutes", [port]),
};
/**
 * Cookie（命令前缀 `cookie.*`）。
 *
 * 后端以内存字典维护 Cookie，`get` 的 `url` 参数当前被忽略（保留给未来按 URL 过滤）。
 */
export const cookie = {
    /**
     * 读取全部 Cookie。
     * @returns JSON 字符串形式的 `{ name: value }` 字典。
     */
    get: (url = "") => call("cookie.get", [url]),
    set: (name, value) => call("cookie.set", [name, value]),
    delete: (name) => call("cookie.delete", [name]),
    clear: () => call("cookie.clear", []),
};
/** 深度链接（命令前缀 `deeplink.*`）。 */
export const deeplink = {
    getCurrent: () => call("deeplink.getCurrent", []),
    register: (scheme) => call("deeplink.register", [scheme]),
    unregister: (scheme) => call("deeplink.unregister", [scheme]),
};
/** 外部打开（命令前缀 `opener.*`）。 */
export const opener = {
    /**
     * 用外部程序打开 URL。
     * @param target 可选的目标程序；省略则用系统默认处理程序。
     */
    openUrl: (url, target) => call("opener.openUrl", [url, target ?? null]),
    /** 用外部程序打开本地路径。 */
    openPath: (path, target) => call("opener.openPath", [path, target ?? null]),
    /** 在文件管理器中定位并选中文件。 */
    revealInFolder: (path) => call("opener.revealInFolder", [path]),
    isUrlAllowed: (url) => call("opener.isUrlAllowed", [url]),
    /** 校验 URL，返回规范化后的 URL；不合法时返回 `null`。 */
    verifyUrl: (url) => call("opener.verifyUrl", [url]),
};
//# sourceMappingURL=net.js.map