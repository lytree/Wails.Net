using Microsoft.Data.Sqlite;
using System.Text;
using Wails.Net.Application.Options;

namespace Wails.Net.Application.Services;

/// <summary>
/// SQLite 数据库服务，基于 Microsoft.Data.Sqlite 提供真实的关系型数据存储。
/// 对应 Wails v3 Go 版本 pkg/services/sqlite。
/// 支持 :memory: 内存数据库和文件数据库，提供同步与异步 API、事务、表管理和 CRUD 操作。
/// </summary>
public class SqliteService : IServiceStartup, IServiceShutdown
{
    /// <summary>
    /// 内存数据库标识。
    /// </summary>
    private const string InMemoryPath = ":memory:";

    /// <summary>
    /// 每个实例唯一的内存数据库名称，确保多个 SqliteService 实例之间数据隔离。
    /// 使用 Cache=Shared 模式时，同名连接字符串共享同一内存数据库；
    /// 为每个实例生成唯一名称可避免测试或并行使用时的状态污染。
    /// </summary>
    private readonly string _inMemoryDbName = $"InMemoryDb_{Guid.NewGuid():N}";

    /// <summary>
    /// SQLite 连接字符串。
    /// </summary>
    private string _connectionString = string.Empty;

    /// <summary>
    /// 内存数据库模式下保持打开的持久连接，确保数据跨连接共享。
    /// </summary>
    private SqliteConnection? _persistentConnection;

    /// <summary>
    /// 服务是否已关闭。
    /// </summary>
    private bool _isShutdown;

    /// <summary>
    /// 获取或设置数据库路径。
    /// 使用 ":memory:" 表示内存数据库（默认），否则为文件路径。
    /// 必须在 <see cref="ServiceStartup"/> 之前设置。
    /// </summary>
    public string DatabasePath { get; set; } = InMemoryPath;

    /// <summary>
    /// 使用默认配置（内存数据库）构造 SQLite 服务实例。
    /// </summary>
    public SqliteService()
    {
    }

    /// <summary>
    /// 使用指定数据库路径构造 SQLite 服务实例。
    /// </summary>
    /// <param name="databasePath">数据库路径，":memory:" 表示内存数据库。</param>
    public SqliteService(string databasePath)
    {
        DatabasePath = databasePath;
    }

