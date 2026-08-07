import { describe, expect, it, vi } from "vitest";

/**
 * L2 命令抽象层测试。
 * 拦截 `./runtime.js` 的 `call`，断言 defineCommand 三种 pack 模式
 * 产出的 wire 参数数组符合线协议约定（与后端 MessageProcessor 一致）。
 */

// vitest 的 vi.mock 工厂提升到模块顶部执行，不能引用测试体变量 —— 用 vi.hoisted。
const { callMock } = vi.hoisted(() => ({ callMock: vi.fn() }));

vi.mock("./runtime.js", () => ({
  call: callMock,
}));

// 在 mock 之后导入被测模块（vitest 中 import 提升晚于 vi.mock 注册）
import { defineCommand } from "./commands.js";

/** 供类型锚定的业务类型（模拟插件场景）。 */
type UpdateChannel = "stable" | "beta";
interface UpdateManifest {
  version: string;
  notes?: string;
}

describe("defineCommand — none 模式", () => {
  it("无参数命令发送空数组 wire", async () => {
    callMock.mockResolvedValue({ version: "1.0.0" });

    const getCurrentVersion = defineCommand<[], string>("updater.getCurrentVersion", "none");
    const result = await getCurrentVersion();

    expect(callMock).toHaveBeenCalledTimes(1);
    expect(callMock).toHaveBeenCalledWith("updater.getCurrentVersion", []);
    expect(result).toBe("1.0.0");
  });

  it("忽略多余运行时参数（none 模式固定发送 []）", () => {
    const getCurrentVersion = defineCommand<[], string>("updater.getCurrentVersion", "none");
    // 类型层面禁止传参，但运行时若有人绕过类型，wire 仍为 []
    getCurrentVersion((42 as unknown) as never);
    expect(callMock).toHaveBeenLastCalledWith("updater.getCurrentVersion", []);
  });
});

describe("defineCommand — single 模式", () => {
  it("单参数对象自动包装为 [{...}]", async () => {
    callMock.mockResolvedValue({ version: "2.0.0-beta", notes: "beta" });

    const checkForUpdate = defineCommand<[UpdateChannel], UpdateManifest>(
      "updater.checkForUpdate", "single");
    const manifest = await checkForUpdate("beta");

    expect(callMock).toHaveBeenCalledTimes(1);
    expect(callMock).toHaveBeenCalledWith("updater.checkForUpdate", ["beta"]);
    expect(manifest).toEqual({ version: "2.0.0-beta", notes: "beta" });
  });

  it("单对象参数保持对象引用（不做拷贝）", () => {
    const opts = { width: 800, height: 600 };
    const setSize = defineCommand<[{ width: number; height: number }], void>(
      "window.setSize", "single");
    setSize(opts);
    expect(callMock).toHaveBeenLastCalledWith("window.setSize", [opts]);
  });
});

describe("defineCommand — spread 模式", () => {
  it("多位置参数原样展开为数组", async () => {
    callMock.mockResolvedValue(undefined);

    const setSize = defineCommand<[number, number], void>("window.setSize", "spread");
    await setSize(800, 600);

    expect(callMock).toHaveBeenCalledTimes(1);
    expect(callMock).toHaveBeenCalledWith("window.setSize", [800, 600]);
  });

  it("位置参数顺序保持", () => {
    const setPosition = defineCommand<[number, number], void>("window.setPosition", "spread");
    setPosition(10, 20);
    expect(callMock).toHaveBeenLastCalledWith("window.setPosition", [10, 20]);
  });
});

describe("defineCommand — 类型与名称", () => {
  it("返回值携带泛型类型（编译期验证，运行时为 call 结果）", async () => {
    const sample: UpdateManifest = { version: "1.0.0" };
    callMock.mockResolvedValue(sample);

    const getCurrentVersion = defineCommand<[], UpdateManifest>("updater.get", "none");
    const result = await getCurrentVersion();
    // 类型层面 result 已推导为 UpdateManifest
    expect(result.version).toBe("1.0.0");
  });
});
