using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 新增内置插件（Stronghold、PersistedScope、Localhost）的单元测试（TUnit）。
/// 对应 Tauri v2 功能对齐阶段新增的插件。
/// </summary>
[NotInParallel]
public sealed class NewPlugins3Tests
{
    /// <summary>
    /// 创建模拟的 <see cref="IPluginContext"/>。
    /// </summary>
    private static IPluginContext CreatePluginContext()
    {
        var services = new ServiceCollection();
        var commands = new CommandRegistry();
        var config = new ConfigurationBuilder().Build();
        var loggerFactory = LoggerFactory.Create(_ => { });

        var context = Substitute.For<IPluginContext>();
        context.Services.Returns(services);
        context.Commands.Returns(commands);
        context.Configuration.Returns(config);
        context.LoggerFactory.Returns(loggerFactory);
        return context;
    }

    /// <summary>
    /// 通过命令注册表调用命令。
    /// </summary>
    private static object? InvokeCommand(CommandRegistry registry, string name, params object?[] args)
    {
        var entry = registry.Find(name);
        if (entry is null)
        {
            throw new InvalidOperationException($"命令未找到: {name}");
        }
        return entry.Method.Invoke(entry.Instance, args);
    }

    /// <summary>
    /// 通过命令注册表调用返回 bool 的命令。
    /// </summary>
    private static bool InvokeBool(CommandRegistry registry, string name, params object?[] args)
    {
        return InvokeCommand(registry, name, args) is bool b && b;
    }

    /// <summary>
    /// 通过命令注册表调用返回 string 的命令。
    /// </summary>
    private static string? InvokeString(CommandRegistry registry, string name, params object?[] args)
    {
        return InvokeCommand(registry, name, args) as string;
    }

    /// <summary>
    /// 通过命令注册表调用返回 string[] 的命令。
    /// </summary>
    private static string[]? InvokeStringArray(CommandRegistry registry, string name, params object?[] args)
    {
        return InvokeCommand(registry, name, args) as string[];
    }

    // ---------------------------------------------------------------------
    // StrongholdPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task StrongholdPlugin_Name_ReturnsStronghold()
    {
        var plugin = new StrongholdPlugin();
        await Assert.That(plugin.Name).IsEqualTo("stronghold");
    }

    [Test]
    public async Task StrongholdPlugin_ConfigureServices_DoesNotThrow()
    {
        var plugin = new StrongholdPlugin();
        var services = new ServiceCollection();
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task StrongholdPlugin_Configure_RegistersCommands()
    {
        var plugin = new StrongholdPlugin();
        var context = CreatePluginContext();

        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        await Assert.That(context.Commands.Count).IsEqualTo(8);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("stronghold.unlock")).IsTrue();
        await Assert.That(names.Contains("stronghold.lock")).IsTrue();
        await Assert.That(names.Contains("stronghold.saveSecret")).IsTrue();
        await Assert.That(names.Contains("stronghold.getSecret")).IsTrue();
        await Assert.That(names.Contains("stronghold.deleteSecret")).IsTrue();
        await Assert.That(names.Contains("stronghold.listKeys")).IsTrue();
        await Assert.That(names.Contains("stronghold.isUnlocked")).IsTrue();
        await Assert.That(names.Contains("stronghold.changePassword")).IsTrue();
    }

    [Test]
    public async Task StrongholdPlugin_Unlock_EmptyPassword_ReturnsFalse()
    {
        var plugin = new StrongholdPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var vaultPath = Path.Combine(Path.GetTempPath(), $"stronghold_test_{Guid.NewGuid():N}.json");
        try
        {
            var result = InvokeBool(context.Commands, "stronghold.unlock", "", vaultPath);
            await Assert.That(result).IsFalse();
        }
        finally
        {
            if (File.Exists(vaultPath)) File.Delete(vaultPath);
        }
    }

