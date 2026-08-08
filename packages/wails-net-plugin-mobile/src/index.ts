/**
 * @wails-net/plugin-mobile — 移动端插件聚合封装（M3 双包模型）。
 * 条码扫描/生物识别/相机/定位/触感/NFC/权限/设备（AndroidRuntimePlugin）。
 */
/**
 * 移动端插件命令封装（Android / iOS 通用）：
 * 条码扫描、生物识别、相机、定位、触感、NFC、权限、设备信息。
 *
 * 说明：`camera.capture` / `nfc.read` / `barcode-scanner.scan` 等命令的取消由后端
 * `ICommandContext.CancellationToken` 提供，**不接受前端传参**（见 AGENTS.md §3.4.6）。
 */
import { call } from "@wails-net/runtime";
import type {
  CameraCaptureResult,
  DeviceInfo,
  GeoPosition,
  GeoWatchHandle,
  HapticsNotificationType,
  PermissionStatus,
} from "@wails-net/runtime";

/**
 * 硬件能力可用性。
 * - `available` —— 可用
 * - `unavailable` —— 硬件存在但当前不可用
 * - `none` —— 无硬件支持
 */
export type Availability = "available" | "unavailable" | "none";

/** 条码扫描（命令前缀 `barcode-scanner.*`）。 */
export const barcodeScanner = {
  /** 启动扫描，返回扫描到的文本内容。 */
  scan: () => call<string>("barcode-scanner.scan", []),
  cancel: () => call<void>("barcode-scanner.cancel", []),
};

/** 生物识别（命令前缀 `biometric.*`）。 */
export const biometric = {
  checkAvailability: () => call<Availability>("biometric.checkAvailability", []),
  /** @param reason 展示给用户的认证理由文本。 */
  authenticate: (reason: string) => call<boolean>("biometric.authenticate", [{ reason }]),
};

/** 相机（命令前缀 `camera.*`）。 */
export const camera = {
  checkAvailability: () => call<Availability>("camera.checkAvailability", []),
  capture: () => call<CameraCaptureResult>("camera.capture", []),
  cancel: () => call<void>("camera.cancel", []),
};

/** `geolocation.*` 的定位参数。 */
export interface GeolocationOptions {
  /** 是否启用高精度定位。 */
  enableHighAccuracy?: boolean;
  /** 超时毫秒数。 */
  timeout?: number;
  /** 可接受的缓存位置最大存活毫秒数。 */
  maximumAge?: number;
}

/** 地理定位（命令前缀 `geolocation.*`）。 */
export const geolocation = {
  checkAvailability: () => call<Availability>("geolocation.checkAvailability", []),
  getCurrentPosition: (options?: GeolocationOptions) =>
    call<GeoPosition | null>("geolocation.getCurrentPosition", [options ?? null]),
  watchPosition: (options?: GeolocationOptions) =>
    call<GeoWatchHandle>("geolocation.watchPosition", [options ?? null]),
  /** @param handle `watchPosition` 返回的句柄，或直接传数字 `watchId`。 */
  clearWatch: (handle: GeoWatchHandle | number) =>
    call<void>("geolocation.clearWatch", [
      typeof handle === "number" ? { watchId: handle } : { watchId: handle.watchId },
    ]),
};

/** 触感反馈（命令前缀 `haptics.*`）。 */
export const haptics = {
  /** @param duration 震动时长（毫秒）。 */
  vibrate: (duration: number) => call<void>("haptics.vibrate", [{ duration }]),
  notification: (type: HapticsNotificationType) => call<void>("haptics.notification", [{ type }]),
  cancel: () => call<void>("haptics.cancel", []),
};

/** NFC（命令前缀 `nfc.*`）。 */
export const nfc = {
  /** 读取 NFC 标签，返回文本内容。 */
  read: () => call<string>("nfc.read", []),
  /** @param data 写入的文本数据。 */
  write: (data: string) => call<void>("nfc.write", [{ data }]),
  cancel: () => call<void>("nfc.cancel", []),
};

/** 权限（命令前缀 `permissions.*`）。 */
export const permissions = {
  check: (permission: string) => call<PermissionStatus>("permissions.check", [permission]),
  request: (permission: string) => call<PermissionStatus>("permissions.request", [permission]),
};

/** 设备信息 / Toast（Android 运行时插件，命令前缀 `device.*` / `toast.*`）。 */
export const device = {
  /**
   * 获取设备信息。
   * 后端返回 JSON 字符串，此处解析为对象。
   */
  info: async (): Promise<DeviceInfo> => {
    const raw = await call<string>("device.info", []);
    try {
      return JSON.parse(raw) as DeviceInfo;
    } catch {
      return {};
    }
  },
  /** 获取设备信息的后端原始 JSON 字符串。 */
  infoRaw: () => call<string>("device.info", []),
  /** 弹出系统 Toast。 */
  showToast: (message: string) => call<void>("toast.show", [{ message }]),
};
