/**
 * Wails v3 Streams 前端运行时客户端（1:1 移植自 Wails v3
 * `@wailsio/runtime/src/stream.ts`，beta.28）。
 *
 * GoStream —— 一门没有监听套接字的 WebSocket 编程模型。
 *
 * 自定义 URL scheme 无法承载 WebSocket，因此在 webview 内用上 WebSocket 就意味着
 * 真的绑定一个 TCP 端口。这里改为在应用已有的资源服务器上承载同一套事件形态：
 * Go→JS 走每页一个「挂起轮询」（held poll），JS→Go 走普通 POST。
 *
 * {@link Stream} 与 `new WebSocket(url)` 一样同步返回且 `readyState === CONNECTING`，
 * 因此可以在模块作用域创建连接：
 *
 * ```ts
 * export const Telemetry = Stream("telemetry"); // 后台连接
 * ```
 *
 * 与 Go 实现的差异（仅环境适配）：
 * - `hasDOM` 就地定义（本仓库无 `environment.js`），仅判断 `window` 是否存在。
 */

import { nanoid } from "./nanoid.js";

/** 是否处于具备 DOM 的浏览器/webview 环境（SSR 预渲染下为 false）。 */
const hasDOM = typeof window !== "undefined";

// 两个运行时实例必须达成一致的所有状态都放在 window 上，而非模块作用域：运行时
// 可能在一个页面里被求值多次（平台注入一份，应用又 import 一份），若使用
// 模块级注册表，两个实例会各自分配连接 id 1、对同一 session 跑起相互竞争的轮询，
// 并丢弃只被另一个实例认识的 id 对应的帧。仅靠 session id 并不足以避免这一点。
interface StreamGlobals {
  /** 每页一个的 session id。 */
  session: string;
  /** 页面代际（generation），用于区分页面再生（reload）。 */
  generation: number;
  /** 下一个连接 id。 */
  nextConnID: number;
  /** id → 连接实例。 */
  connections: Map<number, WailsSocket>;
  /** 是否已有轮询循环在跑。 */
  polling: boolean;
  /** 当前轮询的 AbortController（停止时 abort）。 */
  pollAbort?: AbortController;
}

const GENERATION_KEY = "__wails_stream_generation";
const GENERATION_NAME_MARKER = "\u001f__wails_stream_generation__:";

// window.name 属于浏览上下文（browsing context）而非 Document，因此即便 Web Storage
// 被禁用，它也能在 reload 后存活。保留应用自己设置的名字，只为 Stream 代际保留一个后缀。
function namedPageGeneration(): number {
  try {
    const marker = window.name.lastIndexOf(GENERATION_NAME_MARKER);
    if (marker < 0) return 0;
    const value = Number(window.name.slice(marker + GENERATION_NAME_MARKER.length));
    return Number.isSafeInteger(value) && value > 0 ? value : 0;
  } catch {
    return 0;
  }
}

function rememberNamedPageGeneration(generation: number): void {
  try {
    const marker = window.name.lastIndexOf(GENERATION_NAME_MARKER);
    const applicationName = marker < 0 ? window.name : window.name.slice(0, marker);
    window.name = applicationName + GENERATION_NAME_MARKER + generation;
  } catch {
    // 某些嵌入式浏览器策略会拒绝访问浏览上下文名称，timeOrigin 是最后的兜底来源。
  }
}

// 请求抵达顺序并非页面顺序：一个恰好在导航前发出的请求，可能晚于替换页面的首个
// 请求才到达 Go。把单调计数器放在顶层浏览上下文里，Go 便能区分这些页面再生，
// 而无需信任服务器调度顺序。
function nextPageGeneration(): number {
  if (!hasDOM) return 1;
  // 将该值同时锚定到页面创建时间与存储的计数器。若应用代码清空了 sessionStorage，
  // 下一次 reload 仍必须比 Go 已退役的代际更新；否则从 1 重新开始会让每个 stream
  // 请求在宿主退出前一直收到 410。较旧的 WebKit 上不存在 Performance.timeOrigin，
  // 用 Date.now 让这些引擎仍留在协议内；存储计数器用于区分同一毫秒内的 reload。
  const origin =
    typeof performance !== "undefined" && Number.isFinite(performance.timeOrigin)
      ? performance.timeOrigin
      : Date.now();
  const pageTime = Math.max(1, Math.floor(origin * 1000));
  let current = namedPageGeneration();
  try {
    const raw = window.sessionStorage.getItem(GENERATION_KEY);
    const stored = raw === null ? 0 : Number(raw);
    if (Number.isSafeInteger(stored) && stored > current) current = stored;
  } catch {
    // 存储可能被策略禁用，此时 window.name 提供浏览上下文计数器。
  }

  const next =
    Number.isSafeInteger(current) && current >= 0 && current < Number.MAX_SAFE_INTEGER
      ? Math.max(current + 1, pageTime)
      : pageTime;
  try {
    window.sessionStorage.setItem(GENERATION_KEY, String(next));
  } catch {
    // 存储不可用时，下面的浏览上下文兜底已足够。
  }
  rememberNamedPageGeneration(next);
  return next;
}

