/**
 * @wails-net/plugin-persisted-scope — persisted-scope 插件前端封装。
 * 命令前缀 `scope.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * persisted-scope 插件前端 API（命令前缀 `scope.*`）。
 * 对应后端 Wails.Net.Plugins.PersistedScope.PersistedScopePlugin 注册的命令。
 * 允许在运行时动态添加/移除允许访问的文件系统路径，并将范围变更持久化到 JSON 文件。
 */
export const persistedScope = {
  /** 添加允许的路径并持久化，成功返回 true。scopeName 省略时使用默认作用域。 */
  addPath: (path: string, scopeName?: string | null) =>
    call<boolean>("scope.addPath", [path, scopeName ?? null]),
  /** 移除允许的路径并持久化，成功返回 true。scopeName 省略时使用默认作用域。 */
  removePath: (path: string, scopeName?: string | null) =>
    call<boolean>("scope.removePath", [path, scopeName ?? null]),
  /** 列出指定作用域允许的所有路径；作用域不存在时返回空数组。 */
  listPaths: (scopeName?: string | null) =>
    call<string[] | null>("scope.listPaths", [scopeName ?? null]),
  /** 清除指定作用域的所有路径并持久化。scopeName 省略时使用默认作用域。 */
  clear: (scopeName?: string | null) => call<void>("scope.clear", [scopeName ?? null]),
  /** 校验路径是否在允许范围内（指定 scopeName 时仅查该作用域，否则合并所有作用域）。 */
  isAllowed: (path: string, scopeName?: string | null) =>
    call<boolean>("scope.isAllowed", [path, scopeName ?? null]),
  /** 将指定作用域立即保存到磁盘。scopeName 省略时使用默认作用域。 */
  save: (scopeName?: string | null) => call<void>("scope.save", [scopeName ?? null]),
  /** 从磁盘加载指定作用域的持久化配置，成功返回 true。 */
  load: (scopeName?: string | null) => call<boolean>("scope.load", [scopeName ?? null]),
};