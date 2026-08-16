/**
 * @wails-net/plugin-clipboard — clipboard 插件前端封装。
 * 命令前缀 `clipboard.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * clipboard 插件前端 API（命令前缀 `clipboard.*`）。
 * 对应后端 Wails.Net.Plugins.Clipboard.ClipboardPlugin 注册的命令。
 * 提供剪贴板文本 / HTML / 图片的读写与清空能力。
 */
export const clipboard = {
  /** 读取剪贴板文本；为空时返回空字符串。 */
  getText: () => call<string>("clipboard.getText", []),
  /** 写入剪贴板文本。 */
  setText: (text: string) => call<void>("clipboard.setText", [text]),
  /** 读取剪贴板 HTML；为空时返回空字符串。 */
  getHTML: () => call<string>("clipboard.getHTML", []),
  /** 写入剪贴板 HTML，并附带纯文本回退（fallbackText）。 */
  setHTML: (html: string, fallbackText: string) =>
    call<void>("clipboard.setHTML", [html, fallbackText]),
  /** 读取剪贴板图片，返回 Base64 字符串；无图片时返回 null。 */
  getImage: () => call<string | null>("clipboard.getImage", []),
  /** 写入剪贴板图片（imageData 为 Base64 字符串，对应后端 byte[]）。 */
  setImage: (imageData: string) => call<void>("clipboard.setImage", [imageData]),
  /** 清空剪贴板。 */
  clear: () => call<void>("clipboard.clear", []),
};