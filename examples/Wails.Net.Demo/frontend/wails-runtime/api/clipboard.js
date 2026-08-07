/**
 * 剪贴板命令封装（命令前缀 `clipboard.*`）。
 * 注意：`getImage` / `setImage` 的二进制以 Base64 字符串表示。
 */
import { call } from "../core/runtime.js";
/** 剪贴板（命令前缀 `clipboard.*`）。 */
export const clipboard = {
    getText: () => call("clipboard.getText", []),
    setText: (text) => call("clipboard.setText", [text]),
    getHTML: () => call("clipboard.getHTML", []),
    setHTML: (html, fallbackText) => call("clipboard.setHTML", [html, fallbackText]),
    /** 获取图像（Base64，无则返回 null）。 */
    getImage: () => call("clipboard.getImage", []),
    /** 设置图像（Base64）。 */
    setImage: (base64Data) => call("clipboard.setImage", [base64Data]),
    clear: () => call("clipboard.clear", []),
};
//# sourceMappingURL=clipboard.js.map