/** 挂在 `window._wails` 上的 Stream 相关全局。 */
interface WailsGlobals {
  /** 共享的 stream 全局状态。 */
  __stream?: StreamGlobals;
  /** server 构建注入的工厂：返回真实 WebSocket。 */
  streamFactory?: (name: string) => WebSocket;
}

/** 取（必要时创建）`window._wails` 挂载对象。 */
function wailsGlobals(): WailsGlobals {
  const w = window as unknown as { _wails?: WailsGlobals };
  return (w._wails = w._wails || {});
}

function streamGlobals(): StreamGlobals {
  const fresh = (): StreamGlobals => ({
    session: nanoid(),
    generation: nextPageGeneration(),
    nextConnID: 1,
    connections: new Map<number, WailsSocket>(),
    polling: false,
  });
  if (!hasDOM) return fresh();
  const g = wailsGlobals();
  return (g.__stream = g.__stream || fresh());
}

const G = streamGlobals();

const HDR_SESSION = "x-wails-stream-session";
const HDR_GENERATION = "x-wails-stream-generation";
const HDR_CONN = "x-wails-stream-conn";
const HDR_KIND = "x-wails-stream-kind";
const HDR_NAME = "x-wails-stream-name";
const HDR_CHUNK = "x-wails-stream-chunk";
const HDR_CHUNK_INDEX = "x-wails-stream-chunk-index";
const HDR_CHUNK_TOTAL = "x-wails-stream-chunk-total";
const HDR_BATCH = "x-wails-stream-batch";

const KIND_DATA = 0;
const KIND_OPEN = 1;
const KIND_CLOSE = 2;
const KIND_ERROR = 3;

// 与运行时自身的限制一致：保持在 WebView2 在 WebResourceRequested 中约 2MB 的请求
// 体缓冲上限之内。
const CHUNK_THRESHOLD = 512 * 1024;
const MAX_BATCH_FRAMES = 4096;

// 失败轮询的退避下限与上限。这是对轮询「合理下限」的界定 —— 它是错误退避，
// 而不是稳态间隔。正常情况下轮询会立即重新发起，因为服务端挂起（hold）本身
// 已提供了恰当的延迟。
const BACKOFF_MIN = 250;
const BACKOFF_MAX = 5000;
const textEncoder = new TextEncoder();

function streamURL(endpoint: string): string {
  return window.location.origin + "/wails/stream/" + endpoint;
}

class StreamRequestCancelled extends Error {
  constructor() {
    super("stream request cancelled");
    this.name = "AbortError";
  }
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  if (!signal) return new Promise((resolve) => setTimeout(resolve, ms));
  if (signal.aborted) return Promise.reject(new StreamRequestCancelled());

  return new Promise((resolve, reject) => {
    const onAbort = () => {
      clearTimeout(timer);
      reject(new StreamRequestCancelled());
    };
    const timer = setTimeout(() => {
      signal.removeEventListener("abort", onAbort);
      resolve();
    }, ms);
    signal.addEventListener("abort", onAbort, { once: true });
  });
}

/**
 * 一个流连接。实现了 `WebSocket` 接口中真正有用的子集，因此同一份应用代码在
 * server 构建下可对着真实 WebSocket 工作。
 *
 * 与标准有一处刻意分歧：`binaryType` 默认是 `"arraybuffer"` 而非 `"blob"`，
 * 因为 stream 帧始终是二进制，Blob 会为每条消息多引入一次异步跳转才能读取。
 * 将其设为 `"blob"` 时的行为与标准描述一致。
 */
export class WailsSocket extends EventTarget {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSING = 2;
  static readonly CLOSED = 3;

  readonly CONNECTING = 0;
  readonly OPEN = 1;
  readonly CLOSING = 2;
  readonly CLOSED = 3;

  /** 本连接打开时使用的流名称。 */
  readonly name: string;
  readonly url: string;

  /** 始终为空：不协商子协议与扩展。 */
  readonly protocol = "";
  readonly extensions = "";

  binaryType: "arraybuffer" | "blob" = "arraybuffer";
  readyState: number = WailsSocket.CONNECTING;