    /// <summary>
    /// 服务启动，初始化数据库连接。
    /// 对于内存数据库，打开一个持久连接以保持数据存活。
    /// </summary>
    /// <param name="options">应用选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动操作的异步任务。</returns>
    public Task ServiceStartup(ApplicationOptions options, CancellationToken cancellationToken)
    {
        _isShutdown = false;
        _connectionString = DatabasePath == InMemoryPath
            ? $"Data Source={_inMemoryDbName};Mode=Memory;Cache=Shared"
            : $"Data Source={DatabasePath}";

        // 内存数据库需要保持一个持久连接，否则数据在连接关闭后丢失
        if (DatabasePath == InMemoryPath)
        {
            _persistentConnection = new SqliteConnection(_connectionString);
            _persistentConnection.Open();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 服务关闭，关闭数据库连接并释放资源。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示关闭操作的异步任务。</returns>
    public async Task ServiceShutdown(CancellationToken cancellationToken)
    {
        _isShutdown = true;

        if (_persistentConnection is not null)
        {
            await _persistentConnection.CloseAsync();
            await _persistentConnection.DisposeAsync();
            _persistentConnection = null;
        }
    }

    /// <summary>
    /// 创建并打开一个新的数据库连接。
    /// 每次操作创建新连接以保证线程安全（Microsoft.Data.Sqlite 连接不是线程安全的）。
    /// </summary>
    /// <returns>已打开的 <see cref="SqliteConnection"/> 实例。</returns>
    /// <exception cref="InvalidOperationException">服务已关闭时抛出。</exception>
    private SqliteConnection CreateConnection()
    {
        if (_isShutdown)
        {
            throw new InvalidOperationException("服务已关闭，数据库不可用。");
        }

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// 向命令添加位置参数，并将 SQL 中的 ? 占位符转换为 @pN 命名参数。
    /// Microsoft.Data.Sqlite 不支持 ? 位置占位符，必须使用 @name 命名参数语法。
    /// </summary>
    /// <param name="command">SQL 命令，其 CommandText 将被设置。</param>
    /// <param name="sql">原始 SQL 语句，可能包含 ? 占位符。</param>
    /// <param name="args">参数值数组。</param>
    private static void AddPositionalParameters(SqliteCommand command, string sql, object?[] args)
    {
        if (args.Length > 0 && sql.Contains('?'))
        {
            // 将 ? 逐个替换为 @p0, @p1, ...
            var sb = new StringBuilder(sql.Length + args.Length * 4);
            var paramIndex = 0;
            foreach (var c in sql)
            {
                if (c == '?')
                {
                    sb.Append("@p").Append(paramIndex);
                    paramIndex++;
                }
                else
                {
                    sb.Append(c);
                }
            }
            command.CommandText = sb.ToString();
        }
        else
        {
            command.CommandText = sql;
        }

        for (var i = 0; i < args.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@p" + i;
            parameter.Value = args[i] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }

    /// <summary>
    /// 向命令添加命名参数（使用 @param 语法）。
    /// </summary>
    /// <param name="command">SQL 命令。</param>
    /// <param name="parameters">参数名到值的字典。</param>
    private static void AddNamedParameters(SqliteCommand command, Dictionary<string, object?> parameters)
    {
        foreach (var kvp in parameters)
        {
            command.Parameters.AddWithValue(kvp.Key, kvp.Value ?? DBNull.Value);
        }
    }

    /// <summary>
    /// 将 <see cref="SqliteException"/> 转换为 <see cref="InvalidOperationException"/>。
    /// 对应 Wails v3 中 SQLite 错误的处理方式。
    /// </summary>
    /// <param name="ex">原始 SQLite 异常。</param>
    /// <returns>包装后的异常。</returns>
    private static InvalidOperationException WrapSqliteException(SqliteException ex)
    {
        return new InvalidOperationException($"SQLite 错误 [{ex.SqliteErrorCode}]: {ex.Message}", ex);
    }

    // ========================================================================
    // 同步方法（向后兼容，对应原有 API）
    // ========================================================================

    /// <summary>
    /// 执行非查询 SQL 语句（CREATE TABLE、INSERT、DELETE、UPDATE）。
    /// 使用 ? 作为位置参数占位符。
    /// </summary>
    /// <param name="sql">SQL 语句，使用 ? 作为参数占位符。</param>
    /// <param name="args">参数值数组，按顺序替换 SQL 中的 ? 占位符。</param>
    /// <returns>受影响的行数。</returns>
    /// <exception cref="NotSupportedException">DROP 等不支持的 SQL 语句。</exception>
    /// <exception cref="InvalidOperationException">SQLite 执行错误或服务已关闭。</exception>
    public int Execute(string sql, object[] args)
    {
        // 向后兼容：DROP 在同步 Execute 方法中不被支持（旧实现的测试依赖此行为）
        var normalizedSql = sql.AsSpan().Trim();
        if (normalizedSql.StartsWith("DROP", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"不支持的 SQL 语句: {sql}");
        }

        try
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            AddPositionalParameters(command, sql, args);
            return command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            throw WrapSqliteException(ex);
        }
    }

    /// <summary>
    /// 执行查询 SQL 语句（SELECT），返回匹配的行列表。
    /// 使用 ? 作为位置参数占位符。
    /// </summary>
    /// <param name="sql">SELECT SQL 语句，使用 ? 作为参数占位符。</param>
    /// <param name="args">参数值数组。</param>
    /// <returns>匹配行的列表，每行为列名到值的字典。</returns>
    /// <exception cref="InvalidOperationException">表不存在或 SQLite 执行错误。</exception>
    public List<Dictionary<string, object?>> Query(string sql, object[] args)
    {
        try
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            AddPositionalParameters(command, sql, args);

            var results = new List<Dictionary<string, object?>>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                results.Add(row);
            }

            return results;
        }
        catch (SqliteException ex)
        {
            throw WrapSqliteException(ex);
        }
    }

    /// <summary>
    /// 执行查询并返回第一行第一列的值。
    /// 使用 ? 作为位置参数占位符。
    /// </summary>
    /// <param name="sql">SELECT SQL 语句。</param>
    /// <param name="args">参数值数组。</param>
    /// <returns>第一行第一列的值，若无结果则返回 null。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误。</exception>
    public object? ExecuteScalar(string sql, object[] args)
    {
        try
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            AddPositionalParameters(command, sql, args);
            var result = command.ExecuteScalar();
            return result is DBNull ? null : result;
        }
        catch (SqliteException ex)
        {
            throw WrapSqliteException(ex);
        }
    }

