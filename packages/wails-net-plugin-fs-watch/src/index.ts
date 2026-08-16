/**
 * @wails-net/plugin-fs-watch — fs-watch 插件前端封装。
 * 命令前缀 `fswatch.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * fs-watch 插件前端 API（命令前缀 `fswatch.*`）。
 * 对应后端 Wails.Net.Plugins.FsWatch.FsWatchPlugin 注册的命令。
 * 文件变化通过 `fswatch:changed:{id}` 事件推送到前端。
 */
export const fsWatch = {
  /**
   * 开始监听指定路径的文件系统变化（对应 `fswatch.watch`）。
   * @param path 要监听的目录路径。
   * @param recursive 是否递归监听子目录。
   * @param extensionsJson 文件扩展名过滤的 JSON 数组字符串（如 `[".txt",".json"]`），为 null 或空表示监听所有文件。
   * @returns 监听器 ID；路径无效时返回 0。
   */
  watch: (path: string, recursive: boolean, extensionsJson?: string | null) =>
    call<number>("fswatch.watch", [path, recursive, extensionsJson ?? null]),
  /** 停止指定 ID 的监听器（对应 `fswatch.unwatch`）。 */
  unwatch: (id: number) => call<void>("fswatch.unwatch", [id]),
  /** 停止所有监听器（对应 `fswatch.unwatchAll`）。 */
  unwatchAll: () => call<void>("fswatch.unwatchAll", []),
  /** 获取所有活动监听器的 ID 数组（对应 `fswatch.listWatches`）。 */
  listWatches: () => call<number[]>("fswatch.listWatches", []),
  /** 检查指定 ID 的监听器是否正在监听（对应 `fswatch.isWatching`）。 */
  isWatching: (id: number) => call<boolean>("fswatch.isWatching", [id]),
};