  // 仅为类型而声明；构造函数会用注册真实监听器的访问器替换它们。若直接调用它们，
  // 任何包装器（见 JSONStream）都会同时收到原始事件与解码后的事件。
  onopen: ((ev: Event) => void) | null = null;
  onmessage: ((ev: MessageEvent) => void) | null = null;
  onclose: ((ev: CloseEvent) => void) | null = null;
  onerror: ((ev: Event) => void) | null = null;

  /**
   * @internal 在派发前应用到每个入站载荷。JSONStream 会替换它，使得 `onmessage`
   * 与 `addEventListener("message", …)` 看到同一个解码后的值 —— 只拦截 `onmessage`
   * 会让两者产生分歧。
   */
  _decode: (payload: ArrayBuffer) => unknown = (payload) => payload;

  /** @internal 本连接的 id。 */
  readonly _id: number;
  private _buffered = 0;
  // 发送按连接串行化：并发的 fetch() POST 不保证顺序，而 Go 依赖收到的发送顺序
  // 就是它观察到的顺序。
  private _chain: Promise<void> = Promise.resolve();
  private _pending: Promise<Uint8Array>[] = [];
  private _flushing = false;
  private _openAbort = new AbortController();
  private _sendAbort = new AbortController();

  /** `send()` 已排入但尚未抵达 Go 的字节数。 */
  get bufferedAmount(): number {
    return this._buffered;
  }

  constructor(name: string) {
    super();
    this.name = name;
    this._id = G.nextConnID++;
    this.url = hasDOM ? streamURL("poll") + "#" + encodeURIComponent(name) : "";

    for (const type of ["open", "message", "close", "error"]) {
      defineHandlerProperty(this, type);
    }

    G.connections.set(this._id, this);

    // fire-and-forget：ack 以 open 帧的形式在轮询中抵达，正是它把 readyState
    // 推进到 OPEN。
    this._chain = this._chain
      .then(() =>
        postFrame(this._id, KIND_OPEN, new Uint8Array(0), name, this._openAbort.signal),
      )
      .catch((err) => {
        this._fail(err);
      });

    startPolling();
  }

  /**
   * 向 Go 发送一帧。接受 WebSocket 能接受的一切；字符串按 UTF-8 编码，
   * 因为 Go 收到的每一帧都是字节切片。
   */
  send(data: string | ArrayBufferLike | ArrayBufferView | Blob): void {
    if (this.readyState === WailsSocket.CONNECTING) {
      // 与 WebSocket 规范一致。
      throw new DOMException("Still in CONNECTING state.", "InvalidStateError");
    }
    if (this.readyState !== WailsSocket.OPEN) {
      return;
    }

    // 在链内立即解析，而不是留到链里再解析。可变的二进制输入会同步拷贝，使 send()
    // 像原生 WebSocket 一样快照其字节，即便某个 batch 正在等到进行中的请求之后。
    // Blob 是不可变的，因此可以安全地异步读取一次。
    const immediate = toBytesSync(data);
    const snapshot = immediate
      ? Promise.resolve(immediate)
      : toBytes(data, this._sendAbort.signal);
    // close() 可能在 flush 出队前丢弃这个 promise。立即挂一个拒绝观察者，使取消一次
    // 异步 Blob 读取不会变成未处理拒绝；当 flush 已经持有原始拒绝时，Promise.all
    // 仍会观察到它。
    void snapshot.catch(() => {});

    // Blob 无法同步转换，但 Blob.size 现在就可获得，因此它的字节在 send() 返回时
    // 仍会被计入。等待 arrayBuffer() 解析曾让大 Blob 溜过使用 bufferedAmount 做背压
    // 的代码。
    const blobSize =
      !immediate && typeof Blob !== "undefined" && data instanceof Blob ? data.size : 0;
    if (blobSize) {
      this._buffered += blobSize;
    }

    // 现在就记账，而非等链走到它们时。若在帧排队等候进行中的 batch 期间报告 0，
    // 会让依赖 bufferedAmount 做背压的应用以为可以继续发送。
    if (immediate) {
      this._buffered += immediate.byteLength;
    }
    this._pending.push(snapshot);

    // 排队而非立即发送。请求进行期间累积的内容会在下一次一并发出，从而把每次请求的
    // 开销（一次 scheme-handler 往返，在 macOS 上约十一次 cgo 调用）摊薄到整个 batch。
    // 轻负载下 batch 即单帧，行为与过去完全一致；只有当发送方跑赢传输层时才会出现
    // 批量，而这正是需要它的时候。
    if (this._flushing) return;
    this._flushing = true;

    this._chain = this._chain
      .then(async () => {
        try {
          while (this._pending.length > 0) {
            // 约束单次工作量与临时 promise 数组。postBatch 在解析后应用在线上大小界限。
            const queued = this._pending.splice(0, MAX_BATCH_FRAMES);
            const batch = await Promise.all(queued);

            // 对端关闭可能在读取 Blob 期间抵达。此时连接已消失，不要再为对端已无法
            // 接收的字节发起迟到的 POST。
            if (this.readyState === WailsSocket.CLOSED) {
              break;
            }

            const total = batch.reduce((n, b) => n + b.byteLength, 0);
            try {
              await postBatch(this._id, batch, this._sendAbort.signal);
            } finally {
              // _closed 会立即重置计数器。已在飞行中的请求可能随后完成，绝不能把
              // bufferedAmount 降到 0 以下。
              this._buffered = Math.max(0, this._buffered - total);
            }
          }
        } finally {
          this._flushing = false;
        }
      })
      .catch((err) => {
        this._flushing = false;
        this._fail(err);
      });
  }

