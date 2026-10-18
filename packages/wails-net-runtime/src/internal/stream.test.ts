import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { buildBatch, decodeFrames } from "./stream.js";

/**
 * Stream 前端运行时（`src/internal/stream.ts`）白盒单测。
 *
 * 测试环境为 vitest `node`（见 vitest.config.ts，无 jsdom 依赖）：
 * - 纯函数（`decodeFrames` / `buildBatch`）直接在 node 下测试；
 * - 依赖 `window` 的路径（{@link Stream} 工厂选择、chunk 拆分）先安装最小
 *   `window` 桩并 `vi.resetModules()`，再动态 `import("./stream.js")`，
 *   使模块顶部的 `hasDOM` 判定为 true。
 */

/** 最小 Response 桩。 */
function response(status: number, body: ArrayBuffer = new ArrayBuffer(0)): Response {
  return {
    status,
    ok: status >= 200 && status < 300,
    arrayBuffer: async () => body,
    text: async () => "",
    headers: new Headers(),
  } as unknown as Response;
}

/** 永不主动 resolve、仅在 signal abort 时 reject 的「挂起轮询」桩，模拟服务端 hold。 */
function heldPoll(init?: { signal?: AbortSignal | null }): Promise<Response> {
  return new Promise<Response>((_resolve, reject) => {
    const signal = init?.signal ?? undefined;
    const onAbort = () => reject(new DOMException("aborted", "AbortError"));
    if (signal?.aborted) onAbort();
    else signal?.addEventListener("abort", onAbort, { once: true });
  });
}

/** 安装最小 window 桩（含 sessionStorage / location.origin / _wails）。 */
function installWindowStub(): void {
  const store = new Map<string, string>();
  const sessionStorage = {
    getItem: (k: string) => (store.has(k) ? (store.get(k) as string) : null),
    setItem: (k: string, v: string) => {
      store.set(k, v);
    },
    removeItem: (k: string) => {
      store.delete(k);
    },
    clear: () => store.clear(),
  };
  vi.stubGlobal("window", {
    name: "",
    location: { origin: "http://localhost" },
    sessionStorage,
    _wails: {},
  });
}

/** 构造一个合法的轮询体（magic "WS1\0" + flags + count + 逐帧）。 */
function encodePoll(frames: { connID: number; kind: number; payload: number[] }[], flags = 0): ArrayBuffer {
  const payloads = frames.map((f) => new Uint8Array(f.payload));
  let size = 9;
  for (const p of payloads) size += 9 + p.byteLength;
  const buf = new ArrayBuffer(size);
  const dv = new DataView(buf);
  dv.setUint8(0, 0x57); // 'W'
  dv.setUint8(1, 0x53); // 'S'
  dv.setUint8(2, 0x31); // '1'
  dv.setUint8(3, 0x00);
  dv.setUint8(4, flags);
  dv.setUint32(5, frames.length);
  let off = 9;
  for (let i = 0; i < frames.length; i++) {
    dv.setUint32(off, frames[i].connID);
    off += 4;
    dv.setUint8(off, frames[i].kind);
    off += 1;
    dv.setUint32(off, payloads[i].byteLength);
    off += 4;
    new Uint8Array(buf, off, payloads[i].byteLength).set(payloads[i]);
    off += payloads[i].byteLength;
  }
  return buf;
}

