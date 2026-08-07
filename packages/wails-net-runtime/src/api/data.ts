/**
 * 数据持久化 / 数据库 / 密钥链 / 安全存储 / 本地化 / 作用域 命令封装。
 *
 * 参数顺序严格对齐后端 `MapCommand` 注册签名。多个 SQLite 命令后端返回的是
 * **JSON 字符串**，此处提供解析后的便捷方法与 `*Raw` 原始字符串方法两套 API。
 */
import { call } from "../core/runtime.js";

/** 将后端返回的 JSON 字符串安全解析为对象；解析失败时返回兜底值。 */
function parseJson<T>(raw: string | null, fallback: T): T {
  if (raw === null || raw === "") {
    return fallback;
  }
  try {
    return JSON.parse(raw) as T;
  } catch {
    return fallback;
  }
}

/** 键值存储（命令前缀 `store.*`）。 */
export const store = {
  get: (key: string) => call<string | null>("store.get", [key]),
  set: (key: string, value: string) => call<void>("store.set", [key, value]),
  has: (key: string) => call<boolean>("store.has", [key]),
  /** @returns 是否实际删除了条目。 */
  delete: (key: string) => call<boolean>("store.delete", [key]),
  keys: () => call<string[]>("store.keys", []),
  clear: () => call<void>("store.clear", []),
  /** 订阅指定 key 的变更，变更通过事件总线派发。 */
  watch: (key: string) => call<void>("store.watch", [key]),
};

/**
 * SQLite（命令前缀 `sqlite.*`）。
 *
 * 约定：
 * - `sql` 类命令接收原始 SQL 与可选的位置参数数组；
 * - `table` 类命令（`insert` / `update` / `select` …）接收表名 + JSON 字符串描述的列或值；
 * - 写操作返回受影响行数，读操作返回 JSON 字符串（`*Raw`）或解析后的对象。
 */
export const sqlite = {
  /** 执行查询，返回解析后的行数组。 */
  query: async (sql: string, parameters?: unknown[]): Promise<Record<string, unknown>[]> =>
    parseJson<Record<string, unknown>[]>(
      await call<string>("sqlite.query", [sql, parameters ?? null]),
      [],
    ),
  /** 执行查询，返回后端原始 JSON 字符串。 */
  queryRaw: (sql: string, parameters?: unknown[]) =>
    call<string>("sqlite.query", [sql, parameters ?? null]),
  /** 执行非查询语句，返回受影响行数。 */
  execute: (sql: string, parameters?: unknown[]) =>
    call<number>("sqlite.execute", [sql, parameters ?? null]),
  /** 执行标量查询，返回后端原始 JSON 字符串。 */
  scalar: (sql: string, parameters?: unknown[]) =>
    call<string>("sqlite.scalar", [sql, parameters ?? null]),
  /**
   * 条件查询。
   * @param tableName 表名。
   * @param columnsJson 需要的列（JSON 数组字符串）；省略表示 `*`。
   * @param whereClause WHERE 子句（不含 `WHERE` 关键字）。
   * @param orderBy ORDER BY 子句（不含关键字）。
   * @param limit 返回行数上限。
   * @param offset 偏移量。
   * @param parameters WHERE 子句的位置参数。
   */
  select: async (
    tableName: string,
    columnsJson?: string,
    whereClause?: string,
    orderBy?: string,
    limit?: number,
    offset?: number,
    parameters?: unknown[],
  ): Promise<Record<string, unknown>[]> =>
    parseJson<Record<string, unknown>[]>(
      await call<string>("sqlite.select", [
        tableName,
        columnsJson ?? null,
        whereClause ?? null,
        orderBy ?? null,
        limit ?? null,
        offset ?? null,
        parameters ?? null,
      ]),
      [],
    ),
  /**
   * 插入一行。
   * @param valuesJson 列值映射的 JSON 字符串，如 `'{"name":"a","age":1}'`。
   */
  insert: (tableName: string, valuesJson: string) =>
    call<number>("sqlite.insert", [tableName, valuesJson]),
  /**
   * 更新行。
   * @param valuesJson 待更新列值的 JSON 字符串。
   * @param whereClause WHERE 子句（不含关键字）。
   */
  update: (tableName: string, valuesJson: string, whereClause: string, parameters?: unknown[]) =>
    call<number>("sqlite.update", [tableName, valuesJson, whereClause, parameters ?? null]),
  /** 删除行，返回受影响行数。 */
  delete: (tableName: string, whereClause: string, parameters?: unknown[]) =>
    call<number>("sqlite.delete", [tableName, whereClause, parameters ?? null]),
  /**
   * 建表。
   * @param columnsJson 列定义的 JSON 字符串。
   */
  createTable: (tableName: string, columnsJson: string) =>
    call<number>("sqlite.createTable", [tableName, columnsJson]),
  dropTable: (tableName: string) => call<number>("sqlite.dropTable", [tableName]),
  /** 列出所有表名。 */
  getTables: async (): Promise<string[]> =>
    parseJson<string[]>(await call<string>("sqlite.getTables", []), []),
};