  /** 关闭连接。Go 的处理器会看到 Receive 失败并返回。 */
  close(code: number = 1000, reason: string = ""): void {
    if (this.readyState === WailsSocket.CLOSING || this.readyState === WailsSocket.CLOSED) {
      return;
    }
    this.readyState = WailsSocket.CLOSING;
    this._pending.length = 0;
    this._buffered = 0;
    this._openAbort.abort();

    // 等待接收方容量的一条 data 帧不应阻止预留的 close 控制帧抵达 Go。只取消 data
    // 重试路径；下面的链仍会把 close 排在该路径展开之后，保持正常的 send-before-close
    // 顺序。
    this._sendAbort.abort();
    this._chain = this._chain
      .then(() => postFrame(this._id, KIND_CLOSE, new Uint8Array(0)))
      .catch(() => {
        // 对端已消失；真正重要的是下面本地执行的关闭。
      })
      .then(() => {
        this._closed(code, reason, true);
      });
  }

  /** @internal open 帧从 Go 回来时调用。 */
  _opened(): void {
    if (this.readyState !== WailsSocket.CONNECTING) return;
    this.readyState = WailsSocket.OPEN;
    this.dispatchEvent(new Event("open"));
  }

  /** @internal 对发往本连接的每条 data 帧调用。 */
  _message(payload: ArrayBuffer): void {
    if (this.readyState !== WailsSocket.OPEN) return;
    let data: unknown;
    try {
      data = this._decode(payload);
    } catch {
      this.dispatchEvent(new Event("error"));
      return;
    }
    if (data === payload && this.binaryType === "blob") {
      data = new Blob([payload]);
    }
    this.dispatchEvent(new MessageEvent("message", { data }));
  }

  /** @internal */
  _fail(err: unknown): void {
    // 一次飞行中的发送可能在干净关闭已经抵达之后才拒绝。此时 socket 生命周期已结束：
    // 在 close 之后发出 error 会颠倒 WebSocket 事件顺序并报告虚假失败。
    if (this.readyState === WailsSocket.CLOSED || this.readyState === WailsSocket.CLOSING) return;
    this.dispatchEvent(new Event("error"));
    // 1006：异常关闭，无 close 帧。真实 WebSocket 在握手未完成即掉线时也报此码。
    this._closed(1006, err instanceof Error ? err.message : String(err), false);
  }

  /** @internal */
  _closed(code: number, reason: string, wasClean: boolean): void {
    if (this.readyState === WailsSocket.CLOSED) return;
    this.readyState = WailsSocket.CLOSED;
    this._openAbort.abort();
    this._sendAbort.abort();
    G.connections.delete(this._id);
    if (G.connections.size === 0) G.pollAbort?.abort();
    this._pending.length = 0;
    this._buffered = 0;

    const ev =
      typeof CloseEvent === "function"
        ? new CloseEvent("close", { code, reason, wasClean })
        : (Object.assign(new Event("close"), { code, reason, wasClean }) as CloseEvent);
    this.dispatchEvent(ev);
  }
}

/**
 * 连接一个在 Go 端用 `app.HandleStream(name, handler)` 声明的流。
 *
 * 立即返回且 `readyState === CONNECTING`；监听 `open`，或在 open 后直接调用
 * {@link WailsSocket.send}。支持在模块作用域创建连接，这也正是生成绑定的预期形态。
 */
export function Stream(name: string): WailsSocket | WebSocket {
  // server 构建会安装一个返回真实 WebSocket 的工厂，因为那里有监听器可供升级，
  // 无需模拟。两种对象呈现相同接口，因此这一行之上的任何代码都不关心它是哪一种。
  if (hasDOM) {
    const factory = wailsGlobals().streamFactory;
    if (typeof factory === "function") {
      return factory(name);
    }
  }
  return new WailsSocket(name);
}

