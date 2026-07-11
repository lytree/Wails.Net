using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Tests;

/// <summary>
/// SqliteService 的单元测试（TUnit）。
/// 测试内存数据存储的 CREATE、INSERT、SELECT、DELETE、UPDATE 操作。
/// </summary>
[NotInParallel]
public sealed class SqliteServiceTests
{
    [Test]
    public async Task Execute_CreateTable_ReturnsZero()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

        // 操作
        var result = service.Execute("CREATE TABLE users (name TEXT, age INTEGER)", Array.Empty<object>());

        // 断言
        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task Execute_Insert_ReturnsOne()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
        service.Execute("CREATE TABLE users (name TEXT, age INTEGER)", Array.Empty<object>());

        // 操作
        var result = service.Execute(
            "INSERT INTO users (name, age) VALUES (?, ?)",
            new object[] { "Alice", 30 });

        // 断言
        await Assert.That(result).IsEqualTo(1);
    }

    [Test]
    public async Task Query_SelectAll_ReturnsAllRows()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
        service.Execute("CREATE TABLE users (name TEXT, age INTEGER)", Array.Empty<object>());
        service.Execute("INSERT INTO users (name, age) VALUES (?, ?)", new object[] { "Alice", 30 });
        service.Execute("INSERT INTO users (name, age) VALUES (?, ?)", new object[] { "Bob", 25 });

        // 操作
        var rows = service.Query("SELECT * FROM users", Array.Empty<object>());

        // 断言
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[0]["name"]?.ToString()).IsEqualTo("Alice");
        await Assert.That(rows[1]["name"]?.ToString()).IsEqualTo("Bob");
    }

    [Test]
    public async Task Query_WithWhere_ReturnsMatchingRows()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
        service.Execute("CREATE TABLE users (name TEXT, age INTEGER)", Array.Empty<object>());
        service.Execute("INSERT INTO users (name, age) VALUES (?, ?)", new object[] { "Alice", 30 });
        service.Execute("INSERT INTO users (name, age) VALUES (?, ?)", new object[] { "Bob", 25 });
        service.Execute("INSERT INTO users (name, age) VALUES (?, ?)", new object[] { "Charlie", 30 });

        // 操作
        var rows = service.Query("SELECT * FROM users WHERE age = ?", new object[] { 30 });

        // 断言
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[0]["name"]?.ToString()).IsEqualTo("Alice");
        await Assert.That(rows[1]["name"]?.ToString()).IsEqualTo("Charlie");
    }

    [Test]
    public async Task Query_NonExistentTable_ThrowsInvalidOperationException()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

        // 操作与断言
        await Assert.That(() => service.Query("SELECT * FROM nonexistent", Array.Empty<object>()))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Execute_Delete_RemovesMatchingRows()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
        service.Execute("CREATE TABLE users (name TEXT, age INTEGER)", Array.Empty<object>());
        service.Execute("INSERT INTO users (name, age) VALUES (?, ?)", new object[] { "Alice", 30 });
        service.Execute("INSERT INTO users (name, age) VALUES (?, ?)", new object[] { "Bob", 25 });

        // 操作
        var result = service.Execute("DELETE FROM users WHERE name = ?", new object[] { "Alice" });

        // 断言
        await Assert.That(result).IsEqualTo(1);
        var rows = service.Query("SELECT * FROM users", Array.Empty<object>());
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0]["name"]?.ToString()).IsEqualTo("Bob");
    }

    [Test]
    public async Task Execute_Update_ModifiesMatchingRows()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
        service.Execute("CREATE TABLE users (name TEXT, age INTEGER)", Array.Empty<object>());
        service.Execute("INSERT INTO users (name, age) VALUES (?, ?)", new object[] { "Alice", 30 });
        service.Execute("INSERT INTO users (name, age) VALUES (?, ?)", new object[] { "Bob", 25 });

        // 操作
        var result = service.Execute(
            "UPDATE users SET age = ? WHERE name = ?",
            new object[] { 26, "Bob" });

        // 断言
        await Assert.That(result).IsEqualTo(1);
        var rows = service.Query("SELECT * FROM users WHERE name = ?", new object[] { "Bob" });
        await Assert.That(rows[0]["age"]?.ToString()).IsEqualTo("26");
    }

    [Test]
    public async Task ExecuteScalar_ReturnsFirstColumnOfFirstRow()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
        service.Execute("CREATE TABLE users (name TEXT, age INTEGER)", Array.Empty<object>());
        service.Execute("INSERT INTO users (name, age) VALUES (?, ?)", new object[] { "Alice", 30 });

        // 操作
        var result = service.ExecuteScalar("SELECT * FROM users WHERE name = ?", new object[] { "Alice" });

        // 断言
        await Assert.That(result?.ToString()).IsEqualTo("Alice");
    }

    [Test]
    public async Task ExecuteScalar_NoResults_ReturnsNull()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
        service.Execute("CREATE TABLE users (name TEXT, age INTEGER)", Array.Empty<object>());

        // 操作
        var result = service.ExecuteScalar("SELECT * FROM users WHERE name = ?", new object[] { "Nobody" });

        // 断言
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Execute_UnsupportedSql_ThrowsNotSupportedException()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

        // 操作与断言
        await Assert.That(() => service.Execute("DROP TABLE users", Array.Empty<object>()))
            .ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task Execute_InsertIntoNonExistentTable_ThrowsInvalidOperationException()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

        // 操作与断言
        await Assert.That(() => service.Execute(
            "INSERT INTO nonexistent (col) VALUES (?)",
            new object[] { "val" }))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Execute_DeleteMultipleRows_ReturnsCorrectCount()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
        service.Execute("CREATE TABLE items (name TEXT, category TEXT)", Array.Empty<object>());
        service.Execute("INSERT INTO items (name, category) VALUES (?, ?)", new object[] { "A", "X" });
        service.Execute("INSERT INTO items (name, category) VALUES (?, ?)", new object[] { "B", "X" });
        service.Execute("INSERT INTO items (name, category) VALUES (?, ?)", new object[] { "C", "Y" });

        // 操作
        var result = service.Execute("DELETE FROM items WHERE category = ?", new object[] { "X" });

        // 断言
        await Assert.That(result).IsEqualTo(2);
        var rows = service.Query("SELECT * FROM items", Array.Empty<object>());
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0]["name"]?.ToString()).IsEqualTo("C");
    }

    [Test]
    public async Task ServiceShutdown_ClearsAllTables()
    {
        // 安排
        var service = new SqliteService();
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
        service.Execute("CREATE TABLE users (name TEXT, age INTEGER)", Array.Empty<object>());
        service.Execute("INSERT INTO users (name) VALUES (?)", new object[] { "Alice" });

        // 操作
        await service.ServiceShutdown(CancellationToken.None);

        // 断言：关闭后查询应抛出异常（表已清空）
        await Assert.That(() => service.Query("SELECT * FROM users", Array.Empty<object>()))
            .ThrowsExactly<InvalidOperationException>();
    }
}
