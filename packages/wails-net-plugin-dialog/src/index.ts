/**
 * @wails-net/plugin-dialog — dialog 插件前端封装。
 * 命令前缀 `dialog.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * dialog 插件前端 API（命令前缀 `dialog.*`）。
 * 对应后端 Wails.Net.Plugins.Dialog.DialogPlugin 注册的命令。
 * 提供消息对话框与文件选择 / 保存 / 多选对话框能力。
 */
export const dialog = {
  /** 显示信息提示对话框，返回点击按钮的索引（第一个按钮索引为 0）。 */
  message: (title: string, message: string) =>
    call<number>("dialog.message", [title, message]),
  /** 显示警告对话框，返回点击按钮的索引。 */
  warning: (title: string, message: string) =>
    call<number>("dialog.warning", [title, message]),
  /** 显示错误对话框，返回点击按钮的索引。 */
  error: (title: string, message: string) =>
    call<number>("dialog.error", [title, message]),
  /** 显示询问对话框（Yes/No），返回点击按钮的索引。 */
  question: (title: string, message: string) =>
    call<number>("dialog.question", [title, message]),
  /** 打开单文件选择对话框，返回选中文件路径；用户取消返回 null。filters 如 `["*.png","*.jpg"]`。 */
  openFile: (title?: string | null, directory?: string | null, filters?: string[] | null) =>
    call<string | null>("dialog.openFile", [title ?? null, directory ?? null, filters ?? null]),
  /** 打开文件保存对话框，返回保存路径；用户取消返回 null。 */
  saveFile: (
    title?: string | null,
    directory?: string | null,
    filename?: string | null,
    filters?: string[] | null,
  ) => call<string | null>("dialog.saveFile", [title ?? null, directory ?? null, filename ?? null, filters ?? null]),
  /** 打开多文件选择对话框，返回选中文件路径数组；用户取消返回 null。 */
  openMultipleFiles: (
    title?: string | null,
    directory?: string | null,
    filters?: string[] | null,
  ) => call<string[] | null>("dialog.openMultipleFiles", [title ?? null, directory ?? null, filters ?? null]),
};