// 定义一个 `on<type>` 属性，它会像 DOM 那样注册/注销一个真实的监听器。
// 赋值替换上一个处理器；读取返回它。
function defineHandlerProperty(target: EventTarget, type: string): void {
  let current: ((ev: any) => void) | null = null;
  Object.defineProperty(target, "on" + type, {
    get: () => current,
    set(fn: ((ev: any) => void) | null) {
      if (current) target.removeEventListener(type, current);
      current = typeof fn === "function" ? fn : null;
      if (current) target.addEventListener(type, current);
    },
    configurable: true,
    enumerable: true,
  });
}

/**
 * {@link JSONStream} 返回的流的对象视图：`send` 接收一个要字符串化的值，
 * 消息上的 `ev.data` 是解析后的结果。
 */
export interface JSONSocket
  extends Omit<WailsSocket, "send" | "onmessage" | "addEventListener" | "removeEventListener"> {
  send(value: unknown): void;
  onmessage: ((ev: MessageEvent<any>) => void) | null;
  addEventListener(
    type: "message",
    listener: ((this: JSONSocket, ev: MessageEvent<any>) => any) | EventListenerObject | null,
    options?: boolean | AddEventListenerOptions,
  ): void;
  addEventListener(
    type: string,
    listener: EventListenerOrEventListenerObject | null,
    options?: boolean | AddEventListenerOptions,
  ): void;
  removeEventListener(
    type: "message",
    listener: ((this: JSONSocket, ev: MessageEvent<any>) => any) | EventListenerObject | null,
    options?: boolean | EventListenerOptions,
  ): void;
  removeEventListener(
    type: string,
    listener: EventListenerOrEventListenerObject | null,
    options?: boolean | EventListenerOptions,
  ): void;
}

/**
 * 一个说对象而非字节的 {@link Stream}。
 *
 * `send(value)` 用 `JSON.stringify` 序列化，`onmessage` 处理器中的 `ev.data` 是解析
 * 后的对象而非 `ArrayBuffer`。与 Go 侧的 `StreamConn.SendJSON` / `ReceiveJSON` 配对。
 *
 * ```js
 * const s = JSONStream("telemetry");
 * s.onmessage = (ev) => console.log(ev.data.temperature);
 * s.onopen    = () => s.send({ subscribe: "sensors" });
 * ```
 *
 * 不是合法 JSON 的帧会触发 `error` 事件并被丢弃，而不会从轮询循环抛出并把连接
 * 一起带走。
 */
export function JSONStream(name: string): JSONSocket {
  const socket = Stream(name);
  const decoder = new TextDecoder();
  const parse = (payload: ArrayBuffer | string) =>
    JSON.parse(typeof payload === "string" ? payload : decoder.decode(payload));

  if (socket instanceof WailsSocket) {
    // 通过钩子解码而非包装 onmessage，意味着 addEventListener 监听器也能看到解析后的对象。
    socket._decode = (payload) => parse(payload);
  } else {
    // server 构建拿到的是原生 WebSocket，其派发从不查 _decode。在此实例上同时给两种
    // 监听风格打补丁，使 JSONStream 在任一传输上含义一致 —— 这正是共享 API 的意义。
    const native = socket as WebSocket;
    native.binaryType = "arraybuffer";

    const wrapped = new WeakMap<object, EventListener>();
    const decodedEvents = new WeakMap<Event, MessageEvent | null>();
    const add = native.addEventListener.bind(native);
    const remove = native.removeEventListener.bind(native);

    const decodeEvent = (ev: Event): MessageEvent | null => {
      if (decodedEvents.has(ev)) return decodedEvents.get(ev) ?? null;
      try {
        const value = parse((ev as MessageEvent).data);
        const decoded = new MessageEvent("message", { data: value });
        decodedEvents.set(ev, decoded);
        return decoded;
      } catch {
        decodedEvents.set(ev, null);
        native.dispatchEvent(new Event("error"));
        return null;
      }
    };

    const decode = (listener: any): EventListener => {
      const existing = wrapped.get(listener as object);
      if (existing) return existing;

      const fn: EventListener = (ev) => {
        const decoded = decodeEvent(ev);
        if (!decoded) return;
        if (typeof listener === "function") listener.call(native, decoded);
        else listener.handleEvent(decoded);
      };
      wrapped.set(listener as object, fn);
      return fn;
    };

    native.addEventListener = ((type: string, listener: any, opts?: any) => {
      add(type, type === "message" && listener ? decode(listener) : listener, opts);
    }) as typeof native.addEventListener;

    native.removeEventListener = ((type: string, listener: any, opts?: any) => {
      const fn = type === "message" && listener ? wrapped.get(listener) : undefined;
      remove(type, fn ?? listener, opts);
    }) as typeof native.removeEventListener;

    let handler: ((ev: MessageEvent) => void) | null = null;
    Object.defineProperty(native, "onmessage", {
      get: () => handler,
      set(fn) {
        if (handler) native.removeEventListener("message", handler as EventListener);
        handler = typeof fn === "function" ? fn : null;
        if (handler) native.addEventListener("message", handler as EventListener);
      },
      configurable: true,
      enumerable: true,
    });
  }

  const send = socket.send.bind(socket);
  (socket as unknown as JSONSocket).send = (value: unknown) => send(JSON.stringify(value));

  return socket as unknown as JSONSocket;
}

