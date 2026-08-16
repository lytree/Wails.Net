/**
 * @wails-net/plugin-file-system — file-system 插件前端封装。
 * 命令前缀 `fs.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * 文件元数据，对应后端 Wails.Net.Plugins.FileSystem.FileStat。
 * 注：与 @wails-net/runtime 中 FileStat 字段名不同，此处定义本地类型。
 */
export interface FileStat {
  /** 完整路径。 */
  path: string;
  /** 文件大小（字节）。 */
  size: number;
  /** 是否为目录。 */
  isDirectory: boolean;
  /** 最后修改时间（ISO-8601 UTC 字符串）。 */
  lastModified: string;
  /** 创建时间（ISO-8601 UTC 字符串）。 */
  created: string;
  /** 是否只读。 */
  isReadOnly: boolean;
}

/**
 * file-system 插件前端 API（命令前缀 `fs.*`）。
 * 对应后端 Wails.Net.Plugins.FileSystem.FileSystemPlugin 注册的命令。
 * 提供文件读写、目录操作、文件元数据查询等能力，内置路径穿越防护与沙箱限制。
 *
 * 注意：后端同时注册了短名（如 fs.read）和前端 API 名（如 fs.readTextFile），
 * 此处同时暴露两者供调用方按需选择。
 */
export const fileSystem = {
  // ===== 短名命令（向后兼容） =====
  /** 读取文本文件全部内容。 */
  read: (path: string) => call<string>("fs.read", [path]),
  /** 写入文本文件（覆盖模式）。 */
  write: (path: string, content: string) => call<void>("fs.write", [path, content]),
  /** 检查文件是否存在。 */
  exists: (path: string) => call<boolean>("fs.exists", [path]),
  /** 读取二进制文件，返回 Base64 字符串。 */
  readBinary: (path: string) => call<string>("fs.readBinary", [path]),
  /** 写入二进制文件（data 为 Base64 字符串，对应后端 byte[]）。 */
  writeBinary: (path: string, data: string) => call<void>("fs.writeBinary", [path, data]),

  // ===== 前端 API 名命令（与 @wails-net/runtime/src/api/fs.ts 一致） =====
  /** 读取文本文件全部内容（对应 fs.readTextFile）。 */
  readTextFile: (path: string) => call<string>("fs.readTextFile", [path]),
  /** 写入文本文件（覆盖模式，对应 fs.writeTextFile）。 */
  writeTextFile: (path: string, content: string) => call<void>("fs.writeTextFile", [path, content]),
  /** 读取二进制文件，返回 Base64 字符串（对应 fs.readBinaryFile）。 */
  readBinaryFile: (path: string) => call<string>("fs.readBinaryFile", [path]),
  /** 写入二进制文件（data 为 Base64 字符串，对应 fs.writeBinaryFile）。 */
  writeBinaryFile: (path: string, data: string) => call<void>("fs.writeBinaryFile", [path, data]),
  /** 删除文件（对应 fs.delete）。 */
  delete: (path: string) => call<void>("fs.delete", [path]),
  /** 删除文件（对应 fs.remove）。 */
  remove: (path: string) => call<void>("fs.remove", [path]),

  // ===== 其他文件操作 =====
  /** 复制文件（覆盖目标）。 */
  copy: (src: string, dest: string) => call<void>("fs.copy", [src, dest]),
  /** 重命名或移动文件。 */
  rename: (src: string, dest: string) => call<void>("fs.rename", [src, dest]),
  /** 获取文件或目录元数据。 */
  stat: (path: string) => call<FileStat>("fs.stat", [path]),

  // ===== 目录操作 =====
  /** 创建目录（含父目录）。 */
  mkdir: (path: string) => call<void>("fs.mkdir", [path]),
  /** 删除目录。recursive 为 true 时递归删除子目录和文件。 */
  rmdir: (path: string, recursive: boolean) => call<void>("fs.rmdir", [path, recursive]),
  /** 检查目录是否存在。 */
  existsDir: (path: string) => call<boolean>("fs.existsDir", [path]),
  /** 列出目录下的所有文件系统条目（文件/目录名数组）。 */
  readDir: (path: string) => call<string[]>("fs.readDir", [path]),
  /** 递归列出目录下的所有文件系统条目。 */
  readDirRecursive: (path: string) => call<string[]>("fs.readDirRecursive", [path]),
};