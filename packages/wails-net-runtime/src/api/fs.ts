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
  mkdir: (path: string, recursive?: boolean) => call<void>("fs.mkdir", [path, recursive]),
  rmdir: (path: string, recursive?: boolean) => call<void>("fs.rmdir", [path, recursive]),
  readDir: (path: string) => call<string[]>("fs.readDir", [path]),
  readDirRecursive: (path: string) => call<string[]>("fs.readDirRecursive", [path]),
};

/** 文件监听（命令前缀 `fswatch.*`）。 */
export const fswatch = {
  watch: (path: string, recursive?: boolean, extensions?: string[]) =>
    call<string>("fswatch.watch", [path, recursive, extensions]),
  unwatch: (id: string) => call<void>("fswatch.unwatch", [id]),
  unwatchAll: () => call<void>("fswatch.unwatchAll", []),
  listWatches: () => call<string[]>("fswatch.listWatches", []),
  isWatching: (id: string) => call<boolean>("fswatch.isWatching", [id]),
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
  register: (extension: string, handlerName?: string) =>
    call<void>("fileassociation.register", [extension, handlerName]),
  unregister: (extension: string) => call<void>("fileassociation.unregister", [extension]),
  getRegistered: () => call<string[]>("fileassociation.getRegistered", []),
};
