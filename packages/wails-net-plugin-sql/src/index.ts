/** M3 runtime 变薄：从 @wails-net/runtime 迁移，按需安装 */
import { call } from "@wails-net/runtime";

export const sql = {
  /** 执行非查询 SQL（INSERT/UPDATE/DELETE/DDL），返回受影响行数。 */
  execute: (sql: string, parameters?: unknown[]) =>
    call<number>("sqlite.execute", [sql, parameters ?? []]),
  /** 执行查询 SQL（SELECT），返回行列表 JSON 字符串。 */
  query: (sql: string, parameters?: unknown[]) =>
    call<string>("sqlite.query", [sql, parameters ?? []]),
  /** 执行标量查询，返回第一行第一列的值（JSON 字符串）。 */
  scalar: (sql: string, parameters?: unknown[]) =>
    call<string>("sqlite.scalar", [sql, parameters ?? []]),
  /** 创建表，columnsJson 为列定义 JSON 字符串（形如 {"name":"TEXT"}），返回受影响行数。 */
  createTable: (tableName: string, columnsJson: string) =>
    call<number>("sqlite.createTable", [tableName, columnsJson]),
  /** 删除表，返回受影响行数。 */
  dropTable: (tableName: string) => call<number>("sqlite.dropTable", [tableName]),
  /** 获取所有表名（JSON 字符串数组）。 */
  getTables: () => call<string>("sqlite.getTables", []),
  /** 插入数据，valuesJson 为字段值 JSON 字符串（形如 {"name":"张三"}），返回受影响行数。 */
  insert: (tableName: string, valuesJson: string) =>
    call<number>("sqlite.insert", [tableName, valuesJson]),
  /** 更新数据，valuesJson 为字段值 JSON 字符串，返回受影响行数。 */
  update: (
    tableName: string,
    valuesJson: string,
    whereClause: string,
    parameters?: unknown[],
  ) => call<number>("sqlite.update", [tableName, valuesJson, whereClause, parameters ?? []]),
  /** 删除数据，返回受影响行数。 */
  delete: (tableName: string, whereClause: string, parameters?: unknown[]) =>
    call<number>("sqlite.delete", [tableName, whereClause, parameters ?? []]),
  /** 查询数据，返回行列表 JSON 字符串。columnsJson 为列名 JSON 数组字符串，省略表示全部列。 */
  select: (
    tableName: string,
    columnsJson?: string,
    whereClause?: string,
    orderBy?: string,
    limit?: number,
    offset?: number,
    parameters?: unknown[],
  ) =>
    call<string>("sqlite.select", [
      tableName,
      columnsJson ?? null,
      whereClause ?? null,
      orderBy ?? null,
      limit ?? null,
      offset ?? null,
      parameters ?? [],
    ]),
};