// 除 Blob 外的一切都可同步转换。避免每次发送都产生一个 promise，更重要的是让
// send() 返回时 bufferedAmount 就是精确的。
function toBytesSync(data: string | ArrayBufferLike | ArrayBufferView | Blob): Uint8Array | null {
  if (typeof data === "string") {
    return textEncoder.encode(data);
  }
  if (typeof Blob !== "undefined" && data instanceof Blob) {
    return null;
  }
  if (ArrayBuffer.isView(data)) {
    return new Uint8Array(data.buffer, data.byteOffset, data.byteLength).slice();
  }
  return new Uint8Array(data as ArrayBufferLike).slice();
}

function abortable<T>(promise: Promise<T>, signal?: AbortSignal): Promise<T> {
  if (!signal) return promise;
  if (signal.aborted) return Promise.reject(new StreamRequestCancelled());

  return new Promise((resolve, reject) => {
    const onAbort = () => reject(new StreamRequestCancelled());
    signal.addEventListener("abort", onAbort, { once: true });
    promise.then(
      (value) => {
        signal.removeEventListener("abort", onAbort);
        resolve(value);
      },
      (err) => {
        signal.removeEventListener("abort", onAbort);
        reject(err);
      },
    );
  });
}

async function toBytes(
  data: string | ArrayBufferLike | ArrayBufferView | Blob,
  signal?: AbortSignal,
): Promise<Uint8Array> {
  if (typeof data === "string") {
    return textEncoder.encode(data);
  }
  if (typeof Blob !== "undefined" && data instanceof Blob) {
    return new Uint8Array(await abortable(data.arrayBuffer(), signal));
  }
  if (ArrayBuffer.isView(data)) {
    return new Uint8Array(data.buffer, data.byteOffset, data.byteLength).slice();
  }
  return new Uint8Array(data as ArrayBufferLike).slice();
}

async function postFrame(
  connID: number,
  kind: number,
  body: Uint8Array,
  name?: string,
  signal?: AbortSignal,
): Promise<void> {
  const headers: Record<string, string> = {
    [HDR_SESSION]: G.session,
    [HDR_GENERATION]: String(G.generation),
    [HDR_CONN]: String(connID),
    [HDR_KIND]: String(kind),
    "Content-Type": "application/octet-stream",
  };
  if (name !== undefined) {
    headers[HDR_NAME] = name;
  }

  if (body.byteLength <= CHUNK_THRESHOLD) {
    await postWithRetry(headers, body, signal);
    return;
  }

  // 像运行时拆分超大调用一样拆分超大帧。
  const chunkID = nanoid();
  const total = Math.ceil(body.byteLength / CHUNK_THRESHOLD);
  for (let i = 0; i < total; i++) {
    const slice = body.subarray(i * CHUNK_THRESHOLD, (i + 1) * CHUNK_THRESHOLD);
    await postWithRetry(
      {
        ...headers,
        [HDR_CHUNK]: chunkID,
        [HDR_CHUNK_INDEX]: String(i),
        [HDR_CHUNK_TOTAL]: String(total),
      },
      slice,
      signal,
    );
  }
}

// 429 意味着 Go 处理器尚未取走已排入的内容。重发同一帧既能保持顺序（发送链尚未
// 推进），又能让请求槽位空闲 —— 这正是阻止一条积压连接饿死该窗口轮询的原因。
async function postWithRetry(
  headers: Record<string, string>,
  body: Uint8Array,
  signal?: AbortSignal,
): Promise<void> {
  // 绝不以零延迟重试。接收方落后是需要时间才能缓解的状态，立即重试就是一串 fetch
  // 的忙循环 —— 每次都是一轮 scheme-handler 往返，且宿主上还是一次 cgo 调用。
  // 从 1ms 起步并逐步加倍。
  let wait = 1;
  for (;;) {
    if (signal?.aborted) throw new StreamRequestCancelled();
    const resp = await fetch(streamURL("send"), {
      method: "POST",
      headers,
      body: body as BodyInit,
      signal,
    });
    if (resp.ok) return;
    if (resp.status !== 429) {
      throw new Error(await resp.text());
    }
    await sleep(wait, signal);
    wait = Math.min(wait * 2, 50);
  }
}

