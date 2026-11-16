import { afterEach, describe, expect, it, vi } from "vitest";

/**
 * 双向通道（RH-IPC-05）前端协议测试。
 * 与后端 `ChannelProtocol` / `MessageProcessor.Channels.cs` 对齐：
 * - 上行 `channel:open` / `channel:message` / `channel:close`；
 * - 下行复用事件通道：保留事件名 `wails:channel:<id>`，在用户事件回调前拦截。
 */

import { ChannelClosedError, createChannel } from "./channel.js";
import { wails, call } from "./runtime.js";
import { transport } from "../internal/transport.js";

/** 构造一个后端响应（双层结构，直接把 result 作为业务值返回）。 */
function jsonResponse(id: string, result: unknown): Response {
  return new Response(JSON.stringify({ id, type: "response", result }), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}

/**
 * 构造 fetch mock：回显请求 id 并返回固定 result。
 * Transport 以自增 counter 作为消息 id 关联 pending，响应 id 必须与请求一致。
 */
function mockFetch(result: unknown): ReturnType<typeof vi.fn> {
  return vi.fn(async (_url: string, init?: RequestInit) => {
    const req = JSON.parse(String(init?.body ?? "{}")) as { id?: string };
    return jsonResponse(req.id ?? "0", result);
  });
}

/** 提取最近一次 fetch 的上行消息信封。 */
function lastSentMessage(fetchMock: ReturnType<typeof vi.fn>): { id: string; type: string; payload: Record<string, unknown> } {
  const calls = fetchMock.mock.calls as unknown as [string, RequestInit][];
  const [, init] = calls[calls.length - 1];
  return JSON.parse(String(init.body));
}

describe("channel（RH-IPC-05）", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  afterEach(async () => {
    vi.unstubAllGlobals();
    delete (globalThis as { _wailsEmitEvent?: unknown })._wailsEmitEvent;
    transport.destroy();
    await Promise.resolve();
  });

  it("createChannel 发送 channel:open 并携带后端返回的 channelId", async () => {
    fetchMock = mockFetch({ channelId: "ch-test-1" });
    vi.stubGlobal("fetch", fetchMock);

    const ch = await createChannel({ id: "ch-test-1" });

    expect(ch.id).toBe("ch-test-1");
    const msg = lastSentMessage(fetchMock);
    expect(msg.type).toBe("channel:open");
    expect(msg.payload).toEqual({ id: "ch-test-1" });
  });

  it("wails.call(name, args, { channel }) 在 payload 附带 channelId", async () => {
    fetchMock = mockFetch({ result: "ok", error: null });
    vi.stubGlobal("fetch", fetchMock);

    await call("Progress.Run", [], { channel: "ch-abc" });

    const msg = lastSentMessage(fetchMock);
    expect(msg.type).toBe("call");
    expect(msg.payload.channelId).toBe("ch-abc");
  });

  it("下行 wails:channel:<id> 路由到通道订阅者，不进入用户事件回调", async () => {
    fetchMock = mockFetch({ channelId: "ch-dl" });
    vi.stubGlobal("fetch", fetchMock);
    // 非浏览器环境补 window，使 init() 挂载下行入口 _wailsEmitEvent。
    vi.stubGlobal("window", {});

    const ch = await createChannel();
    expect(ch.id).toBe("ch-dl");

    const received: unknown[] = [];
    ch.onMessage((payload) => received.push(payload));

    const wildcardCb = vi.fn();
    const directNameCb = vi.fn();
    transport.on("wails:*", wildcardCb);
    transport.on("wails:channel:ch-dl", directNameCb);

    (globalThis as { _wailsEmitEvent?: (n: string, d: unknown) => void })._wailsEmitEvent!(
      "wails:channel:ch-dl",
      { event: null, payload: { percent: 50 }, closed: false },
    );

    expect(received).toEqual([{ percent: 50 }]);
    // 通道消息对事件订阅者（含精确命中保留事件名者）不可见。
    expect(wildcardCb).not.toHaveBeenCalled();
    expect(directNameCb).not.toHaveBeenCalled();
  });

  it("后端下发 closed 后通道关闭，继续 send 抛 ChannelClosedError", async () => {
    fetchMock = mockFetch({ channelId: "ch-close" });
    vi.stubGlobal("fetch", fetchMock);
    vi.stubGlobal("window", {});

    const ch = await createChannel();
    (globalThis as { _wailsEmitEvent?: (n: string, d: unknown) => void })._wailsEmitEvent!(
      "wails:channel:ch-close",
      { event: null, payload: null, closed: true },
    );

    expect(() => ch.send("x")).toThrow(ChannelClosedError);
  });

  it("send 走 channel:message，close 走 channel:close", async () => {
    fetchMock = mockFetch({ channelId: "ch-flow" });
    vi.stubGlobal("fetch", fetchMock);
    vi.stubGlobal("window", {});

    const ch = await createChannel();
    ch.send({ status: "ready" }, "evt-1");
    await vi.waitFor(() => expect(fetchMock.mock.calls.length).toBe(2));
    expect(lastSentMessage(fetchMock).type).toBe("channel:message");
    expect(lastSentMessage(fetchMock).payload).toEqual({
      channelId: "ch-flow",
      event: "evt-1",
      data: { status: "ready" },
    });

    await ch.close();
    expect(lastSentMessage(fetchMock).type).toBe("channel:close");
    expect(lastSentMessage(fetchMock).payload).toEqual({ channelId: "ch-flow" });
    expect(() => ch.send("x")).toThrow(ChannelClosedError);
  });

  it("wails.channel.create 与独立 createChannel 等价", async () => {
    fetchMock = mockFetch({ channelId: "ch-ns" });
    vi.stubGlobal("fetch", fetchMock);

    const ch = await wails.channel.create({ id: "ch-ns" });
    expect(ch.id).toBe("ch-ns");
  });
});