    [Test]
    public async Task StrongholdPlugin_Unlock_CreatesVault_And_IsUnlocked()
    {
        var plugin = new StrongholdPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var vaultPath = Path.Combine(Path.GetTempPath(), $"stronghold_test_{Guid.NewGuid():N}.json");
        try
        {
            // 解锁（创建新金库）
            var result = InvokeBool(context.Commands, "stronghold.unlock", "TestPassword123", vaultPath);
            await Assert.That(result).IsTrue();

            // 验证已解锁
            var isUnlocked = InvokeBool(context.Commands, "stronghold.isUnlocked", vaultPath);
            await Assert.That(isUnlocked).IsTrue();

            // 验证文件已创建
            await Assert.That(File.Exists(vaultPath)).IsTrue();
        }
        finally
        {
            InvokeCommand(context.Commands, "stronghold.lock", vaultPath);
            if (File.Exists(vaultPath)) File.Delete(vaultPath);
        }
    }

    [Test]
    public async Task StrongholdPlugin_SaveAndGetSecret_RoundTrip()
    {
        var plugin = new StrongholdPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var vaultPath = Path.Combine(Path.GetTempPath(), $"stronghold_test_{Guid.NewGuid():N}.json");
        try
        {
            InvokeBool(context.Commands, "stronghold.unlock", "MyPassword", vaultPath);

            // 保存秘密
            var saved = InvokeBool(context.Commands, "stronghold.saveSecret", "api_key", "secret-value-123", vaultPath);
            await Assert.That(saved).IsTrue();

            // 读取秘密
            var value = InvokeString(context.Commands, "stronghold.getSecret", "api_key", vaultPath);
            await Assert.That(value).IsEqualTo("secret-value-123");
        }
        finally
        {
            InvokeCommand(context.Commands, "stronghold.lock", vaultPath);
            if (File.Exists(vaultPath)) File.Delete(vaultPath);
        }
    }

    [Test]
    public async Task StrongholdPlugin_ListKeys_ReturnsAllKeys()
    {
        var plugin = new StrongholdPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var vaultPath = Path.Combine(Path.GetTempPath(), $"stronghold_test_{Guid.NewGuid():N}.json");
        try
        {
            InvokeBool(context.Commands, "stronghold.unlock", "Password", vaultPath);
            InvokeBool(context.Commands, "stronghold.saveSecret", "key1", "val1", vaultPath);
            InvokeBool(context.Commands, "stronghold.saveSecret", "key2", "val2", vaultPath);

            var keys = InvokeStringArray(context.Commands, "stronghold.listKeys", vaultPath);
            await Assert.That(keys).IsNotNull();
            if (keys is not null)
            {
                await Assert.That(keys.Length).IsEqualTo(2);
                await Assert.That(keys.Contains("key1")).IsTrue();
                await Assert.That(keys.Contains("key2")).IsTrue();
            }
        }
        finally
        {
            InvokeCommand(context.Commands, "stronghold.lock", vaultPath);
            if (File.Exists(vaultPath)) File.Delete(vaultPath);
        }
    }

    [Test]
    public async Task StrongholdPlugin_DeleteSecret_RemovesKey()
    {
        var plugin = new StrongholdPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var vaultPath = Path.Combine(Path.GetTempPath(), $"stronghold_test_{Guid.NewGuid():N}.json");
        try
        {
            InvokeBool(context.Commands, "stronghold.unlock", "Password", vaultPath);
            InvokeBool(context.Commands, "stronghold.saveSecret", "toDelete", "value", vaultPath);

            var deleted = InvokeBool(context.Commands, "stronghold.deleteSecret", "toDelete", vaultPath);
            await Assert.That(deleted).IsTrue();

            // 再次删除应返回 false
            var deletedAgain = InvokeBool(context.Commands, "stronghold.deleteSecret", "toDelete", vaultPath);
            await Assert.That(deletedAgain).IsFalse();
        }
        finally
        {
            InvokeCommand(context.Commands, "stronghold.lock", vaultPath);
            if (File.Exists(vaultPath)) File.Delete(vaultPath);
        }
    }

