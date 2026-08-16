/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";

export const store = {
  /** 读取指定键的值；键不存在时返回 null。 */
  get: (key: string) => call<string | null>("store.get", [key]),
  /** 写入指定键的值。 */
  set: (key: string, value: string) => call<void>("store.set", [key, value]),
  /** 删除指定键，返回是否删除成功。 */
  delete: (key: string) => call<boolean>("store.delete", [key]),
  /** 检查指定键是否存在。 */
  has: (key: string) => call<boolean>("store.has", [key]),
  /** 获取当前命名空间下的所有键。 */
  keys: () => call<string[]>("store.keys", []),
  /** 清空当前命名空间下的所有键。 */
  clear: () => call<void>("store.clear", []),
  /** 监听指定键的变更，键变化时通过 "store:changed" 事件通知前端。重复调用会替换前一次订阅。 */
  watch: (key: string) => call<void>("store.watch", [key]),
  /** 取消对指定键的监听（幂等，无持有句柄时不抛错）。 */
  unwatch: (key: string) => call<void>("store.unwatch", [key]),
};