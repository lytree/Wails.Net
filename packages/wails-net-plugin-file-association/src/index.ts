/**
 * @wails-net/plugin-file-association — file-association 插件前端封装。
 * 命令前缀 `fileassociation.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * file-association 插件前端 API（命令前缀 `fileassociation.*`）。
 * 对应后端 Wails.Net.Plugins.Fileassociation.FileAssociationPlugin 注册的命令。
 */
export const fileAssociation = {
  /** 注册指定的文件扩展名到操作系统（对应 `fileassociation.register`）。 */
  register: (extension: string) => call<void>("fileassociation.register", [extension]),
  /** 注销指定的文件扩展名（对应 `fileassociation.unregister`）。 */
  unregister: (extension: string) => call<void>("fileassociation.unregister", [extension]),
  /** 获取当前已注册的文件扩展名列表（对应 `fileassociation.getRegistered`）。 */
  getRegistered: () => call<string[]>("fileassociation.getRegistered", []),
};