    [Test]
    public async Task StrongholdPlugin_Lock_ClearsState()
    {
        var plugin = new StrongholdPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var vaultPath = Path.Combine(Path.GetTempPath(), $"stronghold_test_{Guid.NewGuid():N}.json");
        try
        {
            InvokeBool(context.Commands, "stronghold.unlock", "Password", vaultPath);
            InvokeBool(context.Commands, "stronghold.saveSecret", "key", "val", vaultPath);

            // 锁定
            InvokeCommand(context.Commands, "stronghold.lock", vaultPath);

            // 锁定后 isUnlocked 应为 false
            var isUnlocked = InvokeBool(context.Commands, "stronghold.isUnlocked", vaultPath);
            await Assert.That(isUnlocked).IsFalse();

            // 锁定后 getSecret 应返回 null
            var secret = InvokeString(context.Commands, "stronghold.getSecret", "key", vaultPath);
            await Assert.That(secret).IsNull();
        }
        finally
        {
            if (File.Exists(vaultPath)) File.Delete(vaultPath);
        }
    }

    [Test]
    public async Task StrongholdPlugin_WrongPassword_FailsToUnlock()
    {
        var plugin = new StrongholdPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var vaultPath = Path.Combine(Path.GetTempPath(), $"stronghold_test_{Guid.NewGuid():N}.json");
        try
        {
            // 创建金库
            InvokeBool(context.Commands, "stronghold.unlock", "CorrectPassword", vaultPath);
            InvokeBool(context.Commands, "stronghold.saveSecret", "key", "secret", vaultPath);
            InvokeCommand(context.Commands, "stronghold.lock", vaultPath);

            // 用错误密码解锁应失败
            var result = InvokeBool(context.Commands, "stronghold.unlock", "WrongPassword", vaultPath);
            await Assert.That(result).IsFalse();
        }
        finally
        {
            if (File.Exists(vaultPath)) File.Delete(vaultPath);
        }
    }

    [Test]
    public async Task StrongholdPlugin_ChangePassword_SucceedsAndOldPasswordFails()
    {
        var plugin = new StrongholdPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var vaultPath = Path.Combine(Path.GetTempPath(), $"stronghold_test_{Guid.NewGuid():N}.json");
        try
        {
            InvokeBool(context.Commands, "stronghold.unlock", "OldPassword", vaultPath);
            InvokeBool(context.Commands, "stronghold.saveSecret", "key", "secret", vaultPath);

            // 修改密码
            var changed = InvokeBool(context.Commands, "stronghold.changePassword", "OldPassword", "NewPassword", vaultPath);
            await Assert.That(changed).IsTrue();

            // 用旧密码解锁应失败
            InvokeCommand(context.Commands, "stronghold.lock", vaultPath);
            var oldResult = InvokeBool(context.Commands, "stronghold.unlock", "OldPassword", vaultPath);
            await Assert.That(oldResult).IsFalse();

            // 用新密码解锁应成功
            var newResult = InvokeBool(context.Commands, "stronghold.unlock", "NewPassword", vaultPath);
            await Assert.That(newResult).IsTrue();

            // 数据应保留
            var secret = InvokeString(context.Commands, "stronghold.getSecret", "key", vaultPath);
            await Assert.That(secret).IsEqualTo("secret");
        }
        finally
        {
            InvokeCommand(context.Commands, "stronghold.lock", vaultPath);
            if (File.Exists(vaultPath)) File.Delete(vaultPath);
        }
    }

    [Test]
    public async Task StrongholdPlugin_GetSecret_WhenLocked_ReturnsNull()
    {
        var plugin = new StrongholdPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var vaultPath = Path.Combine(Path.GetTempPath(), $"stronghold_test_{Guid.NewGuid():N}.json");
        try
        {
            // 未解锁时 getSecret 应返回 null
            var result = InvokeString(context.Commands, "stronghold.getSecret", "anykey", vaultPath);
            await Assert.That(result).IsNull();
        }
        finally
        {
            if (File.Exists(vaultPath)) File.Delete(vaultPath);
        }
    }

    // ---------------------------------------------------------------------
    // PersistedScopePlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task PersistedScopePlugin_Name_ReturnsPersistedScope()
    {
        var plugin = new PersistedScopePlugin();
        await Assert.That(plugin.Name).IsEqualTo("persisted-scope");
    }

