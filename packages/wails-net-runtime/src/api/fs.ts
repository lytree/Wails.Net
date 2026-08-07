/**
 * 文件系统 / 文件监听 / 路径 / 文件关联 命令封装。
 * 二进制以 Base64 字符串表示（readBinary / writeBinary / readBinaryFile / writeBinaryFile）。
 */
import { call } from "../core/runtime.js";
import type { FileStat } from "./common.js";

/** 文件系统（命令前缀 `fs.*`）。 */
export const fs = {
  read: (path: string) => call<string>("fs.read", [path]),
  write: (path: string, content: string) => call<void>("fs.write", [path, content]),
  readTextFile: (path: string) => call<string>("fs.readTextFile", [path]),
  writeTextFile: (path: string, content: string) => call<void>("fs.writeTextFile", [path, content]),
  readBinary: (path: string) => call<string>("fs.readBinary", [path]),
  writeBinary: (path: string, base64Data: string) => call<void>("fs.writeBinary", [path, base64Data]),
  readBinaryFile: (path: string) => call<string>("fs.readBinaryFile", [path]),
  writeBinaryFile: (path: string, base64Data: string) => call<void>("fs.writeBinaryFile", [path, base64Data]),
  readAsync: (path: string) => call<string>("fs.readAsync", [path]),
  writeAsync: (path: string, content: string) => call<void>("fs.writeAsync", [path, content]),
  exists: (path: string) => call<boolean>("fs.exists", [path]),
  existsDir: (path: string) => call<boolean>("fs.existsDir", [path]),
  delete: (path: string) => call<void>("fs.delete", [path]),
  remove: (path: string) => call<void>("fs.remove", [path]),
  copy: (src: string, dst: string) => call<void>("fs.copy", [src, dst]),
  rename: (oldPath: string, newPath: string) => call<void>("fs.rename", [oldPath, newPath]),
  stat: (path: string) => call<FileStat>("fs.stat", [path]),
  // 后端 fs.mkdir 仅接受单个 path 参数（不支持 recursive）。
  mkdir: (path: string) => call<void>("fs.mkdir", [path]),
  rmdir: (path: string, recursive?: boolean) => call<void>("fs.rmdir", [path, recursive]),
  readDir: (path: string) => call<string[]>("fs.readDir", [path]),
  readDirRecursive: (path: string) => call<string[]>("fs.readDirRecursive", [path]),
};

/**
 * 文件监听（命令前缀 `fswatch.*`）。
 * 监听 ID 为整数（由后端分配），所有按 ID 操作的方法均接收 number。
 */
export const fswatch = {
  // extensions：要监听的扩展名数组，序列化为 JSON 字符串传给后端（extensionsJson 参数）。
  watch: (path: string, recursive?: boolean, extensions?: string[]) =>
    call<number>("fswatch.watch", [path, recursive, JSON.stringify(extensions ?? [])]),
  unwatch: (id: number) => call<void>("fswatch.unwatch", [id]),
  unwatchAll: () => call<void>("fswatch.unwatchAll", []),
  listWatches: () => call<number[]>("fswatch.listWatches", []),
  isWatching: (id: number) => call<boolean>("fswatch.isWatching", [id]),
};

/** 标准路径（命令前缀 `path.*`）。 */
export const path = {
  appDataDir: () => call<string>("path.appDataDir", []),
  appConfigDir: () => call<string>("path.appConfigDir", []),
  appCacheDir: () => call<string>("path.appCacheDir", []),
  appLogDir: () => call<string>("path.appLogDir", []),
  configDir: () => call<string>("path.configDir", []),
  dataDir: () => call<string>("path.dataDir", []),
  documentDir: () => call<string>("path.documentDir", []),
  downloadDir: () => call<string>("path.downloadDir", []),
  homeDir: () => call<string>("path.homeDir", []),
  runtimeDir: () => call<string>("path.runtimeDir", []),
  tempDir: () => call<string>("path.tempDir", []),
};

/** 文件关联（命令前缀 `fileassociation.*`）。 */
export const fileassociation = {
  // 后端 FileAssociationPlugin 仅接收扩展名字符串，无 handlerName 参数。
  register: (extension: string) => call<void>("fileassociation.register", [extension]),
  unregister: (extension: string) => call<void>("fileassociation.unregister", [extension]),
  getRegistered: () => call<string[]>("fileassociation.getRegistered", []),
};
