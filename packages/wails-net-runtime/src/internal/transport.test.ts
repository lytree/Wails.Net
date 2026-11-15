import { afterEach, describe, expect, it, vi } from "vitest";
import { fnv1a } from "./fnv1a.js";
import { Transport, unpack } from "./transport.js";
import { CallError } from "./call-error.js";
import type { WailsResponse } from "./types.js";

describe("fnv1a", () => {
  it("matches known FNV-1a 32-bit vectors (与后端 BindingManager.FNV1aHash 一致)", () => {
    expect(fnv1a("")).toBe(2166136261);
    expect(fnv1a("a")).toBe(0xe40c292c);
    // FNV-1a 32 位标准测试向量；0x85944171 是 FNV-1（非 1a）的同输入结果，不可混用
    expect(fnv1a("foobar")).toBe(0xbf9cf968);
  });

  it("is stable across calls", () => {
    expect(fnv1a("GreetingService.Greet")).toBe(fnv1a("GreetingService.Greet"));
  });
});

describe("unpack (双层响应解包)", () => {
  it("成功响应返回 result.result", () => {
    const resp: WailsResponse = {
      id: "1",
      type: "response",
      result: { result: "hello", error: null },
    };
    expect(unpack(resp)).toBe("hello");
  });

  it("业务错误抛出 CallError", () => {
    const resp: WailsResponse = {
      id: "2",
      type: "response",
      result: { result: null, error: { message: "boom", cause: null, kind: "RuntimeError" } },
    };
    expect(() => unpack(resp)).toThrow(CallError);
    try {
      unpack(resp);
    } catch (e) {
      expect((e as CallError).kind).toBe("RuntimeError");
      expect((e as CallError).message).toBe("boom");
    }
  });

  it("顶层 error 信封也被抛出", () => {
    const resp = {
      id: "3",
      type: "error",
      result: { result: null, error: { message: "bad", cause: null, kind: "TypeError" } },
    } as unknown as WailsResponse;
    expect(() => unpack(resp)).toThrow(CallError);
  });

  it("兼容非嵌套旧格式", () => {
    const resp = { id: "4", type: "response", result: "plain" } as unknown as WailsResponse;
    expect(unpack(resp)).toBe("plain");
  });
});

describe("Transport 通道选择（RH-IPC-04 二态收敛）", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    delete (globalThis as { _wails?: unknown })._wails;
  });

  it("无原生通道时，大消息用单次 scheme POST 发送，不再分块", async () => {
    // 非浏览器环境：init() 只读取 flags，不挂监听器，nativePost 保持 undefined。
    const fetchMock = vi.fn(async (_url: string, _init?: RequestInit) =>
      new Response(JSON.stringify({ id: "1", type: "response", result: { result: null, error: null } }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      }));
    vi.stubGlobal("fetch", fetchMock);

    const transport = new Transport();
    // > 512KB：旧实现在这里会拆成多个 chunk 逐个 POST。
    await transport.invoke("call", { blob: "x".repeat(600 * 1024) });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/wails/message");
    const headers = (init as RequestInit).headers as Record<string, string>;
    expect(Object.keys(headers).some((k) => k.startsWith("x-wails-chunk"))).toBe(false);
  });
});