    [Test]
    public async Task PersistedScopePlugin_ConfigureServices_DoesNotThrow()
    {
        var plugin = new PersistedScopePlugin();
        var services = new ServiceCollection();
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task PersistedScopePlugin_Configure_RegistersCommands()
    {
        var plugin = new PersistedScopePlugin();
        var context = CreatePluginContext();

        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        await Assert.That(context.Commands.Count).IsEqualTo(7);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("scope.addPath")).IsTrue();
        await Assert.That(names.Contains("scope.removePath")).IsTrue();
        await Assert.That(names.Contains("scope.listPaths")).IsTrue();
        await Assert.That(names.Contains("scope.clear")).IsTrue();
        await Assert.That(names.Contains("scope.isAllowed")).IsTrue();
        await Assert.That(names.Contains("scope.save")).IsTrue();
        await Assert.That(names.Contains("scope.load")).IsTrue();
    }

    [Test]
    public async Task PersistedScopePlugin_AddPath_And_ListPaths()
    {
        var plugin = new PersistedScopePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var scopePath = Path.Combine(Path.GetTempPath(), $"scope_test_{Guid.NewGuid():N}.json");
        var testPath = Path.Combine(Path.GetTempPath(), "test_dir");
        try
        {
            var added = InvokeBool(context.Commands, "scope.addPath", testPath, scopePath);
            await Assert.That(added).IsTrue();

            var paths = InvokeStringArray(context.Commands, "scope.listPaths", scopePath);
            await Assert.That(paths).IsNotNull();
            if (paths is not null)
            {
                await Assert.That(paths.Length).IsEqualTo(1);
                await Assert.That(paths[0].Contains("test_dir")).IsTrue();
            }
        }
        finally
        {
            if (File.Exists(scopePath)) File.Delete(scopePath);
        }
    }

    [Test]
    public async Task PersistedScopePlugin_AddPath_DuplicateReturnsFalse()
    {
        var plugin = new PersistedScopePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var scopePath = Path.Combine(Path.GetTempPath(), $"scope_test_{Guid.NewGuid():N}.json");
        var testPath = Path.Combine(Path.GetTempPath(), "dup_dir");
        try
        {
            // 第一次添加
            var added1 = InvokeBool(context.Commands, "scope.addPath", testPath, scopePath);
            await Assert.That(added1).IsTrue();

            // 重复添加同一路径应返回 false
            var added2 = InvokeBool(context.Commands, "scope.addPath", testPath, scopePath);
            await Assert.That(added2).IsFalse();
        }
        finally
        {
            if (File.Exists(scopePath)) File.Delete(scopePath);
        }
    }

    [Test]
    public async Task PersistedScopePlugin_AddPath_EmptyPath_ReturnsFalse()
    {
        var plugin = new PersistedScopePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var scopePath = Path.Combine(Path.GetTempPath(), $"scope_test_{Guid.NewGuid():N}.json");
        try
        {
            var result = InvokeBool(context.Commands, "scope.addPath", "", scopePath);
            await Assert.That(result).IsFalse();
        }
        finally
        {
            if (File.Exists(scopePath)) File.Delete(scopePath);
        }
    }

    [Test]
    public async Task PersistedScopePlugin_RemovePath_Succeeds()
    {
        var plugin = new PersistedScopePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var scopePath = Path.Combine(Path.GetTempPath(), $"scope_test_{Guid.NewGuid():N}.json");
        var testPath = Path.Combine(Path.GetTempPath(), "remove_dir");
        try
        {
            InvokeBool(context.Commands, "scope.addPath", testPath, scopePath);

            var removed = InvokeBool(context.Commands, "scope.removePath", testPath, scopePath);
            await Assert.That(removed).IsTrue();

            // 再次移除应返回 false
            var removedAgain = InvokeBool(context.Commands, "scope.removePath", testPath, scopePath);
            await Assert.That(removedAgain).IsFalse();
        }
        finally
        {
            if (File.Exists(scopePath)) File.Delete(scopePath);
        }
    }

