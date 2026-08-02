/**
 * 数据持久化 / 数据库 / 密钥链 / 安全存储 / 本地化 / 作用域 命令封装。
 */
import { call } from "../core/runtime.js";

/** 键值存储（命令前缀 `store.*`）。 */
export const store = {
  get: (key: string) => call<string | null>("store.get", [key]),
  set: (key: string, value: string) => call<void>("store.set", [key, value]),
  has: (key: string) => call<boolean>("store.has", [key]),
  delete: (key: string) => call<void>("store.delete", [key]),
  keys: () => call<string[]>("store.keys", []),
  clear: () => call<void>("store.clear", []),
  watch: (key: string) => call<void>("store.watch", [key]),
};

/** SQLite（命令前缀 `sqlite.*`）。 */
export const sqlite = {
  query: (sql: string, params?: unknown[]) => call<unknown[]>("sqlite.query", [sql, params]),
  execute: (sql: string, params?: unknown[]) => call<number>("sqlite.execute", [sql, params]),
  scalar: (sql: string, params?: unknown[]) => call<unknown>("sqlite.scalar", [sql, params]),
  select: (table: string, where?: unknown) => call<unknown[]>("sqlite.select", [table, where]),
  insert: (table: string, values: Record<string, unknown>) => call<number>("sqlite.insert", [table, values]),
  update: (table: string, values: Record<string, unknown>, where?: unknown) =>
    call<number>("sqlite.update", [table, values, where]),
  delete: (table: string, where?: unknown) => call<number>("sqlite.delete", [table, where]),
  createTable: (sql: string) => call<void>("sqlite.createTable", [sql]),
  dropTable: (table: string) => call<void>("sqlite.dropTable", [table]),
  getTables: () => call<string[]>("sqlite.getTables", []),
};

/** 密钥链（命令前缀 `keychain.*`）。 */
export const keychain = {
  getPassword: (service: string, account: string) => call<string | null>("keychain.getPassword", [service, account]),
  setPassword: (service: string, account: string, password: string) =>
    call<void>("keychain.setPassword", [service, account, password]),
  deletePassword: (service: string, account: string) => call<void>("keychain.deletePassword", [service, account]),
};

/** 安全存储（命令前缀 `stronghold.*`）。 */
export const stronghold = {
  unlock: (password: string, vaultPath?: string) => call<void>("stronghold.unlock", [password, vaultPath]),
  lock: () => call<void>("stronghold.lock", []),
  isUnlocked: () => call<boolean>("stronghold.isUnlocked", []),
  saveSecret: (key: string, value: string) => call<void>("stronghold.saveSecret", [key, value]),
  getSecret: (key: string) => call<string | null>("stronghold.getSecret", [key]),
  deleteSecret: (key: string) => call<void>("stronghold.deleteSecret", [key]),
  listKeys: () => call<string[]>("stronghold.listKeys", []),
  changePassword: (oldPassword: string, newPassword: string) =>
    call<void>("stronghold.changePassword", [oldPassword, newPassword]),
};

/** 本地化（命令前缀 `localization.*`）。 */
export const localization = {
  t: (key: string, params?: Record<string, unknown>) => call<string>("localization.t", [key, params]),
  getLocale: () => call<string>("localization.getLocale", []),
  setLocale: (locale: string) => call<void>("localization.setLocale", [locale]),
  getAvailableLocales: () => call<string[]>("localization.getAvailableLocales", []),
  registerTranslations: (locale: string, translations: Record<string, string>) =>
    call<void>("localization.registerTranslations", [locale, translations]),
};

/** 持久化作用域（命令前缀 `scope.*`）。 */
export const scope = {
  addPath: (path: string, scopePath?: string) => call<void>("scope.addPath", [path, scopePath]),
  removePath: (path: string) => call<void>("scope.removePath", [path]),
  listPaths: () => call<string[]>("scope.listPaths", []),
  clear: () => call<void>("scope.clear", []),
  isAllowed: (path: string) => call<boolean>("scope.isAllowed", [path]),
  save: () => call<void>("scope.save", []),
  load: () => call<void>("scope.load", []),
};
