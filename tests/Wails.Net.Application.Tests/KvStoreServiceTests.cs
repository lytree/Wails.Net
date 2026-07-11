using System.Text.Json;
using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Tests;

/// <summary>
/// KvStoreService 的单元测试（TUnit）。
/// 测试键值存储的增删改查、持久化、生命周期方法。
/// </summary>
[NotInParallel]
public sealed class KvStoreServiceTests
{
    [Test]
    public async Task Set_StoresValueAsJson()
    {
        // 安排
        var service = new KvStoreService();

        // 操作
        service.Set("name", "Wails.Net");

        // 断言
        await Assert.That(service.Get("name")).IsEqualTo("\"Wails.Net\"");
    }

    [Test]
    public async Task Set_StoresNumberAsJson()
    {
        // 安排
        var service = new KvStoreService();

        // 操作
        service.Set("count", 42);

        // 断言
        await Assert.That(service.Get("count")).IsEqualTo("42");
    }

    [Test]
    public async Task Set_StoresObjectAsJson()
    {
        // 安排
        var service = new KvStoreService();

        // 操作
        service.Set("user", new { name = "Alice", age = 30 });

        // 断言
        var json = service.Get("user");
        await Assert.That(json).IsNotNull();
        using var doc = JsonDocument.Parse(json!);
        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("Alice");
        await Assert.That(doc.RootElement.GetProperty("age").GetInt32()).IsEqualTo(30);
    }

    [Test]
    public async Task Get_NonExistentKey_ReturnsNull()
    {
        // 安排
        var service = new KvStoreService();

        // 操作
        var result = service.Get("nonexistent");

        // 断言
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Delete_ExistingKey_ReturnsTrue()
    {
        // 安排
        var service = new KvStoreService();
        service.Set("key", "value");

        // 操作
        var result = service.Delete("key");

        // 断言
        await Assert.That(result).IsTrue();
        await Assert.That(service.Get("key")).IsNull();
    }

    [Test]
    public async Task Delete_NonExistentKey_ReturnsFalse()
    {
        // 安排
        var service = new KvStoreService();

        // 操作
        var result = service.Delete("nonexistent");

        // 断言
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Keys_ReturnsAllKeys()
    {
        // 安排
        var service = new KvStoreService();
        service.Set("key1", 1);
        service.Set("key2", 2);
        service.Set("key3", 3);

        // 操作
        var keys = service.Keys();

        // 断言
        await Assert.That(keys.Length).IsEqualTo(3);
        await Assert.That(keys).Contains("key1");
        await Assert.That(keys).Contains("key2");
        await Assert.That(keys).Contains("key3");
    }

    [Test]
    public async Task Keys_EmptyStore_ReturnsEmptyArray()
    {
        // 安排
        var service = new KvStoreService();

        // 操作
        var keys = service.Keys();

        // 断言
        await Assert.That(keys.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Clear_RemovesAllKeys()
    {
        // 安排
        var service = new KvStoreService();
        service.Set("key1", 1);
        service.Set("key2", 2);

        // 操作
        service.Clear();

        // 断言
        await Assert.That(service.Keys().Length).IsEqualTo(0);
    }

    [Test]
    public async Task Set_OverwriteExistingKey()
    {
        // 安排
        var service = new KvStoreService();
        service.Set("key", "old");

        // 操作
        service.Set("key", "new");

        // 断言
        await Assert.That(service.Get("key")).IsEqualTo("\"new\"");
    }

    [Test]
    public async Task ServiceShutdown_PersistsDataToDisk()
    {
        // 安排
        var filePath = Path.Combine(Path.GetTempPath(), $"kvstore_test_{Guid.NewGuid():N}.json");
        try
        {
            var service = new KvStoreService(filePath);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
            service.Set("persisted", "value");

            // 操作
            await service.ServiceShutdown(CancellationToken.None);

            // 断言
            await Assert.That(File.Exists(filePath)).IsTrue();
            var json = await File.ReadAllTextAsync(filePath);
            using var doc = JsonDocument.Parse(json);
            await Assert.That(doc.RootElement.GetProperty("persisted").GetString()).IsEqualTo("\"value\"");
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Test]
    public async Task ServiceStartup_LoadsPersistedData()
    {
        // 安排
        var filePath = Path.Combine(Path.GetTempPath(), $"kvstore_test_{Guid.NewGuid():N}.json");
        try
        {
            // 第一次实例：写入数据并关闭
            var service1 = new KvStoreService(filePath);
            await service1.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
            service1.Set("loaded", "data");
            await service1.ServiceShutdown(CancellationToken.None);

            // 第二次实例：从同一文件加载
            var service2 = new KvStoreService(filePath);

            // 操作
            await service2.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

            // 断言
            await Assert.That(service2.Get("loaded")).IsEqualTo("\"data\"");
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Test]
    public async Task ServiceStartup_NoFilePath_DoesNotThrow()
    {
        // 安排
        var service = new KvStoreService();

        // 操作与断言
        await Assert.That(() => service.ServiceStartup(new ApplicationOptions(), CancellationToken.None))
            .ThrowsNothing();
    }

    [Test]
    public async Task ServiceShutdown_NoFilePath_DoesNotThrow()
    {
        // 安排
        var service = new KvStoreService();

        // 操作与断言
        await Assert.That(() => service.ServiceShutdown(CancellationToken.None)).ThrowsNothing();
    }
}