// postBatch 在有限次请求内为一条连接发送若干 data 帧。
// 体格式：count u32，随后 count × ( len u32 | payload )。组合体保持在 CHUNK_THRESHOLD
// 之内：若干单帧很小，组合后绝不能重新制造「单个大帧靠分块规避掉的 WebView2 体大小」
// 问题。
async function postBatch(connID: number, frames: Uint8Array[], signal?: AbortSignal): Promise<void> {
  let start = 0;
  while (start < frames.length) {
    if (frames[start].byteLength > CHUNK_THRESHOLD) {
      await postFrame(connID, KIND_DATA, frames[start], undefined, signal);
      start++;
      continue;
    }

    let end = start;
    let size = 4; // 帧计数
    while (end < frames.length && end - start < MAX_BATCH_FRAMES) {
      const frame = frames[end];
      if (frame.byteLength > CHUNK_THRESHOLD || size + 4 + frame.byteLength > CHUNK_THRESHOLD) {
        break;
      }
      size += 4 + frame.byteLength;
      end++;
    }

    // 接近阈值的单帧可作为普通 POST 体，但一旦算上 count 与 length 头就放不进 batch。
    if (end === start) {
      await postFrame(connID, KIND_DATA, frames[start], undefined, signal);
      start++;
      continue;
    }

    const group = frames.slice(start, end);
    if (group.length === 1) {
      await postFrame(connID, KIND_DATA, group[0], undefined, signal);
    } else {
      await postBatchGroup(connID, group, signal);
    }
    start = end;
  }
}

async function postBatchGroup(connID: number, frames: Uint8Array[], signal?: AbortSignal): Promise<void> {
  if (frames.length === 0 || frames.length > MAX_BATCH_FRAMES) {
    throw new Error("invalid stream batch size");
  }

  let body = buildBatch(frames);
  let sent = 0;
  let wait = 1;
  for (;;) {
    if (signal?.aborted) throw new StreamRequestCancelled();
    const resp = await fetch(streamURL("send"), {
      method: "POST",
      headers: {
        [HDR_SESSION]: G.session,
        [HDR_GENERATION]: String(G.generation),
        [HDR_CONN]: String(connID),
        [HDR_KIND]: String(KIND_DATA),
        [HDR_BATCH]: String(frames.length - sent),
        "Content-Type": "application/octet-stream",
      },
      body: body as BodyInit,
      signal,
    });
    if (resp.ok) return;
    if (resp.status !== 429) {
      throw new Error(await resp.text());
    }
    // 接收方只取走了 batch 的一个前缀。只重发剩余部分，既保持顺序又不重复投递。
    const accepted = Number(resp.headers.get(HDR_BATCH) ?? 0);
    const remaining = frames.length - sent;
    if (!Number.isInteger(accepted) || accepted < 0 || accepted >= remaining) {
      // 零是合法的（无进展），但越界的确认是协议错误。零继续走下面的重试路径。
      if (accepted !== 0) throw new Error("invalid stream batch acknowledgement");
    }
    sent += accepted;
    body = buildBatch(frames.slice(sent));
    await sleep(wait, signal);
    wait = Math.min(wait * 2, 50);
  }
}

/**
 * 编码一个 batch 体：`count(u32 BE)` 后跟 `count × (len(u32 BE) + payload)`。
 * @internal 纯函数，供白盒单测直接调用。
 */
export function buildBatch(frames: Uint8Array[]): Uint8Array {
  let size = 4;
  for (const f of frames) size += 4 + f.byteLength;
  const out = new Uint8Array(size);
  const view = new DataView(out.buffer);
  view.setUint32(0, frames.length);
  let off = 4;
  for (const f of frames) {
    view.setUint32(off, f.byteLength);
    off += 4;
    out.set(f, off);
    off += f.byteLength;
  }
  return out;
}

function startPolling(): void {
  if (G.polling || !hasDOM) return;
  G.polling = true;
  void pollLoop();
}

/**
 * 整个页面共用的一次轮询，被所有连接共享。
 *
 * 这里刻意没有轮询间隔，也没有任何自适应。服务端会把请求挂起直到有帧存在，因此
 * 投递延迟已近乎为 0，客户端间隔只会增加延迟。响应在途期间抵达的帧只会累积并搭上
 * 下一次响应，这让往返本身成为批处理窗口 —— 负载升高时它会变宽，无需任何测量。
 */
