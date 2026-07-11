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
/// 新增内置插件（Dialog、Sqlite、WebSocket、Positioner）的单元测试（TUnit）。
/// 对应 Wails v3 / Tauri v2 功能对齐阶段新增的插件。
/// </summary>
[NotInParallel]
public sealed class NewPluginsTests
{
    /// <summary>
    /// 创建模拟的 <see cref="IPluginContext"/>，提供 CommandRegistry、配置和日志工厂。
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

    // ---------------------------------------------------------------------
    // DialogPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task DialogPlugin_Name_ReturnsDialog()
    {
        // 安排
        var plugin = new DialogPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("dialog");
    }

    [Test]
    public async Task DialogPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new DialogPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task DialogPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new DialogPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 7 个 dialog.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(7);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("dialog.message")).IsTrue();
        await Assert.That(names.Contains("dialog.warning")).IsTrue();
        await Assert.That(names.Contains("dialog.error")).IsTrue();
        await Assert.That(names.Contains("dialog.question")).IsTrue();
        await Assert.That(names.Contains("dialog.openFile")).IsTrue();
        await Assert.That(names.Contains("dialog.saveFile")).IsTrue();
        await Assert.That(names.Contains("dialog.openMultipleFiles")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // SqlPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task SqlPlugin_Name_ReturnsSqlite()
    {
        // 安排
        var plugin = new SqlPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("sqlite");
    }

    [Test]
    public async Task SqlPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new SqlPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task SqlPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new SqlPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 10 个 sqlite.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(10);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("sqlite.execute")).IsTrue();
        await Assert.That(names.Contains("sqlite.query")).IsTrue();
        await Assert.That(names.Contains("sqlite.scalar")).IsTrue();
        await Assert.That(names.Contains("sqlite.createTable")).IsTrue();
        await Assert.That(names.Contains("sqlite.dropTable")).IsTrue();
        await Assert.That(names.Contains("sqlite.getTables")).IsTrue();
        await Assert.That(names.Contains("sqlite.insert")).IsTrue();
        await Assert.That(names.Contains("sqlite.update")).IsTrue();
        await Assert.That(names.Contains("sqlite.delete")).IsTrue();
        await Assert.That(names.Contains("sqlite.select")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // WebSocketPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task WebSocketPlugin_Name_ReturnsWebsocket()
    {
        // 安排
        var plugin = new WebSocketPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("websocket");
    }

    [Test]
    public async Task WebSocketPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new WebSocketPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task WebSocketPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new WebSocketPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 5 个 websocket.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(5);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("websocket.connect")).IsTrue();
        await Assert.That(names.Contains("websocket.send")).IsTrue();
        await Assert.That(names.Contains("websocket.close")).IsTrue();
        await Assert.That(names.Contains("websocket.getState")).IsTrue();
        await Assert.That(names.Contains("websocket.sendBinary")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // PositionerPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task PositionerPlugin_Name_ReturnsPositioner()
    {
        // 安排
        var plugin = new PositionerPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("positioner");
    }

    [Test]
    public async Task PositionerPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new PositionerPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task PositionerPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new PositionerPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 5 个 positioner.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(5);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("positioner.move")).IsTrue();
        await Assert.That(names.Contains("positioner.center")).IsTrue();
        await Assert.That(names.Contains("positioner.moveRelativeTo")).IsTrue();
        await Assert.That(names.Contains("positioner.moveToCursor")).IsTrue();
        await Assert.That(names.Contains("positioner.getPosition")).IsTrue();
    }
}
