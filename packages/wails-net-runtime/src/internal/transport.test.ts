import { describe, expect, it } from "vitest";
import { fnv1a } from "./fnv1a.js";
import { unpack } from "./transport.js";
import { CallError } from "./call-error.js";
import type { WailsResponse } from "./types.js";

describe("fnv1a", () => {
  it("matches known FNV-1a 32-bit vectors (与后端 BindingManager.FNV1aHash 一致)", () => {
    expect(fnv1a("")).toBe(2166136261);
    expect(fnv1a("a")).toBe(0xe40c292c);
    expect(fnv1a("foobar")).toBe(0x85944171);
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
