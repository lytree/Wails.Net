/**
 * @wails-net/plugin-app-info — app 插件前端封装。
 * 命令前缀 `app.*`，后端经 L2 抽象层 `defineCommand` 转发（强类型化）。
 * @platform windows,linux,macos  桌面通用插件。
 */
import { defineCommand } from "@wails-net/runtime";

/** 示例：app.ping（无参数，返回字符串）。 */
export const ping = defineCommand<[], string>("app.ping", "none");