    // ========================================================================
    // 异步方法（推荐使用，支持 @param 命名参数）
    // ========================================================================

    /// <summary>
    /// 异步执行非查询 SQL 语句（INSERT/UPDATE/DELETE/CREATE/ALTER/DROP）。
    /// 使用 ? 作为位置参数占位符。
    /// </summary>
    /// <param name="sql">SQL 语句，使用 ? 作为参数占位符。</param>
    /// <param name="parameters">位置参数值数组。</param>
    /// <returns>受影响的行数。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误或服务已关闭。</exception>
    public async Task<int> ExecuteNonQueryAsync(string sql, params object?[] parameters)
    {
        try
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            AddPositionalParameters(command, sql, parameters);
            return await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException ex)
        {
            throw WrapSqliteException(ex);
        }
    }

    /// <summary>
    /// 异步执行非查询 SQL 语句，使用命名参数（@param 语法）。
    /// </summary>
    /// <param name="sql">SQL 语句，使用 @param 作为参数占位符。</param>
    /// <param name="parameters">参数名到值的字典，键需以 @ 开头。</param>
    /// <returns>受影响的行数。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误或服务已关闭。</exception>
    public async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object?> parameters)
    {
        try
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddNamedParameters(command, parameters);
            return await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException ex)
        {
            throw WrapSqliteException(ex);
        }
    }

    /// <summary>
    /// 异步执行查询并返回第一行第一列的值，强类型返回。
    /// 使用 ? 作为位置参数占位符。
    /// </summary>
    /// <typeparam name="T">返回值类型。</typeparam>
    /// <param name="sql">SELECT SQL 语句。</param>
    /// <param name="parameters">位置参数值数组。</param>
    /// <returns>第一行第一列的值转换为 T 类型，若无结果则返回 default。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误。</exception>
    public async Task<T?> ExecuteScalarAsync<T>(string sql, params object?[] parameters)
    {
        try
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            AddPositionalParameters(command, sql, parameters);
            var result = await command.ExecuteScalarAsync();
            if (result is null or DBNull)
            {
                return default;
            }

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)Convert.ChangeType(result, targetType);
        }
        catch (SqliteException ex)
        {
            throw WrapSqliteException(ex);
        }
    }

    /// <summary>
    /// 异步执行查询并返回第一行第一列的值，使用命名参数。
    /// </summary>
    /// <typeparam name="T">返回值类型。</typeparam>
    /// <param name="sql">SELECT SQL 语句，使用 @param 作为参数占位符。</param>
    /// <param name="parameters">参数名到值的字典。</param>
    /// <returns>第一行第一列的值转换为 T 类型，若无结果则返回 default。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误。</exception>
    public async Task<T?> ExecuteScalarAsync<T>(string sql, Dictionary<string, object?> parameters)
    {
        try
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddNamedParameters(command, parameters);
            var result = await command.ExecuteScalarAsync();
            if (result is null or DBNull)
            {
                return default;
            }

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)Convert.ChangeType(result, targetType);
        }
        catch (SqliteException ex)
        {
            throw WrapSqliteException(ex);
        }
    }

    /// <summary>
    /// 异步执行查询 SQL 语句，返回行列表。
    /// 使用 ? 作为位置参数占位符。
    /// </summary>
    /// <param name="sql">SELECT SQL 语句。</param>
    /// <param name="parameters">位置参数值数组。</param>
    /// <returns>匹配行的列表，每行为列名到值的字典。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误。</exception>
    public async Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(string sql, params object?[] parameters)
    {
        try
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            AddPositionalParameters(command, sql, parameters);

            var results = new List<Dictionary<string, object?>>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                results.Add(row);
            }

            return results;
        }
        catch (SqliteException ex)
        {
            throw WrapSqliteException(ex);
        }
    }

    /// <summary>
    /// 异步执行查询 SQL 语句，使用命名参数。
    /// </summary>
    /// <param name="sql">SELECT SQL 语句，使用 @param 作为参数占位符。</param>
    /// <param name="parameters">参数名到值的字典。</param>
    /// <returns>匹配行的列表，每行为列名到值的字典。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误。</exception>
    public async Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(string sql, Dictionary<string, object?> parameters)
    {
        try
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddNamedParameters(command, parameters);

            var results = new List<Dictionary<string, object?>>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }

                results.Add(row);
            }

            return results;
        }
        catch (SqliteException ex)
        {
            throw WrapSqliteException(ex);
        }
    }

    // ========================================================================
    // 事务支持
    // ========================================================================

    /// <summary>
    /// 开始一个数据库事务。
    /// 返回的 <see cref="SqliteTransaction"/> 持有一个专用连接，
    /// 调用 <see cref="CommitTransaction"/> 或 <see cref="RollbackTransaction"/> 后连接自动关闭。
    /// </summary>
    /// <returns>事务对象。</returns>
    /// <exception cref="InvalidOperationException">服务已关闭。</exception>
    public SqliteTransaction BeginTransaction()
    {
        var connection = CreateConnection();
        return connection.BeginTransaction();
    }

    /// <summary>
    /// 异步开始一个数据库事务。
    /// </summary>
    /// <returns>事务对象。</returns>
    /// <exception cref="InvalidOperationException">服务已关闭。</exception>
    public async Task<SqliteTransaction> BeginTransactionAsync()
    {
        var connection = CreateConnection();
        return (SqliteTransaction)await connection.BeginTransactionAsync();
    }

    /// <summary>
    /// 提交事务并关闭关联的连接。
    /// </summary>
    /// <param name="transaction">要提交的事务。</param>
    public void CommitTransaction(SqliteTransaction transaction)
    {
        var connection = transaction.Connection;
        transaction.Commit();
        transaction.Dispose();
        connection?.Close();
        connection?.Dispose();
    }

    /// <summary>
    /// 异步提交事务并关闭关联的连接。
    /// </summary>
    /// <param name="transaction">要提交的事务。</param>
    public async Task CommitTransactionAsync(SqliteTransaction transaction)
    {
        var connection = transaction.Connection;
        await transaction.CommitAsync();
        await transaction.DisposeAsync();
        if (connection is not null)
        {
            await connection.CloseAsync();
            await connection.DisposeAsync();
        }
    }

    /// <summary>
    /// 回滚事务并关闭关联的连接。
    /// </summary>
    /// <param name="transaction">要回滚的事务。</param>
    public void RollbackTransaction(SqliteTransaction transaction)
    {
        var connection = transaction.Connection;
        transaction.Rollback();
        transaction.Dispose();
        connection?.Close();
        connection?.Dispose();
    }

    /// <summary>
    /// 异步回滚事务并关闭关联的连接。
    /// </summary>
    /// <param name="transaction">要回滚的事务。</param>
    public async Task RollbackTransactionAsync(SqliteTransaction transaction)
    {
        var connection = transaction.Connection;
        await transaction.RollbackAsync();
        await transaction.DisposeAsync();
        if (connection is not null)
        {
            await connection.CloseAsync();
            await connection.DisposeAsync();
        }
    }

    // ========================================================================
    // 表管理
    // ========================================================================

    /// <summary>
    /// 异步创建表。
    /// </summary>
    /// <param name="tableName">表名。</param>
    /// <param name="columnDefinitions">列定义字典，键为列名，值为 SQLite 类型定义（如 "INTEGER PRIMARY KEY"、"TEXT NOT NULL"）。</param>
    /// <returns>受影响的行数（CREATE TABLE 通常返回 0）。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误。</exception>
    public Task<int> CreateTableAsync(string tableName, Dictionary<string, string> columnDefinitions)
    {
        var columns = string.Join(", ", columnDefinitions.Select(c => $"{c.Key} {c.Value}"));
        var sql = $"CREATE TABLE {tableName} ({columns})";
        return ExecuteNonQueryAsync(sql);
    }

    /// <summary>
    /// 异步删除表（使用 IF EXISTS 避免表不存在时抛出异常）。
    /// </summary>
    /// <param name="tableName">表名。</param>
    /// <returns>受影响的行数。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误。</exception>
    public Task<int> DropTableAsync(string tableName)
    {
        return ExecuteNonQueryAsync($"DROP TABLE IF EXISTS {tableName}");
    }

    /// <summary>
    /// 异步获取所有用户表名列表。
    /// 查询 sqlite_master 系统表，按名称排序。
    /// </summary>
    /// <returns>表名列表。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误。</exception>
    public async Task<List<string>> GetTablesAsync()
    {
        var rows = await ExecuteQueryAsync(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");
        return rows
            .Select(r => r.TryGetValue("name", out var name) ? name?.ToString() : null)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();
    }

    // ========================================================================
    // CRUD 操作
    // ========================================================================

    /// <summary>
    /// 异步插入一行数据。
    /// </summary>
    /// <param name="tableName">表名。</param>
    /// <param name="values">列名到值的字典。</param>
    /// <returns>受影响的行数（通常为 1）。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误。</exception>
    public Task<int> InsertAsync(string tableName, Dictionary<string, object?> values)
    {
        var columns = new List<string>();
        var placeholders = new List<string>();
        var parameters = new Dictionary<string, object?>();
        var i = 0;
        foreach (var kvp in values)
        {
            var paramName = $"@p{i}";
            columns.Add(kvp.Key);
            placeholders.Add(paramName);
            parameters[paramName] = kvp.Value;
            i++;
        }

        var sql = $"INSERT INTO {tableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})";
        return ExecuteNonQueryAsync(sql, parameters);
    }

    /// <summary>
    /// 异步更新匹配行的指定列。
    /// </summary>
    /// <param name="tableName">表名。</param>
    /// <param name="values">要更新的列名到值字典。</param>
    /// <param name="whereClause">WHERE 子句（不含 WHERE 关键字），使用 ? 作为参数占位符。</param>
    /// <param name="parameters">WHERE 子句的参数值数组。</param>
    /// <returns>受影响的行数。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误。</exception>
    public Task<int> UpdateAsync(string tableName, Dictionary<string, object?> values, string whereClause, params object?[] parameters)
    {
        var setClauses = new List<string>();
        var allParameters = new List<object?>();

        // SET 子句使用 ? 占位符
        foreach (var kvp in values)
        {
            setClauses.Add($"{kvp.Key} = ?");
            allParameters.Add(kvp.Value);
        }

        var sql = $"UPDATE {tableName} SET {string.Join(", ", setClauses)}";
        if (!string.IsNullOrEmpty(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }

        allParameters.AddRange(parameters);
        return ExecuteNonQueryAsync(sql, allParameters.ToArray());
    }

    /// <summary>
    /// 异步删除匹配行。
    /// </summary>
    /// <param name="tableName">表名。</param>
    /// <param name="whereClause">WHERE 子句（不含 WHERE 关键字），使用 ? 作为参数占位符。</param>
    /// <param name="parameters">WHERE 子句的参数值数组。</param>
    /// <returns>受影响的行数。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误。</exception>
    public Task<int> DeleteAsync(string tableName, string whereClause, params object?[] parameters)
    {
        var sql = $"DELETE FROM {tableName}";
        if (!string.IsNullOrEmpty(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }

        return ExecuteNonQueryAsync(sql, parameters);
    }

    /// <summary>
    /// 异步查询表数据，支持列选择、条件过滤、排序、分页。
    /// </summary>
    /// <param name="tableName">表名。</param>
    /// <param name="columns">要查询的列名数组，为 null 时查询所有列（*）。</param>
    /// <param name="whereClause">WHERE 子句（不含 WHERE 关键字），使用 ? 作为参数占位符。</param>
    /// <param name="orderBy">ORDER BY 子句（不含 ORDER BY 关键字）。</param>
    /// <param name="limit">返回行数限制。</param>
    /// <param name="offset">结果偏移量。</param>
    /// <param name="parameters">WHERE 子句的参数值数组。</param>
    /// <returns>匹配行的列表。</returns>
    /// <exception cref="InvalidOperationException">SQLite 执行错误。</exception>
    public Task<List<Dictionary<string, object?>>> SelectAsync(
        string tableName,
        string[]? columns,
        string? whereClause,
        string? orderBy,
        int? limit,
        int? offset,
        params object?[] parameters)
    {
        var colList = columns is null || columns.Length == 0
            ? "*"
            : string.Join(", ", columns);

        var sql = $"SELECT {colList} FROM {tableName}";
        if (!string.IsNullOrEmpty(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }

        if (!string.IsNullOrEmpty(orderBy))
        {
            sql += $" ORDER BY {orderBy}";
        }

        if (limit.HasValue)
        {
            sql += $" LIMIT {limit.Value}";
        }

        if (offset.HasValue)
        {
            sql += $" OFFSET {offset.Value}";
        }

        return ExecuteQueryAsync(sql, parameters);
    }
}