describe("decodeFrames", () => {
  it("解析合法轮询体为 (connID, kind, payload) 帧序列", () => {
    const buf = encodePoll([
      { connID: 1, kind: 0, payload: [1, 2, 3] },
      { connID: 7, kind: 1, payload: [] },
    ]);
    const frames = decodeFrames(buf);
    expect(frames).not.toBeNull();
    expect(frames!).toHaveLength(2);
    expect(frames![0].connID).toBe(1);
    expect(frames![0].kind).toBe(0);
    expect(Array.from(new Uint8Array(frames![0].payload))).toEqual([1, 2, 3]);
    expect(frames![1].connID).toBe(7);
    expect(frames![1].kind).toBe(1);
    expect(new Uint8Array(frames![1].payload).byteLength).toBe(0);
  });

  it("接受 flags===1（保留位之外的位仅第 0 位可用）", () => {
    const frames = decodeFrames(encodePoll([{ connID: 2, kind: 2, payload: [] }], 1));
    expect(frames).not.toBeNull();
    expect(frames![0].kind).toBe(2);
  });

  it("过短（< 9 字节）返回 null", () => {
    expect(decodeFrames(new ArrayBuffer(8))).toBeNull();
  });

  it("magic 不匹配返回 null", () => {
    const buf = encodePoll([{ connID: 1, kind: 0, payload: [9] }]);
    new DataView(buf).setUint8(2, 0x00); // 破坏 '1'
    expect(decodeFrames(buf)).toBeNull();
  });

  it("flags 含非法位（& ~1 !== 0）返回 null", () => {
    expect(decodeFrames(encodePoll([{ connID: 1, kind: 0, payload: [] }], 2))).toBeNull();
  });

  it("声明帧数多于实际可读帧时返回 null（截断响应）", () => {
    const truncated = new ArrayBuffer(9);
    const dv = new DataView(truncated);
    dv.setUint8(0, 0x57);
    dv.setUint8(1, 0x53);
    dv.setUint8(2, 0x31);
    dv.setUint8(3, 0x00);
    dv.setUint32(5, 1); // 声称 1 帧，却不带帧头
    expect(decodeFrames(truncated)).toBeNull();
  });

  it("尾部多余字节返回 null", () => {
    const buf = encodePoll([{ connID: 1, kind: 0, payload: [1] }]);
    const withTrailer = new Uint8Array(buf.byteLength + 1);
    withTrailer.set(new Uint8Array(buf), 0);
    expect(decodeFrames(withTrailer.buffer)).toBeNull();
  });

  it("kind 超出 KIND_ERROR(3) 返回 null", () => {
    expect(decodeFrames(encodePoll([{ connID: 1, kind: 4, payload: [] }]))).toBeNull();
  });
});

describe("buildBatch（batch 帧编码）", () => {
  it("编码 count(u32 BE) + count×(len u32 BE + payload)", () => {
    const body = buildBatch([new Uint8Array([1, 2]), new Uint8Array([3, 4])]);
    expect(Array.from(body)).toEqual([
      0, 0, 0, 2, // count = 2
      0, 0, 0, 2, 1, 2, // frame 0
      0, 0, 0, 2, 3, 4, // frame 1
    ]);
  });

  it("空 batch 仅含 count=0（4 字节）", () => {
    const body = buildBatch([]);
    expect(body.byteLength).toBe(4);
    expect(new DataView(body.buffer).getUint32(0)).toBe(0);
  });

  it("可被往返解码，保留原帧顺序与内容", () => {
    const frames = [new Uint8Array(0), new Uint8Array([9]), new Uint8Array([1, 2, 3])];
    const body = buildBatch(frames);
    const dv = new DataView(body.buffer, body.byteOffset, body.byteLength);
    expect(dv.getUint32(0)).toBe(3);
    const out: number[][] = [];
    let off = 4;
    for (let i = 0; i < 3; i++) {
      const len = dv.getUint32(off);
      off += 4;
      out.push(Array.from(body.subarray(off, off + len)));
      off += len;
    }
    expect(off).toBe(body.byteLength);
    expect(out).toEqual([[], [9], [1, 2, 3]]);
  });
});

