/**
 * @wails-net/plugin-stronghold — stronghold 插件前端封装。
 * 命令前缀 `stronghold.*`（M3 runtime 变薄：从 @wails-net/runtime 迁移至此，按需安装）。
 * @platform windows,linux,macos,android  全平台通用插件。
 */
import { call } from "@wails-net/runtime";

/**
 * stronghold 插件前端 API（命令前缀 `stronghold.*`）。
 * 对应后端 Wails.Net.Plugins.Stronghold.StrongholdPlugin 注册的命令。
 * 使用 AES-GCM 认证加密和 PBKDF2 密钥派生，加密数据持久化到 JSON 文件。
 */
export const stronghold = {
  /** 使用密码解锁或创建金库，成功返回 true。vaultPath 省略时使用默认金库路径。 */
  unlock: (password: string, vaultPath?: string | null) =>
    call<boolean>("stronghold.unlock", [password, vaultPath ?? null]),
  /** 锁定金库（清除内存中的密钥和秘密数据）。vaultPath 省略时使用默认金库路径。 */
  lock: (vaultPath?: string | null) => call<void>("stronghold.lock", [vaultPath ?? null]),
  /** 保存加密秘密，成功返回 true。vaultPath 省略时使用默认金库路径。 */
  saveSecret: (key: string, value: string, vaultPath?: string | null) =>
    call<boolean>("stronghold.saveSecret", [key, value, vaultPath ?? null]),
  /** 获取解密秘密；未找到或金库未解锁返回 null。vaultPath 省略时使用默认金库路径。 */
  getSecret: (key: string, vaultPath?: string | null) =>
    call<string | null>("stronghold.getSecret", [key, vaultPath ?? null]),
  /** 删除秘密，成功返回 true。vaultPath 省略时使用默认金库路径。 */
  deleteSecret: (key: string, vaultPath?: string | null) =>
    call<boolean>("stronghold.deleteSecret", [key, vaultPath ?? null]),
  /** 列出所有秘密键；金库未解锁返回 null。vaultPath 省略时使用默认金库路径。 */
  listKeys: (vaultPath?: string | null) =>
    call<string[] | null>("stronghold.listKeys", [vaultPath ?? null]),
  /** 检查金库是否已解锁。vaultPath 省略时使用默认金库路径。 */
  isUnlocked: (vaultPath?: string | null) =>
    call<boolean>("stronghold.isUnlocked", [vaultPath ?? null]),
  /** 修改金库密码，旧密码错误返回 false。vaultPath 省略时使用默认金库路径。 */
  changePassword: (oldPassword: string, newPassword: string, vaultPath?: string | null) =>
    call<boolean>("stronghold.changePassword", [oldPassword, newPassword, vaultPath ?? null]),
};