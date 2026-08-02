/**
 * 移动端插件命令封装（Android / iOS 通用）：
 * 条码扫描、生物识别、相机、定位、触感、NFC、权限、设备信息。
 */
import { call } from "../core/runtime.js";
import type { CameraCaptureResult, GeoPosition, GeoWatchHandle, PermissionStatus } from "./common.js";

/** 条码扫描（命令前缀 `barcode-scanner.*`）。 */
export const barcodeScanner = {
  scan: (options?: unknown) => call<string>("barcode-scanner.scan", [options]),
  cancel: () => call<void>("barcode-scanner.cancel", []),
};

/** 生物识别（命令前缀 `biometric.*`）。 */
export const biometric = {
  checkAvailability: () => call<boolean>("biometric.checkAvailability", []),
  authenticate: (reason?: string) => call<boolean>("biometric.authenticate", [reason]),
};

/** 相机（命令前缀 `camera.*`）。 */
export const camera = {
  checkAvailability: () => call<boolean>("camera.checkAvailability", []),
  capture: (options?: unknown) => call<CameraCaptureResult>("camera.capture", [options]),
  cancel: () => call<void>("camera.cancel", []),
};

/** 地理定位（命令前缀 `geolocation.*`）。 */
export const geolocation = {
  checkAvailability: () => call<boolean>("geolocation.checkAvailability", []),
  getCurrentPosition: (options?: unknown) => call<GeoPosition>("geolocation.getCurrentPosition", [options]),
  watchPosition: (options?: unknown) => call<GeoWatchHandle>("geolocation.watchPosition", [options]),
  clearWatch: (handle: GeoWatchHandle | string) =>
    call<void>("geolocation.clearWatch", [typeof handle === "string" ? handle : handle.id]),
};

/** 触感反馈（命令前缀 `haptics.*`）。 */
export const haptics = {
  vibrate: (durationMs?: number) => call<void>("haptics.vibrate", [durationMs]),
  notification: (type?: string) => call<void>("haptics.notification", [type]),
  cancel: () => call<void>("haptics.cancel", []),
};

/** NFC（命令前缀 `nfc.*`）。 */
export const nfc = {
  read: (options?: unknown) => call<unknown>("nfc.read", [options]),
  write: (payload: unknown, options?: unknown) => call<void>("nfc.write", [payload, options]),
  cancel: () => call<void>("nfc.cancel", []),
};

/** 权限（命令前缀 `permissions.*`）。 */
export const permissions = {
  check: (permission: string) => call<PermissionStatus>("permissions.check", [permission]),
  request: (permission: string) => call<PermissionStatus>("permissions.request", [permission]),
};

/** 设备信息 / Toast（Android 运行时插件，命令前缀 `device.*` / `toast.*`）。 */
export const device = {
  info: () => call<Record<string, unknown>>("device.info", []),
  showToast: (message: string, duration?: "short" | "long") =>
    call<void>("toast.show", [message, duration]),
};
