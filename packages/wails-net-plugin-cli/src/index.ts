/**
 * @wails-net/plugin-cli — cli 插件前端封装。
 * 命令前缀 `cli.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";
import type { CliMatches } from "@wails-net/runtime";

/**
 * cli 插件前端 API（命令前缀 `cli.*`）。
 * 对应后端 Wails.Net.Plugins.Cli.CliPlugin 注册的命令。
 */
export const cli = {
  /** 获取命令行参数解析结果（对应 `cli.getMatches`）。 */
  getMatches: () => call<CliMatches>("cli.getMatches", []),
};