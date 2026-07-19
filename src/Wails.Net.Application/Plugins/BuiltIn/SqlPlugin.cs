using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Plugins.BuiltIn;

/// <summary>
/// SQLite 数据库插件，提供前端执行 SQL 命令的能力。
/// 对应 Tauri v2 的 <c>@tauri-apps/plugin-sql</c>。
/// 通过 <see cref="ICommandContext.Services"/> 从 DI 容器获取 <see cref="SqliteService"/> 单例实例。
/// </summary>
public class SqlPlugin : IPlugin
{
    /// <summary>插件名称</summary>
    public string Name => "sqlite";

    /// <summary>
    /// 注册插件依赖的服务到 DI 容器。此插件无需注册额外服务。
    /// </summary>
    /// <param name="services">DI 服务集合。</param>
    public void ConfigureServices(IServiceCollection services)
    {
        // 无需注册额外服务（SqliteService 已由 AddWailsServices 注册为单例）
    }

    /// <summary>
    /// 配置插件，注册 SQLite 相关命令。
    /// 命令使用异步方法以避免阻塞 UI 线程。
    /// </summary>
    /// <param name="context">插件上下文。</param>
    public void Configure(IPluginContext context)
    {
        // 声明权限集
        context.Permissions.RegisterPermissionSet("sql:default", "SQL 数据库默认权限集",
            "sql:allow-execute", "sql:allow-select");
        context.Permissions.DeclarePermission("sql:allow-execute", "允许执行 SQL 语句");
        context.Permissions.DeclarePermission("sql:allow-select", "允许执行 SQL 查询");

        // 执行非查询 SQL（INSERT/UPDATE/DELETE/DDL），返回受影响行数
        context.Commands.MapCommandAsync("sqlite.execute",
            (Func<ICommandContext, string, object[]?, Task<int>>)(async (ctx, sql, parameters) =>
        {
            var service = ctx.Services.GetService<SqliteService>();
            if (service is null)
            {
                return 0;
            }

            try
            {
                return await service.ExecuteNonQueryAsync(sql, parameters ?? []);
            }
            catch
            {
                return 0;
            }
        }));

        // 执行查询 SQL（SELECT），返回行列表
        context.Commands.MapCommandAsync("sqlite.query",
            (Func<ICommandContext, string, object[]?, Task<string>>)(async (ctx, sql, parameters) =>
        {
            var service = ctx.Services.GetService<SqliteService>();
            if (service is null)
            {
                return "[]";
            }

            try
            {
                var rows = await service.ExecuteQueryAsync(sql, parameters ?? []);
                return JsonSerializer.Serialize(rows);
            }
            catch
            {
                return "[]";
            }
        }));

        // 执行标量查询，返回第一行第一列的值
        context.Commands.MapCommandAsync("sqlite.scalar",
            (Func<ICommandContext, string, object[]?, Task<string>>)(async (ctx, sql, parameters) =>
        {
            var service = ctx.Services.GetService<SqliteService>();
            if (service is null)
            {
                return "null";
            }

            try
            {
                var value = await service.ExecuteScalarAsync<object>(sql, parameters ?? []);
                return JsonSerializer.Serialize(value);
            }
            catch
            {
                return "null";
            }
        }));

        // 创建表
        context.Commands.MapCommandAsync("sqlite.createTable",
            (Func<ICommandContext, string, string, Task<int>>)(async (ctx, tableName, columnsJson) =>
        {
            var service = ctx.Services.GetService<SqliteService>();
            if (service is null)
            {
                return 0;
            }

            try
            {
                var columns = JsonSerializer.Deserialize<Dictionary<string, string>>(columnsJson);
                if (columns is null)
                {
                    return 0;
                }

                return await service.CreateTableAsync(tableName, columns);
            }
            catch
            {
                return 0;
            }
        }));

        // 删除表
        context.Commands.MapCommandAsync("sqlite.dropTable",
            (Func<ICommandContext, string, Task<int>>)(async (ctx, tableName) =>
        {
            var service = ctx.Services.GetService<SqliteService>();
            if (service is null)
            {
                return 0;
            }

            try
            {
                return await service.DropTableAsync(tableName);
            }
            catch
            {
                return 0;
            }
        }));

        // 获取所有表名
        context.Commands.MapCommandAsync("sqlite.getTables",
            (Func<ICommandContext, Task<string>>)(async ctx =>
        {
            var service = ctx.Services.GetService<SqliteService>();
            if (service is null)
            {
                return "[]";
            }

            try
            {
                var tables = await service.GetTablesAsync();
                return JsonSerializer.Serialize(tables);
            }
            catch
            {
                return "[]";
            }
        }));

        // 插入数据
        context.Commands.MapCommandAsync("sqlite.insert",
            (Func<ICommandContext, string, string, Task<int>>)(async (ctx, tableName, valuesJson) =>
        {
            var service = ctx.Services.GetService<SqliteService>();
            if (service is null)
            {
                return 0;
            }

            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(valuesJson);
                if (values is null)
                {
                    return 0;
                }

                return await service.InsertAsync(tableName, values);
            }
            catch
            {
                return 0;
            }
        }));

        // 更新数据
        context.Commands.MapCommandAsync("sqlite.update",
            (Func<ICommandContext, string, string, string, object[]?, Task<int>>)(async (ctx, tableName, valuesJson, whereClause, parameters) =>
        {
            var service = ctx.Services.GetService<SqliteService>();
            if (service is null)
            {
                return 0;
            }

            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(valuesJson);
                if (values is null)
                {
                    return 0;
                }

                return await service.UpdateAsync(tableName, values, whereClause, parameters ?? []);
            }
            catch
            {
                return 0;
            }
        }));

        // 删除数据
        context.Commands.MapCommandAsync("sqlite.delete",
            (Func<ICommandContext, string, string, object[]?, Task<int>>)(async (ctx, tableName, whereClause, parameters) =>
        {
            var service = ctx.Services.GetService<SqliteService>();
            if (service is null)
            {
                return 0;
            }

            try
            {
                return await service.DeleteAsync(tableName, whereClause, parameters ?? []);
            }
            catch
            {
                return 0;
            }
        }));

        // 查询数据
        context.Commands.MapCommandAsync("sqlite.select",
            (Func<ICommandContext, string, string?, string?, string?, int?, int?, object[]?, Task<string>>)(async (ctx, tableName, columnsJson, whereClause, orderBy, limit, offset, parameters) =>
        {
            var service = ctx.Services.GetService<SqliteService>();
            if (service is null)
            {
                return "[]";
            }

            try
            {
                string[]? columns = null;
                if (!string.IsNullOrEmpty(columnsJson))
                {
                    columns = JsonSerializer.Deserialize<string[]>(columnsJson);
                }

                var rows = await service.SelectAsync(tableName, columns, whereClause, orderBy, limit, offset, parameters ?? []);
                return JsonSerializer.Serialize(rows);
            }
            catch
            {
                return "[]";
            }
        }));
    }
}
