/**
 * @wails-net/plugin-store — store 插件前端封装。
 * 命令前缀 `store.*`，后端经 L2 抽象层 `defineCommand` 转发（强类型化）。
 * @platform windows,linux,macos  桌面通用插件。
 */
import { defineCommand } from "@wails-net/runtime";

/** 示例：store.ping（无参数，返回字符串）。 */
export const ping = defineCommand<[], string>("store.ping", "none");