    [Test]
    public async Task PersistedScopePlugin_IsAllowed_ExactMatch_ReturnsTrue()
    {
        var plugin = new PersistedScopePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var scopePath = Path.Combine(Path.GetTempPath(), $"scope_test_{Guid.NewGuid():N}.json");
        var testDir = Path.Combine(Path.GetTempPath(), "allowed_dir");
        try
        {
            InvokeBool(context.Commands, "scope.addPath", testDir, scopePath);

            // 精确匹配路径
            var allowed = InvokeBool(context.Commands, "scope.isAllowed", testDir, scopePath);
            await Assert.That(allowed).IsTrue();

            // 不在允许列表中的路径
            var otherPath = Path.Combine(Path.GetTempPath(), "not_allowed_dir");
            var notAllowed = InvokeBool(context.Commands, "scope.isAllowed", otherPath, scopePath);
            await Assert.That(notAllowed).IsFalse();
        }
        finally
        {
            if (File.Exists(scopePath)) File.Delete(scopePath);
        }
    }

    [Test]
    public async Task PersistedScopePlugin_IsAllowed_WildcardMatch_ReturnsTrue()
    {
        var plugin = new PersistedScopePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var scopePath = Path.Combine(Path.GetTempPath(), $"scope_test_{Guid.NewGuid():N}.json");
        try
        {
            // 添加通配符路径
            var wildcardPath = Path.Combine(Path.GetTempPath(), "wild*")!;
            InvokeBool(context.Commands, "scope.addPath", wildcardPath, scopePath);

            var testPath = Path.Combine(Path.GetTempPath(), "wild123");
            var allowed = InvokeBool(context.Commands, "scope.isAllowed", testPath, scopePath);
            await Assert.That(allowed).IsTrue();
        }
        finally
        {
            if (File.Exists(scopePath)) File.Delete(scopePath);
        }
    }

    [Test]
    public async Task PersistedScopePlugin_Clear_RemovesAllPaths()
    {
        var plugin = new PersistedScopePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var scopePath = Path.Combine(Path.GetTempPath(), $"scope_test_{Guid.NewGuid():N}.json");
        try
        {
            InvokeBool(context.Commands, "scope.addPath", Path.Combine(Path.GetTempPath(), "dir1"), scopePath);
            InvokeBool(context.Commands, "scope.addPath", Path.Combine(Path.GetTempPath(), "dir2"), scopePath);

            InvokeCommand(context.Commands, "scope.clear", scopePath);

            var paths = InvokeStringArray(context.Commands, "scope.listPaths", scopePath);
            await Assert.That(paths).IsNotNull();
            if (paths is not null)
            {
                await Assert.That(paths.Length).IsEqualTo(0);
            }
        }
        finally
        {
            if (File.Exists(scopePath)) File.Delete(scopePath);
        }
    }

    [Test]
    public async Task PersistedScopePlugin_Load_RestoresPaths()
    {
        var plugin = new PersistedScopePlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var scopePath = Path.Combine(Path.GetTempPath(), $"scope_test_{Guid.NewGuid():N}.json");
        var testPath = Path.Combine(Path.GetTempPath(), "persist_dir");
        try
        {
            // 添加路径并保存
            InvokeBool(context.Commands, "scope.addPath", testPath, scopePath);

            // 验证文件已保存
            await Assert.That(File.Exists(scopePath)).IsTrue();

            // 手动加载应返回 true
            var loaded = InvokeBool(context.Commands, "scope.load", scopePath);
            await Assert.That(loaded).IsTrue();

            // 加载后路径应存在
            var paths = InvokeStringArray(context.Commands, "scope.listPaths", scopePath);
            await Assert.That(paths).IsNotNull();
            if (paths is not null)
            {
                await Assert.That(paths.Length).IsEqualTo(1);
                await Assert.That(paths[0].Contains("persist_dir")).IsTrue();
            }
        }
        finally
        {
            if (File.Exists(scopePath)) File.Delete(scopePath);
        }
    }

    // ---------------------------------------------------------------------
    // LocalhostPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task LocalhostPlugin_Name_ReturnsLocalhost()
    {
        var plugin = new LocalhostPlugin();
        await Assert.That(plugin.Name).IsEqualTo("localhost");
    }

