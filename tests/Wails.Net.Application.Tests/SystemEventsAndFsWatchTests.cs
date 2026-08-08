using Wails.Net.Plugins.FsWatch;
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
    // RuntimeGenerator JS API 补全测试（P0-D：前端运行时已迁往 npm 包 @wails-net/runtime，
    // 故此处的 window.wails 字符串断言失去意义，相关测试已移除。
    // 等价覆盖见 packages/wails-net-runtime/src/api/*.ts 单测与 vitest 套件。）
    // ---------------------------------------------------------------------
}