async function pollLoop(): Promise<void> {
  let backoff = 0;
  const abort = new AbortController();
  G.pollAbort = abort;

  while (G.connections.size > 0) {
    try {
      const resp = await fetch(streamURL("poll"), {
        method: "GET",
        headers: {
          [HDR_SESSION]: G.session,
          [HDR_GENERATION]: String(G.generation),
        },
        cache: "no-store",
        signal: abort.signal,
      });

      if (resp.status === 410) {
        // session 消失了：应用正在关闭，窗口已关闭，或我们被判定不可达而回收。
        // 没有可重试的余地。
        closeAll(1001, "session closed");
        break;
      }
      if (!resp.ok && resp.status !== 204) {
        if (!isRetryablePollStatus(resp.status)) {
          failAll("stream poll failed: " + resp.status);
          break;
        }
        throw new Error("retryable stream poll failure: " + resp.status);
      }

      backoff = 0;
      if (resp.status !== 204) {
        let buf: ArrayBuffer;
        try {
          buf = await resp.arrayBuffer();
        } catch {
          // 成功的响应被产出时，Go 队列已被消费。在此时刻之后被中断的体无法通过重试
          // 找回，因此暴露这次丢失并停止，而不是带着缺失的帧静默继续。
          failAll("stream poll response body failed");
          break;
        }
        const frames = decodeFrames(buf);
        if (frames === null) {
          // 帧格式不匹配意味着陈旧的缓存运行时遇到更新的 Go 侧。重试无法修复，
          // 而让它落入下面的退避会静默地永远自旋，因此用理由让所有连接失败。
          closeAll(1002, "unrecognised stream framing");
          break;
        }
        deliver(frames);
      }
      // 无论收到数据还是 hold 到期，都立即重新轮询。
    } catch {
      if (abort.signal.aborted || G.connections.size === 0) break;
      backoff = backoff ? Math.min(backoff * 2, BACKOFF_MAX) : BACKOFF_MIN;
      try {
        await sleep(backoff, abort.signal);
      } catch {
        break;
      }
    }
  }

  if (G.pollAbort === abort) G.pollAbort = undefined;
  G.polling = false;

  // 在我们收尾期间打开的连接：把循环重新挑起来。
  if (G.connections.size > 0) {
    startPolling();
  }
}

function isRetryablePollStatus(status: number): boolean {
  return status === 408 || status === 425 || status === 429 || (status >= 500 && status <= 599);
}

/** 轮询响应中解出的单帧。 */
export interface InFrame {
  connID: number;
  kind: number;
  payload: ArrayBuffer;
}

// 在派发任何部分之前校验整个响应。短响应不得先投递一个合法前缀再抛 RangeError：
// Go 队列此时已被抽干，重试无法找回那些帧。应把整个响应视为协议错误。
/**
 * 解码轮询体。校验 magic `"WS1\0"`、`flags & ~1 === 0`，随后逐帧解析
 * `(connID u32 BE, kind u8, len u32 BE, payload)`。
 * @returns 帧数组；任何格式/长度不匹配返回 `null`。
 * @internal 纯函数，供白盒单测直接调用。
 */
export function decodeFrames(buf: ArrayBuffer): InFrame[] | null {
  if (buf.byteLength < 9) return null;
  const dv = new DataView(buf);
  if (
    dv.getUint8(0) !== 0x57 ||
    dv.getUint8(1) !== 0x53 ||
    dv.getUint8(2) !== 0x31 ||
    dv.getUint8(3) !== 0x00 ||
    (dv.getUint8(4) & ~1) !== 0
  ) {
    return null;
  }

  const count = dv.getUint32(5);
  const frames: InFrame[] = [];
  let off = 9;

  for (let i = 0; i < count; i++) {
    if (off + 9 > buf.byteLength) return null;
    const connID = dv.getUint32(off);
    off += 4;
    const kind = dv.getUint8(off);
    off += 1;
    const len = dv.getUint32(off);
    off += 4;
    if (kind > KIND_ERROR || len > buf.byteLength - off) return null;
    const payload = buf.slice(off, off + len);
    off += len;
    frames.push({ connID, kind, payload });
  }

  return off === buf.byteLength ? frames : null;
}

function deliver(frames: InFrame[]): void {
  for (const frame of frames) {
    const connID = frame.connID;
    const kind = frame.kind;
    const payload = frame.payload;
    const conn = G.connections.get(connID);
    if (!conn) continue;

    switch (kind) {
      case KIND_DATA:
        conn._message(payload);
        break;
      case KIND_OPEN:
        conn._opened();
        break;
      case KIND_CLOSE:
        conn._closed(1000, "", true);
        break;
      case KIND_ERROR:
        conn._fail(new Error(new TextDecoder().decode(payload)));
        break;
    }
  }
}

function closeAll(code: number, reason: string): void {
  for (const conn of [...G.connections.values()]) {
    conn._closed(code, reason, false);
  }
}

function failAll(reason: string): void {
  for (const conn of [...G.connections.values()]) {
    conn._fail(new Error(reason));
  }
}
