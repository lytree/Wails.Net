/**
 * 移动端插件命令封装（Android / iOS 通用）：
 * 条码扫描、生物识别、相机、定位、触感、NFC、权限、设备信息。
 *
 * 说明：`camera.capture` / `nfc.read` / `barcode-scanner.scan` 等命令的取消由后端
 * `ICommandContext.CancellationToken` 提供，**不接受前端传参**（见 AGENTS.md §3.4.6）。
 */
import { call } from "../core/runtime.js";
/** 条码扫描（命令前缀 `barcode-scanner.*`）。 */
export const barcodeScanner = {
    /** 启动扫描，返回扫描到的文本内容。 */
    scan: () => call("barcode-scanner.scan", []),
    cancel: () => call("barcode-scanner.cancel", []),
};
/** 生物识别（命令前缀 `biometric.*`）。 */
export const biometric = {
    checkAvailability: () => call("biometric.checkAvailability", []),
    /** @param reason 展示给用户的认证理由文本。 */
    authenticate: (reason) => call("biometric.authenticate", [{ reason }]),
};
/** 相机（命令前缀 `camera.*`）。 */
export const camera = {
    checkAvailability: () => call("camera.checkAvailability", []),
    capture: () => call("camera.capture", []),
    cancel: () => call("camera.cancel", []),
};
/** 地理定位（命令前缀 `geolocation.*`）。 */
export const geolocation = {
    checkAvailability: () => call("geolocation.checkAvailability", []),
    getCurrentPosition: (options) => call("geolocation.getCurrentPosition", [options ?? null]),
    watchPosition: (options) => call("geolocation.watchPosition", [options ?? null]),
    /** @param handle `watchPosition` 返回的句柄，或直接传数字 `watchId`。 */
    clearWatch: (handle) => call("geolocation.clearWatch", [
        typeof handle === "number" ? { watchId: handle } : { watchId: handle.watchId },
    ]),
};
/** 触感反馈（命令前缀 `haptics.*`）。 */
export const haptics = {
    /** @param duration 震动时长（毫秒）。 */
    vibrate: (duration) => call("haptics.vibrate", [{ duration }]),
    notification: (type) => call("haptics.notification", [{ type }]),
    cancel: () => call("haptics.cancel", []),
};
/** NFC（命令前缀 `nfc.*`）。 */
export const nfc = {
    /** 读取 NFC 标签，返回文本内容。 */
    read: () => call("nfc.read", []),
    /** @param data 写入的文本数据。 */
    write: (data) => call("nfc.write", [{ data }]),
    cancel: () => call("nfc.cancel", []),
};
/** 权限（命令前缀 `permissions.*`）。 */
export const permissions = {
    check: (permission) => call("permissions.check", [permission]),
    request: (permission) => call("permissions.request", [permission]),
};
/** 设备信息 / Toast（Android 运行时插件，命令前缀 `device.*` / `toast.*`）。 */
export const device = {
    /**
     * 获取设备信息。
     * 后端返回 JSON 字符串，此处解析为对象。
     */
    info: async () => {
        const raw = await call("device.info", []);
        try {
            return JSON.parse(raw);
        }
        catch {
            return {};
        }
    },
    /** 获取设备信息的后端原始 JSON 字符串。 */
    infoRaw: () => call("device.info", []),
    /** 弹出系统 Toast。 */
    showToast: (message) => call("toast.show", [{ message }]),
};
//# sourceMappingURL=mobile.js.map