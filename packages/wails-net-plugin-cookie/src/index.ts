/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";

export const cookie = {
  /**
   * 读取全部 Cookie。
   * @returns JSON 字符串形式的 `{ name: value }` 字典。
   */
  get: (url = "") => call<string>("cookie.get", [url]),
  set: (name: string, value: string) => call<boolean>("cookie.set", [name, value]),
  delete: (name: string) => call<boolean>("cookie.delete", [name]),
  clear: () => call<boolean>("cookie.clear", []),
};