describe("Stream 工厂选择", () => {
  beforeEach(() => {
    vi.resetModules();
    installWindowStub();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("window._wails.streamFactory 为函数时返回其产物（server 模式真 WebSocket）", async () => {
    const fake = { readyState: 1, binaryType: "arraybuffer", send() {}, close() {} };
    (globalThis as unknown as { window: { _wails: Record<string, unknown> } }).window._wails.streamFactory =
      () => fake;

    const { Stream } = await import("./stream.js");
    expect(Stream("telemetry")).toBe(fake);
  });

  it("无 streamFactory 时返回 WailsSocket 且初始 readyState 为 CONNECTING", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn((input: unknown, init?: { signal?: AbortSignal }) =>
        String(input).endsWith("/poll") ? heldPoll(init) : Promise.resolve(response(204)),
      ),
    );

    const { Stream, WailsSocket } = await import("./stream.js");
    const socket = Stream("telemetry");
    expect(socket).toBeInstanceOf(WailsSocket);
    expect((socket as WailsSocket).readyState).toBe(WailsSocket.CONNECTING);

    (socket as WailsSocket).close();
    await vi.waitFor(() => expect((socket as WailsSocket).readyState).toBe(WailsSocket.CLOSED), {
      interval: 1,
      timeout: 300,
    });
  });
});

describe("postFrame 帧编码（chunk 拆分）", () => {
  beforeEach(() => {
    vi.resetModules();
    installWindowStub();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("单帧 > 512KiB 时按 chunk 拆分，携带一致的 chunk id 与 index/total 头", async () => {
    const chunks: { index: number; total: number; id: string; len: number }[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn((input: unknown, init?: { signal?: AbortSignal; headers?: Record<string, string>; body?: Uint8Array }) => {
        if (String(input).endsWith("/poll")) return heldPoll(init);
        if (init?.headers?.["x-wails-stream-kind"] === "0") {
          chunks.push({
            index: Number(init.headers["x-wails-stream-chunk-index"]),
            total: Number(init.headers["x-wails-stream-chunk-total"]),
            id: String(init.headers["x-wails-stream-chunk"]),
            len: (init.body as Uint8Array).byteLength,
          });
        }
        return Promise.resolve(response(204));
      }),
    );

    const { WailsSocket } = await import("./stream.js");
    const socket = new WailsSocket("test");
    socket._opened();
    socket.send(new Uint8Array(600 * 1024));

    await vi.waitFor(() => expect(chunks).toHaveLength(2), { interval: 1, timeout: 300 });
    expect(chunks.map((c) => c.index)).toEqual([0, 1]);
    expect(chunks.every((c) => c.total === 2)).toBe(true);
    expect(chunks.every((c) => c.len <= 512 * 1024)).toBe(true);
    expect(new Set(chunks.map((c) => c.id)).size).toBe(1);

    socket.close();
    await vi.waitFor(() => expect(socket.readyState).toBe(WailsSocket.CLOSED), {
      interval: 1,
      timeout: 300,
    });
  });

  it("单帧 ≤ 512KiB 时不拆分（无 chunk 头）", async () => {
    let dataPosts = 0;
    let sawChunkHeader = false;
    vi.stubGlobal(
      "fetch",
      vi.fn((input: unknown, init?: { signal?: AbortSignal; headers?: Record<string, string> }) => {
        if (String(input).endsWith("/poll")) return heldPoll(init);
        if (init?.headers?.["x-wails-stream-kind"] === "0") {
          dataPosts++;
          if (init.headers["x-wails-stream-chunk"] !== undefined) sawChunkHeader = true;
        }
        return Promise.resolve(response(204));
      }),
    );

    const { WailsSocket } = await import("./stream.js");
    const socket = new WailsSocket("test");
    socket._opened();
    socket.send(new Uint8Array(1024));

    await vi.waitFor(() => expect(dataPosts).toBe(1), { interval: 1, timeout: 300 });
    expect(sawChunkHeader).toBe(false);

    socket.close();
    await vi.waitFor(() => expect(socket.readyState).toBe(WailsSocket.CLOSED), {
      interval: 1,
      timeout: 300,
    });
  });
});
