/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";

export const websocket = {
  /** 建立连接，返回连接 ID。 */
  connect: (url: string) => call<string>("websocket.connect", [url]),
  send: (connectionId: string, message: string) =>
    call<boolean>("websocket.send", [connectionId, message]),
  sendBinary: (connectionId: string, base64Data: string) =>
    call<boolean>("websocket.sendBinary", [connectionId, base64Data]),
  close: (connectionId: string) => call<boolean>("websocket.close", [connectionId]),
  getState: (connectionId: string) => call<string>("websocket.getState", [connectionId]),
};
