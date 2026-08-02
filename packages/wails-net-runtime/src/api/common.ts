/**
 * 公共选项 / 数据结构类型。
 *
 * 这些类型与 Wails.Net 后端插件命令的参数 / 返回值对应。字段命名采用 camelCase
 * （与后端 `JsonSerializerDefaults.Web` + camelCase 输出一致）。`byte[]` 在前端以
 * Base64 字符串表示，`Guid` 以字符串，`DateTime` 以 ISO-8601 字符串。
 */

/** 通知选项。 */
export interface NotificationOptions {
  /** 标题（必填）。 */
  title: string;
  /** 正文（必填）。 */
  body: string;
  /** 图标（Base64 data URL 或路径）。 */
  icon?: string;
  /** 通知 ID（用于取消 / 去重）。 */
  id?: string;
  /** 点击动作标识。 */
  action?: string;
  /** 其他扩展字段。 */
  [key: string]: unknown;
}

/** 打开文件对话框选项。 */
export interface OpenDialogOptions {
  /** 标题。 */
  title?: string;
  /** 默认目录。 */
  directory?: string;
  /** 文件类型过滤器，如 `["*.png", "*.jpg"]`。 */
  filters?: string[];
}

/** 保存文件对话框选项。 */
export interface SaveDialogOptions {
  /** 标题。 */
  title?: string;
  /** 默认目录。 */
  directory?: string;
  /** 默认文件名。 */
  filename?: string;
  /** 文件类型过滤器。 */
  filters?: string[];
}

/** 文件 / 目录状态信息。 */
export interface FileStat {
  /** 名称。 */
  name: string;
  /** 完整路径。 */
  path: string;
  /** 字节大小。 */
  size: number;
  /** 权限模式（数值）。 */
  mode: number;
  /** 最后修改时间（ISO-8601 字符串）。 */
  modTime: string;
  /** 是否为目录。 */
  isDir: boolean;
}

/** 相机采集结果。 */
export interface CameraCaptureResult {
  /** 是否成功。 */
  success: boolean;
  /** Base64 图像数据（成功时）。 */
  base64Data?: string;
  /** 错误信息（失败时）。 */
  error?: string;
}

/** 地理坐标位置。 */
export interface GeoPosition {
  /** 纬度。 */
  latitude: number;
  /** 经度。 */
  longitude: number;
  /** 精度（米）。 */
  accuracy?: number;
  /** 海拔（米）。 */
  altitude?: number;
  /** 海拔精度（米）。 */
  altitudeAccuracy?: number;
  /** 航向（度）。 */
  heading?: number;
  /** 速度（米/秒）。 */
  speed?: number;
  /** 时间戳（ISO-8601）。 */
  timestamp?: string;
}

/** 定位监听句柄（用于 clearWatch）。 */
export interface GeoWatchHandle {
  /** 监听 ID。 */
  id: string;
}

/** 权限状态。 */
export type PermissionState = "granted" | "denied" | "prompt" | "restricted";

/** 权限描述（permissions.check 返回值）。 */
export interface PermissionStatus {
  /** 权限名。 */
  permission: string;
  /** 状态。 */
  state: PermissionState;
}

/** HTTP 响应（http.fetch 等返回）。 */
export interface HttpResponse<T = unknown> {
  /** 状态码。 */
  status: number;
  /** 状态文本。 */
  statusText: string;
  /** 响应头。 */
  headers: Record<string, string>;
  /** 响应体（根据解析方式可能是文本 / JSON / base64）。 */
  body: T;
  /** 是否为 ok（2xx）。 */
  ok: boolean;
}

/** 上传 / 下载进度回调参数。 */
export interface TransferProgress {
  /** 已传输字节。 */
  transferred: number;
  /** 总字节（未知时为 -1）。 */
  total: number;
  /** 进度比例 0..1。 */
  percent: number;
}

/** 屏幕信息。 */
export interface ScreenInfo {
  /** 屏幕 ID。 */
  id: string;
  /** 是否为primary。 */
  isPrimary: boolean;
  /** 逻辑宽。 */
  width: number;
  /** 逻辑高。 */
  height: number;
  /** 缩放因子（DPI）。 */
  scaleFactor: number;
  /** 工作区（不含任务栏）。 */
  workArea?: { x: number; y: number; width: number; height: number };
}

/** 窗口位置（左上角坐标）。 */
export interface Point {
  x: number;
  y: number;
}

/** 窗口尺寸。 */
export interface Size {
  width: number;
  height: number;
}

/** 窗口信息（windows.getAll 等返回）。 */
export interface WindowInfo {
  /** 窗口 ID。 */
  id: number;
  /** 窗口名称。 */
  name: string;
}

/** 菜单角色（menu.addRoleItem）。 */
export type MenuRole =
  | "about"
  | "services"
  | "hide"
  | "hideOthers"
  | "unhide"
  | "quit"
  | "undo"
  | "redo"
  | "cut"
  | "copy"
  | "paste"
  | "selectAll"
  | "minimize"
  | "zoom"
  | "close"
  | "bringAllToFront";

/** 菜单项（menu.setApplicationMenu / setContextMenu）。 */
export interface MenuItem {
  /** 标签。 */
  label?: string;
  /** 角色（使用系统标准项）。 */
  role?: MenuRole;
  /** 点击命令名（后端注册的命令）。 */
  command?: string;
  /** 是否禁用。 */
  disabled?: boolean;
  /** 是否勾选。 */
  checked?: boolean;
  /** 快捷键（如 "Ctrl+C"）。 */
  accelerator?: string;
  /** 子菜单。 */
  submenu?: MenuItem[];
  /** 分隔符。 */
  type?: "separator" | "checkbox" | "radio" | "normal";
  /** 图标（Base64）。 */
  icon?: string;
  /** 扩展字段。 */
  [key: string]: unknown;
}

/** 菜单定义。 */
export type Menu = MenuItem[];
