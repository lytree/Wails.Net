/**
 * 对话框命令封装（命令前缀 `dialog.*`）。
 * `message` / `warning` / `error` / `question` 返回被点击按钮的索引（int）。
 */
import { call } from "../core/runtime.js";
import type { OpenDialogOptions, SaveDialogOptions } from "./common.js";

/** 对话框（命令前缀 `dialog.*`）。 */
export const dialog = {
  message: (title: string, message: string) => call<number>("dialog.message", [title, message]),
  warning: (title: string, message: string) => call<number>("dialog.warning", [title, message]),
  error: (title: string, message: string) => call<number>("dialog.error", [title, message]),
  question: (title: string, message: string) => call<number>("dialog.question", [title, message]),
  openFile: (options?: OpenDialogOptions) =>
    call<string | null>("dialog.openFile", [options?.title ?? null, options?.directory ?? null, options?.filters ?? null]),
  saveFile: (options?: SaveDialogOptions) =>
    call<string | null>("dialog.saveFile", [
      options?.title ?? null,
      options?.directory ?? null,
      options?.filename ?? null,
      options?.filters ?? null,
    ]),
  openMultipleFiles: (options?: OpenDialogOptions) =>
    call<string[] | null>("dialog.openMultipleFiles", [
      options?.title ?? null,
      options?.directory ?? null,
      options?.filters ?? null,
    ]),
};
