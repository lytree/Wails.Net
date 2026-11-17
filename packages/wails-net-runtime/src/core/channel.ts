/**
 * Wails.Net 双向通道 API（RH-IPC-05，对齐 Tauri v2 Channel / Wails v3 Channel 原语）。
 *
 * 通道弥补请求-响应模型的局限：后端命令可通过 `ICommandContext.Channel` 主动向前端
 * 推送流式消息（长耗时任务进度回传），前端也可通过同一通道向命令回送消息。
 *
 * 用法：
 * ```ts
 * import { createChannel, wails } from "@wailsnet/runtime";
 *
 * const ch = await createChannel();
 * ch.onMessage((progress) => console.log("progress:", progress));
 * await wails.call("Updater.Install", [args], { channel: ch.id });
 * ch.send({ status: "ready" });      // 前端 → 命令
 * await ch.close();                  // 结束
 * ```
 *
 * 协议（与后端 `ChannelProtocol` 对齐）：
 * - 上行 `channel:open` / `channel:message` / `channel:close`；
 * - 下行复用事件通道：保留事件名 `wails:channel:<id>`，由 transport 在用户事件回调前拦截。
 */

import { transport, type CancellablePromise } from "../internal/transport.js";

/** 双向通道句柄。 */
export interface WailsChannel {
  /** 通道唯一标识（前端未指定时由后端生成）。 */
  readonly id: string;
  /**
   * 订阅后端下发的消息，返回取消订阅函数。
   * @param callback 载荷回调；`event` 为可选事件标识（通常为 null）。
   */
  onMessage(callback: (payload: unknown, event: string | null) => void): () => void;
  /** 向后端命令发送消息（fire-and-forget）。 */
  send(payload: unknown, event?: string | null): void;
  /** 关闭通道（后端会下发关闭信号，之后 send 抛错）。 */
  close(): CancellablePromise<unknown>;
}

/** 创建通道的选项。 */
export interface CreateChannelOptions {
  /** 指定通道 ID；缺省由后端生成。 */
  id?: string;
}

/** 已关闭通道上执行 send 时的错误。 */
export class ChannelClosedError extends Error {
  constructor(id: string) {
    super(`channel "${id}" 已关闭`);
    this.name = "ChannelClosedError";
  }
}

/** 内部实现：带关闭态的通道。 */
class ChannelImpl implements WailsChannel {
  readonly id: string;
  private _closed = false;

  constructor(id: string) {
    this.id = id;
    // 构造期即订阅关闭信号：即使用户从未调用 onMessage，
    // 后端下发的 closed 也能正确标记关闭态（send 抛 ChannelClosedError）。
    transport.onChannelMessage(id, (_payload, _event, closed) => {
      if (closed) this._closed = true;
    });
  }

  get isClosed(): boolean {
    return this._closed;
  }

  onMessage(callback: (payload: unknown, event: string | null) => void): () => void {
    return transport.onChannelMessage(this.id, (payload, event, closed) => {
      // 关闭信号由构造期订阅负责，这里只派发业务消息。
      if (closed) return;
      callback(payload, event);
    });
  }

  send(payload: unknown, event?: string | null): void {
    if (this._closed) throw new ChannelClosedError(this.id);
    transport.send("channel:message", { channelId: this.id, event: event ?? null, data: payload });
  }

  close(): CancellablePromise<unknown> {
    this._closed = true;
    return transport.invoke("channel:close", { channelId: this.id });
  }
}

/**
 * 创建一个与后端绑定的双向通道。
 * @param options 可选；`id` 指定通道 ID（重复 ID 幂等：返回同一通道语义）。
 */
export async function createChannel(options?: CreateChannelOptions): Promise<WailsChannel> {
  const result = await transport.invoke<{ channelId?: string }>(
    "channel:open",
    options?.id != null ? { id: options.id } : {},
  );
  const id = result?.channelId;
  if (typeof id !== "string" || id.length === 0) {
    throw new Error("channel:open 未返回 channelId");
  }
  return new ChannelImpl(id);
}
