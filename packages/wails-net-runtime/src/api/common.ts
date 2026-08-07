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

/** 相机采集结果。对应后端 `CameraCaptureResult`。 */
export interface CameraCaptureResult {
  /** 是否成功。 */
  success: boolean;
  /** Base64 JPEG 图像数据；失败时为空串。 */
  base64Data: string;
  /** 错误信息（失败时）。 */
  error?: string;
}

/** 地理坐标。对应后端 `GeolocationCoords`。 */
export interface GeoCoords {
  /** 纬度。 */
  latitude: number;
  /** 经度。 */
  longitude: number;
  /** 精度（米）。 */
  accuracy: number;
  /** 海拔（米）。 */
  altitude?: number;
  /** 海拔精度（米）。 */
  altitudeAccuracy?: number;
  /** 航向（度）。 */
  heading?: number;
  /** 速度（米/秒）。 */
  speed?: number;
}

/**
 * 地理位置。对应后端 `GeolocationPosition`。
 *
 * 注意坐标在嵌套的 `coords` 字段中（与 W3C Geolocation API 一致），
 * `timestamp` 为 Unix 毫秒时间戳。
 */
export interface GeoPosition {
  /** 坐标信息。 */
  coords: GeoCoords;
  /** Unix 毫秒时间戳。 */
  timestamp: number;
}

/** 定位监听句柄（用于 `clearWatch`）。对应后端 `WatchPositionResult`。 */
export interface GeoWatchHandle {
  /** 监听 ID。 */
  watchId: number;
}

/** 触感通知类型。对应后端 `NotificationType` 枚举（驼峰字符串）。 */
export type HapticsNotificationType = "success" | "warning" | "error";

/**
 * 设备信息（`device.info` 返回的 JSON 解析结果）。
 * 字段取决于平台，Android 提供 platform / manufacturer / brand / model / device / version / sdkInt。
 */
