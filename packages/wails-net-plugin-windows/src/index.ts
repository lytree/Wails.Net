/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";
import type { WindowInfo } from "@wails-net/runtime";

/** windows.getByName 命令参数。 */
export interface WindowsByNameOptions {
  /** 窗口名称。 */
  name: string;
}

/** windows.getById 命令参数。 */
export interface WindowsByIdOptions {
  /** 窗口 ID。 */
  id: number;
}

/** windows.emit 命令参数。 */
export interface WindowsEmitOptions {
  /** 事件名称（实际广播为 "wails:window:{name}"）。 */
  name: string;
  /** 事件数据，可为空。 */
  data?: unknown;
  /** 目标窗口 ID，省略时广播到所有窗口。 */
  targetWindowId?: number;
}

export const windows = {
  /** 获取当前窗口信息。 */
  getCurrent: () => call<WindowInfo | null>("windows.getCurrent", []),
  /** 获取所有窗口信息。 */
  getAll: () => call<WindowInfo[]>("windows.getAll", []),
  /** 按名称获取窗口信息（忽略大小写）。 */
  getByName: (opts: WindowsByNameOptions) =>
    call<WindowInfo | null>("windows.getByName", [opts]),
  /** 按 ID 获取窗口信息。 */
  getById: (opts: WindowsByIdOptions) =>
    call<WindowInfo | null>("windows.getById", [opts]),
  /** 广播窗口事件（name 前缀为 "wails:window:"，可指定目标窗口）。 */
  emit: (opts: WindowsEmitOptions) => call<void>("windows.emit", [opts]),
};