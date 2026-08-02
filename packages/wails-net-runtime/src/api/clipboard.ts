/**
 * 剪贴板命令封装（命令前缀 `clipboard.*`）。
 * 注意：`getImage` / `setImage` 的二进制以 Base64 字符串表示。
 */
import { call } from "../core/runtime.js";

/** 剪贴板（命令前缀 `clipboard.*`）。 */
export const clipboard = {
  getText: () => call<string>("clipboard.getText", []),
  setText: (text: string) => call<void>("clipboard.setText", [text]),
  getHTML: () => call<string>("clipboard.getHTML", []),
  setHTML: (html: string, fallbackText?: string) => call<void>("clipboard.setHTML", [html, fallbackText]),
  /** 获取图像（Base64，无则返回 null）。 */
  getImage: () => call<string | null>("clipboard.getImage", []),
  /** 设置图像（Base64）。 */
  setImage: (base64Data: string) => call<void>("clipboard.setImage", [base64Data]),
  clear: () => call<void>("clipboard.clear", []),
};
