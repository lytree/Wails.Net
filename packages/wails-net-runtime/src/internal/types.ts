/**
 * Wails.Net 前后端 IPC 协议类型定义。
 *
 * 协议与 Wails.Net 后端 `MessageProcessor` / `ResponseMessage` 严格对齐：
 * - 上行（前端 → 后端）：`WailsMessage { id, type, payload, windowId?, origin? }`
 * - 下行（后端 → 前端）：`WailsResponse { id, type, result: { result, error } }`
 *
 * 重要：响应是**双层**结构，`result.result` 才是业务返回值，`result.error` 为错误。
 * 部分路径下 `type` 会变成 `"error"`（协议/解析错误），需兼容处理。
 */

/** 错误种类，与后端 `Wails.Net.Errors.CallErrorKind` 一致。 */
export type CallErrorKind = "ReferenceError" | "TypeError" | "RuntimeError";

/** 后端返回的业务错误对象。 */
export interface WailsCallError {
  /** 错误消息。 */
  message: string;
  /** 触发此错误的底层原因（可能为 null）。 */
  cause: string | null;
  /** 错误种类。 */
  kind: CallErrorKind;
}

/** 上行消息信封（前端 → 后端）。 */
export interface WailsMessage<P = unknown> {
  /** 调用 ID，必须全局唯一且为字符串（取消机制依赖它作为 callId）。 */
  id: string;
  /** 消息类型 / 命令名，如 "call"、"event.emit"、"cancel"、"window.setTitle"、"clipboard.getText"。 */
  type: string;
  /** 载荷（任意 JSON）。 */
  payload: P;
  /** 可选：目标窗口 ID（原生通道会自动注入；HTTP 通道不会）。 */
  windowId?: number;
  /** 可选：来源 URL（Capability.remote 校验用）。 */
  origin?: string;
}

/** 下行响应信封（后端 → 前端）。`type` 为 "response" 或 "error"。 */
export interface WailsResponse<R = unknown> {
  /** 对应上行消息的 id。 */
  id: string;
  /** "response" 表示正常响应（即使其中包含业务错误）；"error" 表示协议/解析错误。 */
  type: "response" | "error";
  /** 业务结果：{ result, error } 双层结构。 */
  result: {
    /** 业务返回值（成功时）；失败时通常为 null。 */
    result: R | null;
    /** 业务错误（失败时）；成功时为 null。 */
    error: WailsCallError | null;
  };
}

/** 事件载荷（上行 emit / 下行推送共用）。 */
export interface WailsEventPayload<D = unknown> {
  /** 事件名（内建事件以 `wails:` 前缀）。 */
  name: string;
  /** 事件数据。 */
  data: D;
  /** 发送方窗口 ID（可选）。 */
  senderWindowId?: number;
}

/** 运行时配置标志。C# 端 `RuntimeGenerator` 会注入 `window._wails`，未注入时使用默认值。 */
export interface WailsRuntimeFlags {
  /** 平台：windows / linux / android / darwin。 */
  platform: string;
  /** 是否调试模式。 */
  isDebug: boolean;
  /** 是否 Server 模式（使用 WebSocket 而非原生/HTTP）。 */
  isServerMode: boolean;
  /** 资源服务器 URL（Server 模式下 HTTP 通道的基址）。 */
  assetServerUrl?: string;
  /** WebSocket URL（Server 模式通道）。 */
  webSocketUrl?: string;
}

/** 读取 `window._wails` 运行时标志（若存在）。 */
export function readRuntimeFlags(): Partial<WailsRuntimeFlags> | undefined {
  const w = globalThis as unknown as { _wails?: Partial<WailsRuntimeFlags> };
  return w._wails;
}
