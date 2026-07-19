using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.BuiltIn;
using Wails.Net.Runtime.Js;

namespace Wails.Net.Application.Tests;

/// <summary>
/// 系统事件与 FsWatchPlugin 的单元测试（TUnit）。
/// 对应 Wails/Tauri 功能对齐阶段新增的系统事件发射器和文件监听插件。
/// </summary>
[NotInParallel]
public sealed class SystemEventsAndFsWatchTests
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
        => CommandTestHelper.Invoke(registry, name, args);

    /// <summary>
    /// 通过命令注册表调用返回 bool 的命令。
    /// </summary>
    private static bool InvokeBool(CommandRegistry registry, string name, params object?[] args)
    {
        return InvokeCommand(registry, name, args) is bool b && b;
    }

    /// <summary>
    /// 通过命令注册表调用返回 int 的命令。
    /// </summary>
    private static int InvokeInt(CommandRegistry registry, string name, params object?[] args)
    {
        return InvokeCommand(registry, name, args) is int i ? i : 0;
    }

    /// <summary>
    /// 通过命令注册表调用返回 int[] 的命令。
    /// </summary>
    private static int[]? InvokeIntArray(CommandRegistry registry, string name, params object?[] args)
    {
        return InvokeCommand(registry, name, args) as int[];
    }

    // ---------------------------------------------------------------------
    // FsWatchPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task FsWatchPlugin_Name_ReturnsFsWatch()
    {
        var plugin = new FsWatchPlugin();
        await Assert.That(plugin.Name).IsEqualTo("fs-watch");
    }

    [Test]
    public async Task FsWatchPlugin_ConfigureServices_DoesNotThrow()
    {
        var plugin = new FsWatchPlugin();
        var services = new ServiceCollection();
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task FsWatchPlugin_Configure_RegistersCommands()
    {
        var plugin = new FsWatchPlugin();
        var context = CreatePluginContext();

        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        await Assert.That(context.Commands.Count).IsEqualTo(5);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("fswatch.watch")).IsTrue();
        await Assert.That(names.Contains("fswatch.unwatch")).IsTrue();
        await Assert.That(names.Contains("fswatch.unwatchAll")).IsTrue();
        await Assert.That(names.Contains("fswatch.listWatches")).IsTrue();
        await Assert.That(names.Contains("fswatch.isWatching")).IsTrue();
    }

    [Test]
    public async Task FsWatchPlugin_Watch_InvalidPath_ReturnsZero()
    {
        var plugin = new FsWatchPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var id = InvokeInt(context.Commands, "fswatch.watch", "/nonexistent/path/123", false, null);
        await Assert.That(id).IsEqualTo(0);
    }

    [Test]
    public async Task FsWatchPlugin_Watch_EmptyPath_ReturnsZero()
    {
        var plugin = new FsWatchPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var id = InvokeInt(context.Commands, "fswatch.watch", "", false, null);
        await Assert.That(id).IsEqualTo(0);
    }

    [Test]
    public async Task FsWatchPlugin_Watch_ValidPath_ReturnsId()
    {
        var plugin = new FsWatchPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var tempDir = Path.Combine(Path.GetTempPath(), $"fswatch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var id = InvokeInt(context.Commands, "fswatch.watch", tempDir, false, null);
            await Assert.That(id).IsGreaterThan(0);

            // 验证 isWatching 返回 true
            var isWatching = InvokeBool(context.Commands, "fswatch.isWatching", id);
            await Assert.That(isWatching).IsTrue();

            // 清理
            InvokeCommand(context.Commands, "fswatch.unwatch", id);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            plugin.Dispose();
        }
    }

    [Test]
    public async Task FsWatchPlugin_Unwatch_StopsWatching()
    {
        var plugin = new FsWatchPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var tempDir = Path.Combine(Path.GetTempPath(), $"fswatch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var id = InvokeInt(context.Commands, "fswatch.watch", tempDir, false, null);
            await Assert.That(id).IsGreaterThan(0);

            // 停止监听
            InvokeCommand(context.Commands, "fswatch.unwatch", id);

            // 验证 isWatching 返回 false
            var isWatching = InvokeBool(context.Commands, "fswatch.isWatching", id);
            await Assert.That(isWatching).IsFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            plugin.Dispose();
        }
    }

    [Test]
    public async Task FsWatchPlugin_ListWatches_ReturnsAllIds()
    {
        var plugin = new FsWatchPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var tempDir1 = Path.Combine(Path.GetTempPath(), $"fswatch_test1_{Guid.NewGuid():N}");
        var tempDir2 = Path.Combine(Path.GetTempPath(), $"fswatch_test2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir1);
        Directory.CreateDirectory(tempDir2);
        try
        {
            var id1 = InvokeInt(context.Commands, "fswatch.watch", tempDir1, false, null);
            var id2 = InvokeInt(context.Commands, "fswatch.watch", tempDir2, false, null);

            var watches = InvokeIntArray(context.Commands, "fswatch.listWatches");
            await Assert.That(watches).IsNotNull();
            if (watches is not null)
            {
                await Assert.That(watches.Length).IsEqualTo(2);
                await Assert.That(watches.Contains(id1)).IsTrue();
                await Assert.That(watches.Contains(id2)).IsTrue();
            }

            InvokeCommand(context.Commands, "fswatch.unwatchAll");
        }
        finally
        {
            if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, true);
            if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, true);
            plugin.Dispose();
        }
    }

    [Test]
    public async Task FsWatchPlugin_UnwatchAll_ClearsAllWatches()
    {
        var plugin = new FsWatchPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var tempDir1 = Path.Combine(Path.GetTempPath(), $"fswatch_test_{Guid.NewGuid():N}");
        var tempDir2 = Path.Combine(Path.GetTempPath(), $"fswatch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir1);
        Directory.CreateDirectory(tempDir2);
        try
        {
            InvokeInt(context.Commands, "fswatch.watch", tempDir1, false, null);
            InvokeInt(context.Commands, "fswatch.watch", tempDir2, false, null);

            // 停止所有监听
            InvokeCommand(context.Commands, "fswatch.unwatchAll");

            var watches = InvokeIntArray(context.Commands, "fswatch.listWatches");
            await Assert.That(watches).IsNotNull();
            if (watches is not null)
            {
                await Assert.That(watches.Length).IsEqualTo(0);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, true);
            if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, true);
            plugin.Dispose();
        }
    }

    [Test]
    public async Task FsWatchPlugin_Watch_WithExtensions_ParsesCorrectly()
    {
        var plugin = new FsWatchPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var tempDir = Path.Combine(Path.GetTempPath(), $"fswatch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var extensionsJson = JsonSerializer.Serialize(new[] { ".txt", ".json" });
            var id = InvokeInt(context.Commands, "fswatch.watch", tempDir, true, extensionsJson);
            await Assert.That(id).IsGreaterThan(0);

            InvokeCommand(context.Commands, "fswatch.unwatch", id);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            plugin.Dispose();
        }
    }

    [Test]
    public async Task FsWatchPlugin_Watch_InvalidExtensionsJson_DoesNotThrow()
    {
        var plugin = new FsWatchPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var tempDir = Path.Combine(Path.GetTempPath(), $"fswatch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // 无效的 JSON 不应导致异常
            var id = InvokeInt(context.Commands, "fswatch.watch", tempDir, false, "invalid json");
            await Assert.That(id).IsGreaterThan(0);

            InvokeCommand(context.Commands, "fswatch.unwatch", id);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            plugin.Dispose();
        }
    }

    [Test]
    public async Task FsWatchPlugin_Watch_RemainsActiveAfterFileCreation()
    {
        // 注意：此测试验证监听器在文件创建后仍然活跃，不验证事件触发回调。
        // FsWatchPlugin 的事件机制是内部的，无公共事件订阅 API，
        // 事件触发的端到端验证需要通过前端集成测试完成。
        var plugin = new FsWatchPlugin();
        var context = CreatePluginContext();
        plugin.Configure(context);

        var tempDir = Path.Combine(Path.GetTempPath(), $"fswatch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var id = InvokeInt(context.Commands, "fswatch.watch", tempDir, false, null);
            await Assert.That(id).IsGreaterThan(0);

            // 创建文件触发文件系统事件
            var testFile = Path.Combine(tempDir, $"test_{Guid.NewGuid():N}.txt");
            File.WriteAllText(testFile, "test content");

            // 等待 FileSystemWatcher 事件传播
            await Task.Delay(300);

            // 验证监听器仍然活跃
            var isWatching = InvokeBool(context.Commands, "fswatch.isWatching", id);
            await Assert.That(isWatching).IsTrue();

            InvokeCommand(context.Commands, "fswatch.unwatch", id);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            plugin.Dispose();
        }
    }

    // ---------------------------------------------------------------------
    // RuntimeGenerator JS API 补全测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsFsWatchNamespace()
    {
        var options = new RuntimeOptions { Platform = "windows", IsDebug = false, IsServerMode = false };
        var api = RuntimeGenerator.GenerateApi(options);

        await Assert.That(api.Contains("fswatch:")).IsTrue();
        await Assert.That(api.Contains("fswatch.watch")).IsTrue();
        await Assert.That(api.Contains("fswatch.unwatch")).IsTrue();
        await Assert.That(api.Contains("fswatch.unwatchAll")).IsTrue();
        await Assert.That(api.Contains("fswatch.listWatches")).IsTrue();
        await Assert.That(api.Contains("fswatch.isWatching")).IsTrue();
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsSystemNamespace()
    {
        var options = new RuntimeOptions { Platform = "windows", IsDebug = false, IsServerMode = false };
        var api = RuntimeGenerator.GenerateApi(options);

        await Assert.That(api.Contains("system.")).IsTrue();
        await Assert.That(api.Contains("system.platform")).IsTrue();
        await Assert.That(api.Contains("system.arch")).IsTrue();
        await Assert.That(api.Contains("system.hostname")).IsTrue();
        await Assert.That(api.Contains("system.version")).IsTrue();
        await Assert.That(api.Contains("system.type")).IsTrue();
        await Assert.That(api.Contains("system.locale")).IsTrue();
        await Assert.That(api.Contains("system.timezone")).IsTrue();
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsPowerNamespace()
    {
        var options = new RuntimeOptions { Platform = "windows", IsDebug = false, IsServerMode = false };
        var api = RuntimeGenerator.GenerateApi(options);

        await Assert.That(api.Contains("power.")).IsTrue();
        await Assert.That(api.Contains("power.requestWakeLock")).IsTrue();
        await Assert.That(api.Contains("power.releaseWakeLock")).IsTrue();
        await Assert.That(api.Contains("power.isWakeLockHeld")).IsTrue();
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsProcessNamespace()
    {
        var options = new RuntimeOptions { Platform = "windows", IsDebug = false, IsServerMode = false };
        var api = RuntimeGenerator.GenerateApi(options);

        await Assert.That(api.Contains("process.exit")).IsTrue();
        await Assert.That(api.Contains("process.restart")).IsTrue();
        await Assert.That(api.Contains("process.getPid")).IsTrue();
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsFsNamespace()
    {
        var options = new RuntimeOptions { Platform = "windows", IsDebug = false, IsServerMode = false };
        var api = RuntimeGenerator.GenerateApi(options);

        await Assert.That(api.Contains("fs.readTextFile")).IsTrue();
        await Assert.That(api.Contains("fs.writeTextFile")).IsTrue();
        await Assert.That(api.Contains("fs.exists")).IsTrue();
        await Assert.That(api.Contains("fs.mkdir")).IsTrue();
        await Assert.That(api.Contains("fs.remove")).IsTrue();
        await Assert.That(api.Contains("fs.rename")).IsTrue();
        await Assert.That(api.Contains("fs.copy")).IsTrue();
        await Assert.That(api.Contains("fs.readDir")).IsTrue();
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsShellNamespace()
    {
        var options = new RuntimeOptions { Platform = "windows", IsDebug = false, IsServerMode = false };
        var api = RuntimeGenerator.GenerateApi(options);

        await Assert.That(api.Contains("shell.execute")).IsTrue();
        await Assert.That(api.Contains("shell.open")).IsTrue();
        await Assert.That(api.Contains("shell.openUrl")).IsTrue();
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsNotificationNamespace()
    {
        var options = new RuntimeOptions { Platform = "windows", IsDebug = false, IsServerMode = false };
        var api = RuntimeGenerator.GenerateApi(options);

        await Assert.That(api.Contains("notification.show")).IsTrue();
        await Assert.That(api.Contains("notification.requestPermission")).IsTrue();
        await Assert.That(api.Contains("notification.hasPermission")).IsTrue();
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsStoreNamespace()
    {
        var options = new RuntimeOptions { Platform = "windows", IsDebug = false, IsServerMode = false };
        var api = RuntimeGenerator.GenerateApi(options);

        await Assert.That(api.Contains("store.get")).IsTrue();
        await Assert.That(api.Contains("store.set")).IsTrue();
        await Assert.That(api.Contains("store.delete")).IsTrue();
        await Assert.That(api.Contains("store.keys")).IsTrue();
        await Assert.That(api.Contains("store.clear")).IsTrue();
        await Assert.That(api.Contains("store.has")).IsTrue();
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_ContainsLogNamespace()
    {
        var options = new RuntimeOptions { Platform = "windows", IsDebug = false, IsServerMode = false };
        var api = RuntimeGenerator.GenerateApi(options);

        await Assert.That(api.Contains("log.debug")).IsTrue();
        await Assert.That(api.Contains("log.info")).IsTrue();
        await Assert.That(api.Contains("log.warn")).IsTrue();
        await Assert.That(api.Contains("log.error")).IsTrue();
        await Assert.That(api.Contains("log.trace")).IsTrue();
    }

    [Test]
    public async Task RuntimeGenerator_GenerateApi_AllNamespacesPresent()
    {
        var options = new RuntimeOptions { Platform = "windows", IsDebug = false, IsServerMode = false };
        var api = RuntimeGenerator.GenerateApi(options);

        // 验证所有命名空间都存在
        await Assert.That(api.Contains("fswatch:")).IsTrue();
        await Assert.That(api.Contains("system.")).IsTrue();
        await Assert.That(api.Contains("power.")).IsTrue();
        await Assert.That(api.Contains("process.")).IsTrue();
        await Assert.That(api.Contains("fs.")).IsTrue();
        await Assert.That(api.Contains("shell.")).IsTrue();
        await Assert.That(api.Contains("notification.")).IsTrue();
        await Assert.That(api.Contains("store.")).IsTrue();
        await Assert.That(api.Contains("log.")).IsTrue();
    }
}
