/**
 * 文件系统 / 文件监听 / 路径 / 文件关联 命令封装。
 * 二进制以 Base64 字符串表示（readBinary / writeBinary / readBinaryFile / writeBinaryFile）。
 */
import { call } from "../core/runtime.js";
/** 文件系统（命令前缀 `fs.*`）。 */
export const fs = {
    read: (path) => call("fs.read", [path]),
    write: (path, content) => call("fs.write", [path, content]),
    readTextFile: (path) => call("fs.readTextFile", [path]),
    writeTextFile: (path, content) => call("fs.writeTextFile", [path, content]),
    readBinary: (path) => call("fs.readBinary", [path]),
    writeBinary: (path, base64Data) => call("fs.writeBinary", [path, base64Data]),
    readBinaryFile: (path) => call("fs.readBinaryFile", [path]),
    writeBinaryFile: (path, base64Data) => call("fs.writeBinaryFile", [path, base64Data]),
    readAsync: (path) => call("fs.readAsync", [path]),
    writeAsync: (path, content) => call("fs.writeAsync", [path, content]),
    exists: (path) => call("fs.exists", [path]),
    existsDir: (path) => call("fs.existsDir", [path]),
    delete: (path) => call("fs.delete", [path]),
    remove: (path) => call("fs.remove", [path]),
    copy: (src, dst) => call("fs.copy", [src, dst]),
    rename: (oldPath, newPath) => call("fs.rename", [oldPath, newPath]),
    stat: (path) => call("fs.stat", [path]),
    // 后端 fs.mkdir 仅接受单个 path 参数（不支持 recursive）。
    mkdir: (path) => call("fs.mkdir", [path]),
    rmdir: (path, recursive) => call("fs.rmdir", [path, recursive]),
    readDir: (path) => call("fs.readDir", [path]),
    readDirRecursive: (path) => call("fs.readDirRecursive", [path]),
};
/**
 * 文件监听（命令前缀 `fswatch.*`）。
 * 监听 ID 为整数（由后端分配），所有按 ID 操作的方法均接收 number。
 */
export const fswatch = {
    // extensions：要监听的扩展名数组，序列化为 JSON 字符串传给后端（extensionsJson 参数）。
    watch: (path, recursive, extensions) => call("fswatch.watch", [path, recursive, JSON.stringify(extensions ?? [])]),
    unwatch: (id) => call("fswatch.unwatch", [id]),
    unwatchAll: () => call("fswatch.unwatchAll", []),
    listWatches: () => call("fswatch.listWatches", []),
    isWatching: (id) => call("fswatch.isWatching", [id]),
};
/** 标准路径（命令前缀 `path.*`）。 */
export const path = {
    appDataDir: () => call("path.appDataDir", []),
    appConfigDir: () => call("path.appConfigDir", []),
    appCacheDir: () => call("path.appCacheDir", []),
    appLogDir: () => call("path.appLogDir", []),
    configDir: () => call("path.configDir", []),
    dataDir: () => call("path.dataDir", []),
    documentDir: () => call("path.documentDir", []),
    downloadDir: () => call("path.downloadDir", []),
    homeDir: () => call("path.homeDir", []),
    runtimeDir: () => call("path.runtimeDir", []),
    tempDir: () => call("path.tempDir", []),
};
/** 文件关联（命令前缀 `fileassociation.*`）。 */
export const fileassociation = {
    // 后端 FileAssociationPlugin 仅接收扩展名字符串，无 handlerName 参数。
    register: (extension) => call("fileassociation.register", [extension]),
    unregister: (extension) => call("fileassociation.unregister", [extension]),
    getRegistered: () => call("fileassociation.getRegistered", []),
};
//# sourceMappingURL=fs.js.map