    [Test]
    public async Task LocalhostPlugin_ConfigureServices_DoesNotThrow()
    {
        var plugin = new LocalhostPlugin();
        var services = new ServiceCollection();
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task LocalhostPlugin_Configure_RegistersCommands()
    {
        var plugin = new LocalhostPlugin();
        var context = CreatePluginContext();

        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        await Assert.That(context.Commands.Count).IsEqualTo(8);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("localhost.start")).IsTrue();
        await Assert.That(names.Contains("localhost.stop")).IsTrue();
        await Assert.That(names.Contains("localhost.getUrl")).IsTrue();
        await Assert.That(names.Contains("localhost.isRunning")).IsTrue();
        await Assert.That(names.Contains("localhost.setRoot")).IsTrue();
        await Assert.That(names.Contains("localhost.addRoute")).IsTrue();
        await Assert.That(names.Contains("localhost.removeRoute")).IsTrue();
        await Assert.That(names.Contains("localhost.listRoutes")).IsTrue();
    }

    [Test]
    public async Task LocalhostPlugin_IsRunning_BeforeStart_ReturnsFalse()
    {
        var plugin = new LocalhostPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var running = InvokeBool(context.Commands, "localhost.isRunning", 18999);
        await Assert.That(running).IsFalse();
    }

    [Test]
    public async Task LocalhostPlugin_Start_And_Stop_RoundTrip()
    {
        var plugin = new LocalhostPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        const int testPort = 18923;
        try
        {
            // 启动
            var url = InvokeString(context.Commands, "localhost.start", testPort, (string?)null);
            await Assert.That(url).IsEqualTo($"http://localhost:{testPort}");

            // 验证运行
            var running = InvokeBool(context.Commands, "localhost.isRunning", testPort);
            await Assert.That(running).IsTrue();

            // 获取 URL
            var getUrl = InvokeString(context.Commands, "localhost.getUrl", testPort);
            await Assert.That(getUrl).IsEqualTo($"http://localhost:{testPort}");

            // 停止
            InvokeCommand(context.Commands, "localhost.stop", testPort);

            // 验证已停止
            var runningAfterStop = InvokeBool(context.Commands, "localhost.isRunning", testPort);
            await Assert.That(runningAfterStop).IsFalse();
        }
        finally
        {
            InvokeCommand(context.Commands, "localhost.stop", testPort);
        }
    }

    [Test]
    public async Task LocalhostPlugin_AddRoute_And_ListRoutes()
    {
        var plugin = new LocalhostPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        const int testPort = 18924;
        try
        {
            InvokeString(context.Commands, "localhost.start", testPort, (string?)null);

            // 添加路由
            InvokeCommand(context.Commands, "localhost.addRoute", testPort, "/api/data", "GET");
            InvokeCommand(context.Commands, "localhost.addRoute", testPort, "/api/users", "POST");

            // 列出路由
            var routes = InvokeStringArray(context.Commands, "localhost.listRoutes", testPort);
            await Assert.That(routes).IsNotNull();
            if (routes is not null)
            {
                await Assert.That(routes.Length).IsEqualTo(2);
                await Assert.That(routes.Contains("GET:/api/data")).IsTrue();
                await Assert.That(routes.Contains("POST:/api/users")).IsTrue();
            }
        }
        finally
        {
            InvokeCommand(context.Commands, "localhost.stop", testPort);
        }
    }

    [Test]
    public async Task LocalhostPlugin_RemoveRoute_RemovesSpecifiedRoute()
    {
        var plugin = new LocalhostPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        const int testPort = 18925;
        try
        {
            InvokeString(context.Commands, "localhost.start", testPort, (string?)null);
            InvokeCommand(context.Commands, "localhost.addRoute", testPort, "/api/test", "GET");

            // 移除路由
            InvokeCommand(context.Commands, "localhost.removeRoute", testPort, "/api/test");

            var routes = InvokeStringArray(context.Commands, "localhost.listRoutes", testPort);
            await Assert.That(routes).IsNotNull();
            if (routes is not null)
            {
                await Assert.That(routes.Length).IsEqualTo(0);
            }
        }
        finally
        {
            InvokeCommand(context.Commands, "localhost.stop", testPort);
        }
    }

    [Test]
    public async Task LocalhostPlugin_GetUrl_WhenNotRunning_ReturnsNull()
    {
        var plugin = new LocalhostPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var url = InvokeString(context.Commands, "localhost.getUrl", 18926);
        await Assert.That(url).IsNull();
    }
}