export interface DeviceInfo {
  /** 平台标识，如 `"android"`。 */
  platform?: string;
  /** 制造商。 */
  manufacturer?: string;
  /** 品牌。 */
  brand?: string;
  /** 型号。 */
  model?: string;
  /** 设备代号。 */
  device?: string;
  /** 系统版本号。 */
  version?: string;
  /** Android API Level。 */
  sdkInt?: number;
  /** 其他平台特有字段。 */
  [key: string]: unknown;
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

/**
 * HTTP 响应（`http.fetch` / `http.get` 等返回）。
 * 对应后端 `HttpPlugin.HttpResponseResult`。
 */
export interface HttpResponse {
  /** HTTP 状态码。 */
  statusCode: number;
  /** 是否为成功状态（2xx）。 */
  ok: boolean;
  /** 响应体文本。后端统一以字符串返回，JSON 需自行 `JSON.parse`。 */
  body: string;
  /** 响应内容类型。 */
  contentType: string;
  /** 响应头。 */
  headers: Record<string, string>;
}

/** `http.fetch` 的请求选项。对应后端 `HttpPlugin.HttpRequestOptions`。 */
export interface HttpRequestOptions {
  /** 请求 URL（必填）。 */
  url: string;
  /** HTTP 方法，默认 `GET`。 */
  method?: string;
  /** 请求体。 */
  body?: string;
  /** 请求内容类型。 */
  contentType?: string;
  /** 附加请求头。 */
  headers?: Record<string, string>;
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

/**
 * 屏幕信息（`screen.getAll` / `screen.getPrimary` 返回值）。
 * 对应后端 `Wails.Net.Application.Screens.Screen`，字段与其一一对应。
 */
export interface ScreenInfo {
  /** 屏幕 ID。 */
  id: string;
  /** 屏幕名称。 */
  name: string;
  /** 逻辑坐标 X。 */
  x: number;
  /** 逻辑坐标 Y。 */
  y: number;
  /** 逻辑宽。 */
  width: number;
  /** 逻辑高。 */
  height: number;
  /** 工作区（不含任务栏）逻辑坐标 X。 */
  workAreaX: number;
  /** 工作区逻辑坐标 Y。 */
  workAreaY: number;
  /** 工作区逻辑宽。 */
  workAreaWidth: number;
  /** 工作区逻辑高。 */
  workAreaHeight: number;
  /** 物理像素坐标 X。 */
  physicalX: number;
  /** 物理像素坐标 Y。 */
  physicalY: number;
  /** 物理像素宽。 */
  physicalWidth: number;
  /** 物理像素高。 */
  physicalHeight: number;
  /** 物理工作区坐标 X。 */
  physicalWorkAreaX: number;
  /** 物理工作区坐标 Y。 */
  physicalWorkAreaY: number;
  /** 物理工作区宽。 */
  physicalWorkAreaWidth: number;
  /** 物理工作区高。 */
  physicalWorkAreaHeight: number;
  /** 缩放因子（DPI）。 */
  scaleFactor: number;
  /** 是否为主屏。 */
  isPrimary: boolean;
  /** 旋转角度（度）。 */
  rotation: number;
  /** 缩略图（Base64）。 */
  thumbnail?: string;
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

/**
 * 菜单项预定义角色，对应后端 `Wails.Net.Application.Menus.MenuRole` 枚举。
 *
 * 后端命令层已注册 `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`，
 * 因此这里直接使用驼峰字符串即可（无需传枚举序号）。
 * macOS 专属角色在 Windows/Linux 上静默降级为 no-op。
 */
export type MenuRole =
  | "none"
  | "separator"
  | "copy"
  | "cut"
  | "paste"
  | "selectAll"
  | "undo"
  | "redo"
  | "minimize"
  | "maximize"
  | "fullscreen"
  | "closeWindow"
  | "zoom"
  | "about"
  | "quit"
  | "hide"
  | "hideOthers"
  | "showAll"
  | "services"
  | "bringAllToFront"
  | "toggleFullScreen";

/**
 * 菜单节点。对应后端 `Wails.Net.Application.Menus.Menu`。
 *
 * 后端的 `MenuItem` 继承自 `Menu`，因此菜单是**递归的单一节点结构**
 * （而非「菜单 = 菜单项数组」）：顶层 `Menu` 通过 `items` 挂载子项，
 * 子项自身又可通过 `isSubMenu` + `items` 继续嵌套。
 */
export interface Menu {
  /** 显示文本。 */
  label?: string;
  /** 图标位图（Base64 字符串，对应后端 `byte[]`）。 */
  bitmap?: string;
  /** 是否为子菜单容器。 */
  isSubMenu?: boolean;
  /** 是否为复选框项。 */
  isCheckbox?: boolean;
  /** 是否为单选项。 */
  isRadio?: boolean;
  /** 是否为分隔符。 */
  isSeparator?: boolean;
  /** 是否禁用。 */
  isDisabled?: boolean;
  /** 是否处于勾选状态。 */
  checked?: boolean;
  /** 快捷键（如 `"Ctrl+C"`）。 */
  accelerator?: string;
  /** 子项列表。 */
  items?: MenuItem[];
}

/**
 * 菜单项。对应后端 `Wails.Net.Application.Menus.MenuItem`（继承 `Menu`）。
 *
 * 注意：后端的 `Callback` / `CallbackWithContext` 是服务端委托，
 * 不参与 JSON 传输，故此处不暴露。前端响应点击请监听菜单事件。
 */
export interface MenuItem extends Menu {
  /** 菜单项 ID（后端 `uint`，用于 `menu.updateMenuItem` 等定位）。 */
  id?: number;
  /** 预定义角色。设置后平台将调用系统原生命令，忽略自定义回调。 */
  role?: MenuRole;
  /** "关于" 对话框元数据（仅 `role: "about"` 时有意义）。 */
  aboutMetadata?: AboutMetadata;
}

/** "关于" 对话框元数据。对应后端 `Wails.Net.Application.Menus.AboutMetadata`。 */
export interface AboutMetadata {
  /** 应用名称。 */
  name?: string;
  /** 完整版本号。 */
  version?: string;
  /** 简短版本号。 */
  shortVersion?: string;
  /** 作者。 */
  authors?: string;
  /** 版权信息。 */
  copyright?: string;
  /** 许可证。 */
  license?: string;
  /** 官网 URL。 */
  website?: string;
  /** 官网显示文本。 */
  websiteLabel?: string;
  /** 说明文本。 */
  comments?: string;
}

/** CLI 选项值（对应后端 `CliArgValue`）。 */
export interface CliArgValue {
  /** 是否存在（flag 类选项）。 */
  exists: boolean;
  /** 字符串值（单值选项）。 */
  value?: string;
  /** 多值列表（多值选项）。 */
  values?: string[];
}

/** CLI 解析结果（对应后端 `CliMatches`，`cli.getMatches` 返回）。 */
export interface CliMatches {
  /** 非选项参数数组（位置参数）。 */
  args: string[];
  /** 选项字典，按参数长名索引。 */
  options: Record<string, CliArgValue>;
  /** 匹配的子命令名；未匹配子命令时为 null。 */
  subcommand?: string;
}

/** Shell 命令执行结果（对应后端 `ShellPlugin.ShellResult`）。 */
export interface ShellResult {
  /** 退出码。 */
  exitCode: number;
  /** 标准输出。 */
  stdout: string;
  /** 标准错误。 */
  stderr: string;
}
