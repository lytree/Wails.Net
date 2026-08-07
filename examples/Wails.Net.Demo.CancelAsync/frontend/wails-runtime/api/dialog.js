/**
 * 对话框命令封装（命令前缀 `dialog.*`）。
 * `message` / `warning` / `error` / `question` 返回被点击按钮的索引（int）。
 */
import { call } from "../core/runtime.js";
/** 对话框（命令前缀 `dialog.*`）。 */
export const dialog = {
    message: (title, message) => call("dialog.message", [title, message]),
    warning: (title, message) => call("dialog.warning", [title, message]),
    error: (title, message) => call("dialog.error", [title, message]),
    question: (title, message) => call("dialog.question", [title, message]),
    openFile: (options) => call("dialog.openFile", [options?.title ?? null, options?.directory ?? null, options?.filters ?? null]),
    saveFile: (options) => call("dialog.saveFile", [
        options?.title ?? null,
        options?.directory ?? null,
        options?.filename ?? null,
        options?.filters ?? null,
    ]),
    openMultipleFiles: (options) => call("dialog.openMultipleFiles", [
        options?.title ?? null,
        options?.directory ?? null,
        options?.filters ?? null,
    ]),
};
//# sourceMappingURL=dialog.js.map