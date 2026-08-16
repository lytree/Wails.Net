/**
 * @wails-net/plugin-power-management — power-management 插件前端封装。
 * 命令前缀 `power.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * power-management 插件前端 API（命令前缀 `power.*`）。
 * 对应后端 Wails.Net.Plugins.Powermanagement.PowerManagementPlugin 注册的命令。
 */
export const powerManagement = {
  /** 请求系统保持唤醒（防止挂起）；返回是否成功（对应 `power.requestWakeLock`）。 */
  requestWakeLock: () => call<boolean>("power.requestWakeLock", []),
  /** 释放保持唤醒锁；返回是否成功（对应 `power.releaseWakeLock`）。 */
  releaseWakeLock: () => call<boolean>("power.releaseWakeLock", []),
  /** 查询当前是否持有唤醒锁（对应 `power.isWakeLockHeld`）。 */
  isWakeLockHeld: () => call<boolean>("power.isWakeLockHeld", []),
};