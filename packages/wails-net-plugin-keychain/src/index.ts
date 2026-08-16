/**
 * @wails-net/plugin-keychain — keychain 插件前端封装。
 * 命令前缀 `keychain.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * keychain 插件前端 API（命令前缀 `keychain.*`）。
 * 对应后端 Wails.Net.Plugins.Keychain.KeychainPlugin 注册的命令。
 * 调用系统原生钥匙串（Windows Credential Manager / Linux libsecret / macOS Keychain）。
 */
export const keychain = {
  /** 设置密码，成功返回 true。 */
  setPassword: (service: string, account: string, password: string) =>
    call<boolean>("keychain.setPassword", [service, account, password]),
  /** 读取密码；未找到或当前平台无原生实现返回 null。 */
  getPassword: (service: string, account: string) =>
    call<string | null>("keychain.getPassword", [service, account]),
  /** 删除密码，成功返回 true。 */
  deletePassword: (service: string, account: string) =>
    call<boolean>("keychain.deletePassword", [service, account]),
};