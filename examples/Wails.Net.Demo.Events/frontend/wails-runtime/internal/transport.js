/**
 * Wails.Net 自包含前端传输层。
 *
 * 设计目标：前端不再依赖 C# 端注入的 `window.wails` / `window._wailsInvoke`，
 * 由本包自行完成通道选择、序列化、分块、取消与响应解包。仅读取 C# 注入的
 * 小型 `window._wails` 标志对象（platform / isServerMode / 各 URL）作为配置。
 *
 * 三态通道策略：
 *  1. Server 模式（isServerMode 或配置了 webSocketUrl）→ WebSocket（`ws://localhost:34116/wails/ws`）
 *  2. 原生 postMessage（WebView2 / WebKitGTK / Android），且消息 ≤ 512KB → 低延迟副通道
 *  3. 其余 → HTTP `POST /wails/message`（超过 512KB 自动分块上传）
 * 取消消息始终走可靠通道（Server 模式走 WS，否则走 HTTP）。
 */
import { toCallError } from "./call-error.js";
import { fnv1a } from "./fnv1a.js";
import { readRuntimeFlags, } from "./types.js";
const CHUNK_THRESHOLD = 512 * 1024; // 512KB：原生/HTTP 分界（按 JSON 字符串长度）
const CHUNK_SIZE = 1000000; // 1MB：单 chunk 字节上限（后端限制 ≤ 1MB）
const DEFAULT_WS_URL = "ws://localhost:34116/wails/ws";
function randomId(len = 21) {
    const alphabet = "useandom-26T198340PX75pxJACKVERYMINDBUSHWOLF_GQZbfghjklqvwyzrict";
    const arr = new Uint8Array(len);
    const cryptoObj = globalThis.crypto;
    if (cryptoObj?.getRandomValues) {
        cryptoObj.getRandomValues(arr);
    }
    else {
        for (let i = 0; i < len; i++)
            arr[i] = Math.floor(Math.random() * 256);
    }
    let id = "";
    for (let i = 0; i < len; i++) {
        id += alphabet[arr[i] % alphabet.length];
    }
    return id;
}
/** 传输层实现。 */
export class Transport {
    constructor() {
        Object.defineProperty(this, "pending", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: new Map()
        });
        Object.defineProperty(this, "eventCallbacks", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: new Map()
        });
        Object.defineProperty(this, "counter", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: 0
        });
        Object.defineProperty(this, "nativePost", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: void 0
        });
        Object.defineProperty(this, "ws", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: void 0
        });
        Object.defineProperty(this, "wsUrl", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: DEFAULT_WS_URL
        });
        Object.defineProperty(this, "messageUrl", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: "/wails/message"
        });
        Object.defineProperty(this, "serverMode", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: false
        });
        Object.defineProperty(this, "initialized", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: false
        });
        Object.defineProperty(this, "reconnectTimer", {
            enumerable: true,
            configurable: true,
            writable: true,
            value: void 0
        });
    }
    /** 初始化通道检测与下行监听。幂等。 */
    init() {
        if (this.initialized)
            return;
        this.initialized = true;
        const flags = readRuntimeFlags();
        this.serverMode = flags?.isServerMode === true || !!flags?.webSocketUrl;
        this.messageUrl = (flags?.assetServerUrl ?? "") + "/wails/message";
        this.wsUrl = flags?.webSocketUrl ?? DEFAULT_WS_URL;
        if (typeof globalThis === "undefined" || typeof globalThis.window === "undefined") {
            // 非浏览器环境（如测试）：跳过通道初始化。
            return;
        }
        this.nativePost = this.detectNativePost();
        this.registerDownlink();
        if (this.serverMode)
            this.connectWs();
    }
    /** 释放资源（清理 WS 与监听器）。 */
    destroy() {
        if (this.reconnectTimer)
            clearTimeout(this.reconnectTimer);
        if (this.ws) {
            this.ws.onmessage = null;
            this.ws.onclose = null;
            this.ws.onerror = null;
            try {
                this.ws.close();
            }
            catch {
                /* ignore */
            }
            this.ws = undefined;
        }
        this.pending.clear();
        this.eventCallbacks.clear();
        this.initialized = false;
    }
    ensureInit() {
        if (!this.initialized)
            this.init();
    }
    // ----- 通道检测 -----
    detectNativePost() {
        const w = globalThis;
        if (w.chrome?.webview?.postMessage) {
            return (s) => w.chrome.webview.postMessage(s);
        }
        if (w.webkit?.messageHandlers?.external?.postMessage) {
            return (s) => w.webkit.messageHandlers.external.postMessage(s);
        }
        if (typeof w.WailsBridge?.invoke === "function") {
            // Android：AndroidWebviewWindow 通过 AddJavascriptInterface(listener, "WailsBridge")
            // 注入同步桥接对象，上行调用 window.WailsBridge.invoke(json)；下行仍由
            // __wailsNative.onMessage / _wailsEmitEvent 承接。
            // 注意：探测键必须是 WailsBridge 而非 wails——本包自身会挂载 window.wails.invoke，
            // 用 wails.invoke 探测会在任意平台产生误判，把消息发进自己的 API。
            return (s) => {
                w.WailsBridge.invoke(s);
            };
        }
        return undefined;
    }
    registerDownlink() {
        const w = globalThis;
        const onMsg = (e) => {
            if (typeof e.data === "string")
                this.onNativeMessage(e.data);
        };
        if (w.chrome?.webview?.addEventListener) {
            w.chrome.webview.addEventListener("message", onMsg);
        }
        else if (typeof w.addEventListener === "function") {
            w.addEventListener("message", onMsg);
        }
        // 供原生 / Android 桥接调用
        w.__wailsNative = { onMessage: (json) => this.onNativeMessage(json) };
        // 桌面模式事件下行入口。
        // 后端 EventIPCTransport.DispatchWailsEvent 不走 postMessage 回传，而是对每个窗口执行
        //   ExecuteScriptAsync("window._wailsEmitEvent && window._wailsEmitEvent(name, data, senderWindowId)")
        // 因为后端带 `&&` 短路保护，若此全局缺失，事件会被静默丢弃且不报错。
        // 必须挂载，否则桌面模式下 wails.events.on(...) 收不到任何后端事件。
        w._wailsEmitEvent = (name, data, senderWindowId) => this._emitEvent(name, data, senderWindowId ?? null);
    }
    connectWs() {
        if (typeof WebSocket === "undefined")
            return;
        try {
            this.ws = new WebSocket(this.wsUrl);
        }
        catch {
            return;
        }
        this.ws.onmessage = (e) => {
            try {
                const data = typeof e.data === "string" ? JSON.parse(e.data) : e.data;
                if (data && data.type === "event" && typeof data.name === "string") {
                    this._emitEvent(data.name, data.data);
                    return;
                }
                this.handleResponse(data);
            }
            catch {
                /* ignore malformed */
            }
        };
        this.ws.onclose = () => {
            this.ws = undefined;
            if (this.serverMode) {
                this.reconnectTimer = setTimeout(() => this.connectWs(), 3000);
            }
        };
        this.ws.onerror = () => {
            try {
                this.ws?.close();
            }
            catch {
                /* ignore */
            }
        };
    }
    // ----- 公共调用 API -----
    /** 发起一次需要响应的调用（call / query 等）。 */
    invoke(type, payload) {
        this.ensureInit();
        const id = this.nextId();
        const promise = new Promise((resolve, reject) => {
            this.pending.set(id, { resolve: resolve, reject, cancelled: false });
        });
        promise.callId = id;
        promise.cancel = () => this.cancel(id);
        void this.dispatch({ id, type, payload });
        return promise;
    }
    /** 发起一次无需响应的发送（event.emit / drag / contextmenu）。 */
    send(type, payload) {
        this.ensureInit();
        void this.dispatch({ id: this.nextId(), type, payload });
    }
    /** 取消一个进行中的调用。 */
    cancel(callId) {
        this.ensureInit();
        const p = this.pending.get(callId);
        if (p)
            p.cancelled = true;
        const msg = {
            id: "cancel-" + callId + "-" + Date.now(),
            type: "cancel",
            payload: { callId: String(callId) },
        };
        if (this.serverMode && this.ws && this.ws.readyState === WebSocket.OPEN) {
            this.ws.send(JSON.stringify(msg));
        }
        else {
            void this.sendCancelHttp(msg);
        }
    }
    // ----- 事件订阅 -----
    on(name, cb) {
        // 必须初始化：纯订阅型前端（只订阅、从不主动调用后端）否则永远不会挂载
        // window._wailsEmitEvent，Server 模式下也不会建立 WebSocket，导致事件全部丢失。
        this.ensureInit();
        let set = this.eventCallbacks.get(name);
        if (!set) {
            set = new Set();
            this.eventCallbacks.set(name, set);
        }
        set.add(cb);
        return () => this.off(name, cb);
    }
    once(name, cb) {
        const off = this.on(name, (data, senderWindowId) => {
            off();
            cb(data, senderWindowId);
        });
        return off;
    }
    off(name, cb) {
        this.eventCallbacks.get(name)?.delete(cb);
    }
    /** 向后端发送事件（fire-and-forget，event.emit 永不返回响应）。 */
    emit(name, data) {
        const payload = { name, data };
        this.send("event.emit", payload);
    }
    _emitEvent(name, data, senderWindowId = null) {
        const set = this.eventCallbacks.get(name);
        if (!set)
            return;
        // 复制一份再遍历：回调内部调用 off()/once() 会改动原 Set，直接遍历会漏派发。
        for (const cb of [...set]) {
            try {
                cb(data, senderWindowId);
            }
            catch (err) {
                // 单个订阅者异常不应影响其他订阅者
                console?.error?.(`[wails] event "${name}" callback threw:`, err);
            }
        }
    }
    // ----- 通道分发 -----
    async dispatch(msg) {
        if (this.serverMode && this.ws && this.ws.readyState === WebSocket.OPEN) {
            this.ws.send(JSON.stringify(msg));
            return;
        }
        const native = this.nativePost;
        if (native) {
            const s = JSON.stringify(msg);
            if (s.length <= CHUNK_THRESHOLD) {
                native(s);
                return;
            }
        }
        await this.sendHttp(msg);
    }
    async sendHttp(msg) {
        const body = JSON.stringify(msg);
        const byteLen = new TextEncoder().encode(body).length;
        if (byteLen <= CHUNK_THRESHOLD) {
            const resp = await fetch(this.messageUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body,
            });
            await this.handleHttpResponse(resp);
            return;
        }
        // 分块上传
        const bytes = new TextEncoder().encode(body);
        const total = Math.max(1, Math.ceil(bytes.length / CHUNK_SIZE));
        const chunkId = randomId(21);
        for (let i = 0; i < total; i++) {
            const slice = bytes.subarray(i * CHUNK_SIZE, Math.min((i + 1) * CHUNK_SIZE, bytes.length));
            const headers = {
                "Content-Type": "application/json",
                "x-wails-chunk-id": chunkId,
                "x-wails-chunk-index": String(i),
                "x-wails-chunk-total": String(total),
            };
            const resp = await fetch(this.messageUrl, {
                method: "POST",
                headers,
                body: slice,
            });
            if (i < total - 1) {
                if (!resp.ok) {
                    console?.error?.(`[wails] chunk ${i}/${total} upload failed: ${resp.status}`);
                }
                continue;
            }
            await this.handleHttpResponse(resp);
        }
    }
    async sendCancelHttp(msg) {
        try {
            await fetch(this.messageUrl, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(msg),
            });
        }
        catch (err) {
            console?.error?.("[wails] cancel failed:", err);
        }
    }
    async handleHttpResponse(resp) {
        try {
            const text = await resp.text();
            if (!text)
                return;
            const data = JSON.parse(text);
            this.handleResponse(data);
        }
        catch {
            /* ignore */
        }
    }
    onNativeMessage(json) {
        try {
            const data = JSON.parse(json);
            if (data && data.type === "event" && typeof data.name === "string") {
                this._emitEvent(data.name, data.data);
                return;
            }
            this.handleResponse(data);
        }
        catch {
            /* ignore malformed */
        }
    }
    handleResponse(data) {
        const id = data?.id;
        if (!id)
            return;
        const p = this.pending.get(id);
        if (!p)
            return;
        this.pending.delete(id);
        if (p.cancelled)
            return; // 已取消，丢弃
        try {
            const value = unpack(data);
            p.resolve(value);
        }
        catch (err) {
            p.reject(err);
        }
    }
    nextId() {
        return String(++this.counter);
    }
}
/**
 * 解包双层响应：`result.result` 为业务值，`result.error` 为业务错误。
 * 兼容 `type === "error"` 的顶层错误信封，以及无嵌套的旧格式。
 */
export function unpack(data) {
    if (!data)
        throw toCallError("空响应");
    const outer = data.result;
    if (outer && typeof outer === "object" && "error" in outer) {
        if (outer.error)
            throw toCallError(outer.error);
        return outer.result;
    }
    return outer;
}
/** 计算绑定方法 ID（FNV-1a 32 位，对应后端 FullName 哈希）。 */
export function bindingId(fullName) {
    return fnv1a(fullName);
}
/**
 * 全局传输单例。
 *
 * 通过 `globalThis.__wailsTransport` 做跨副本交接：同一页面若因打包边界
 * （主 bundle + 动态 import 的子 chunk、或宿主注入脚本与应用 bundle 并存）
 * 加载了多份本模块，各副本必须共用同一个 Transport 实例，否则
 * pending 表与事件订阅表会被割裂，出现「调用无响应」「事件收不到」。
 */
const transportHost = globalThis;
transportHost.__wailsTransport ?? (transportHost.__wailsTransport = new Transport());
export const transport = transportHost.__wailsTransport;
//# sourceMappingURL=transport.js.map