/** 密钥链（命令前缀 `keychain.*`）。 */
export const keychain = {
  getPassword: (service: string, account: string) =>
    call<string | null>("keychain.getPassword", [service, account]),
  setPassword: (service: string, account: string, password: string) =>
    call<void>("keychain.setPassword", [service, account, password]),
  deletePassword: (service: string, account: string) =>
    call<void>("keychain.deletePassword", [service, account]),
};

/**
 * 安全存储（命令前缀 `stronghold.*`）。
 *
 * 所有方法都接受可选的 `vaultPath`（末位参数），省略时使用默认保险库。
 */
export const stronghold = {
  unlock: (password: string, vaultPath?: string) =>
    call<boolean>("stronghold.unlock", [password, vaultPath ?? null]),
  lock: (vaultPath?: string) => call<void>("stronghold.lock", [vaultPath ?? null]),
  isUnlocked: (vaultPath?: string) => call<boolean>("stronghold.isUnlocked", [vaultPath ?? null]),
  saveSecret: (key: string, value: string, vaultPath?: string) =>
    call<boolean>("stronghold.saveSecret", [key, value, vaultPath ?? null]),
  getSecret: (key: string, vaultPath?: string) =>
    call<string | null>("stronghold.getSecret", [key, vaultPath ?? null]),
  deleteSecret: (key: string, vaultPath?: string) =>
    call<boolean>("stronghold.deleteSecret", [key, vaultPath ?? null]),
  listKeys: (vaultPath?: string) => call<string[] | null>("stronghold.listKeys", [vaultPath ?? null]),
  changePassword: (oldPassword: string, newPassword: string, vaultPath?: string) =>
    call<boolean>("stronghold.changePassword", [oldPassword, newPassword, vaultPath ?? null]),
};

/** 本地化（命令前缀 `localization.*`）。 */
export const localization = {
  t: (key: string, params?: Record<string, unknown>) =>
    call<string>("localization.t", [key, params ?? null]),
  getLocale: () => call<string>("localization.getLocale", []),
  setLocale: (locale: string) => call<void>("localization.setLocale", [locale]),
  getAvailableLocales: () => call<string[]>("localization.getAvailableLocales", []),
  registerTranslations: (locale: string, translations: Record<string, string>) =>
    call<void>("localization.registerTranslations", [locale, translations]),
};

/**
 * 持久化作用域（命令前缀 `scope.*`）。
 *
 * 所有方法都接受可选的 `scopePath`（末位参数），用于指定作用域配置文件位置。
 */
export const scope = {
  addPath: (path: string, scopePath?: string) =>
    call<boolean>("scope.addPath", [path, scopePath ?? null]),
  removePath: (path: string, scopePath?: string) =>
    call<boolean>("scope.removePath", [path, scopePath ?? null]),
  listPaths: (scopePath?: string) => call<string[] | null>("scope.listPaths", [scopePath ?? null]),
  clear: (scopePath?: string) => call<void>("scope.clear", [scopePath ?? null]),
  isAllowed: (path: string, scopePath?: string) =>
    call<boolean>("scope.isAllowed", [path, scopePath ?? null]),
  save: (scopePath?: string) => call<void>("scope.save", [scopePath ?? null]),
  load: (scopePath?: string) => call<boolean>("scope.load", [scopePath ?? null]),
};
