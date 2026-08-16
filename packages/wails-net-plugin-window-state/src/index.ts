/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";

export const windowState = {
  /** 保存所有窗口的当前状态（位置、大小、最大化/最小化）到状态文件。 */
  save: () => call<void>("windowstate.save", []),
  /** 从状态文件恢复所有窗口的状态。 */
  restore: () => call<void>("windowstate.restore", []),
  /** 清除保存的窗口状态文件。 */
  clear: () => call<void>("windowstate.clear", []),
};