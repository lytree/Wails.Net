/**
 * 数据持久化 / 数据库 / 密钥链 / 安全存储 / 本地化 / 作用域 命令封装。
 *
 * 参数顺序严格对齐后端 `MapCommand` 注册签名。多个 SQLite 命令后端返回的是
 * **JSON 字符串**，此处提供解析后的便捷方法与 `*Raw` 原始字符串方法两套 API。
 */
import { call } from "../core/runtime.js";
/** 将后端返回的 JSON 字符串安全解析为对象；解析失败时返回兜底值。 */
function parseJson(raw, fallback) {
    if (raw === null || raw === "") {
        return fallback;
    }
    try {
        return JSON.parse(raw);
    }
    catch {
        return fallback;
    }
}
/** 键值存储（命令前缀 `store.*`）。 */
export const store = {
    get: (key) => call("store.get", [key]),
    set: (key, value) => call("store.set", [key, value]),
    has: (key) => call("store.has", [key]),
    /** @returns 是否实际删除了条目。 */
    delete: (key) => call("store.delete", [key]),
    keys: () => call("store.keys", []),
    clear: () => call("store.clear", []),
    /** 订阅指定 key 的变更，变更通过事件总线派发。 */
    watch: (key) => call("store.watch", [key]),
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
    query: async (sql, parameters) => parseJson(await call("sqlite.query", [sql, parameters ?? null]), []),
    /** 执行查询，返回后端原始 JSON 字符串。 */
    queryRaw: (sql, parameters) => call("sqlite.query", [sql, parameters ?? null]),
    /** 执行非查询语句，返回受影响行数。 */
    execute: (sql, parameters) => call("sqlite.execute", [sql, parameters ?? null]),
    /** 执行标量查询，返回后端原始 JSON 字符串。 */
    scalar: (sql, parameters) => call("sqlite.scalar", [sql, parameters ?? null]),
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
    select: async (tableName, columnsJson, whereClause, orderBy, limit, offset, parameters) => parseJson(await call("sqlite.select", [
        tableName,
        columnsJson ?? null,
        whereClause ?? null,
        orderBy ?? null,
        limit ?? null,
        offset ?? null,
        parameters ?? null,
    ]), []),
    /**
     * 插入一行。
     * @param valuesJson 列值映射的 JSON 字符串，如 `'{"name":"a","age":1}'`。
     */
    insert: (tableName, valuesJson) => call("sqlite.insert", [tableName, valuesJson]),
    /**
     * 更新行。
     * @param valuesJson 待更新列值的 JSON 字符串。
     * @param whereClause WHERE 子句（不含关键字）。
     */
    update: (tableName, valuesJson, whereClause, parameters) => call("sqlite.update", [tableName, valuesJson, whereClause, parameters ?? null]),
    /** 删除行，返回受影响行数。 */
    delete: (tableName, whereClause, parameters) => call("sqlite.delete", [tableName, whereClause, parameters ?? null]),
    /**
     * 建表。
     * @param columnsJson 列定义的 JSON 字符串。
     */
    createTable: (tableName, columnsJson) => call("sqlite.createTable", [tableName, columnsJson]),
    dropTable: (tableName) => call("sqlite.dropTable", [tableName]),
    /** 列出所有表名。 */
    getTables: async () => parseJson(await call("sqlite.getTables", []), []),
};
/** 密钥链（命令前缀 `keychain.*`）。 */
export const keychain = {
    getPassword: (service, account) => call("keychain.getPassword", [service, account]),
    setPassword: (service, account, password) => call("keychain.setPassword", [service, account, password]),
    deletePassword: (service, account) => call("keychain.deletePassword", [service, account]),
};
/**
 * 安全存储（命令前缀 `stronghold.*`）。
 *
 * 所有方法都接受可选的 `vaultPath`（末位参数），省略时使用默认保险库。
 */
export const stronghold = {
    unlock: (password, vaultPath) => call("stronghold.unlock", [password, vaultPath ?? null]),
    lock: (vaultPath) => call("stronghold.lock", [vaultPath ?? null]),
    isUnlocked: (vaultPath) => call("stronghold.isUnlocked", [vaultPath ?? null]),
    saveSecret: (key, value, vaultPath) => call("stronghold.saveSecret", [key, value, vaultPath ?? null]),
    getSecret: (key, vaultPath) => call("stronghold.getSecret", [key, vaultPath ?? null]),
    deleteSecret: (key, vaultPath) => call("stronghold.deleteSecret", [key, vaultPath ?? null]),
    listKeys: (vaultPath) => call("stronghold.listKeys", [vaultPath ?? null]),
    changePassword: (oldPassword, newPassword, vaultPath) => call("stronghold.changePassword", [oldPassword, newPassword, vaultPath ?? null]),
};
/** 本地化（命令前缀 `localization.*`）。 */
export const localization = {
    t: (key, params) => call("localization.t", [key, params ?? null]),
    getLocale: () => call("localization.getLocale", []),
    setLocale: (locale) => call("localization.setLocale", [locale]),
    getAvailableLocales: () => call("localization.getAvailableLocales", []),
    registerTranslations: (locale, translations) => call("localization.registerTranslations", [locale, translations]),
};
/**
 * 持久化作用域（命令前缀 `scope.*`）。
 *
 * 所有方法都接受可选的 `scopePath`（末位参数），用于指定作用域配置文件位置。
 */
export const scope = {
    addPath: (path, scopePath) => call("scope.addPath", [path, scopePath ?? null]),
    removePath: (path, scopePath) => call("scope.removePath", [path, scopePath ?? null]),
    listPaths: (scopePath) => call("scope.listPaths", [scopePath ?? null]),
    clear: (scopePath) => call("scope.clear", [scopePath ?? null]),
    isAllowed: (path, scopePath) => call("scope.isAllowed", [path, scopePath ?? null]),
    save: (scopePath) => call("scope.save", [scopePath ?? null]),
    load: (scopePath) => call("scope.load", [scopePath ?? null]),
};
//# sourceMappingURL=data.js.map