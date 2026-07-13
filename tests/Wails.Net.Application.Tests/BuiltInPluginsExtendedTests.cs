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
/// 内置插件（Process、AppInfo、Path、OsInfo、GlobalShortcut、Clipboard、Log、Store、Updater、Notification）
/// 的扩展单元测试（TUnit）。
/// </summary>
[NotInParallel]
public sealed class BuiltInPluginsExtendedTests
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
    // ProcessPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task ProcessPlugin_Name_ReturnsProcess()
    {
        // 安排
        var plugin = new ProcessPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("process");
    }

    [Test]
    public async Task ProcessPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new ProcessPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task ProcessPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new ProcessPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 process.exit、process.restart、process.relaunch 三个命令
        await Assert.That(context.Commands.Count).IsEqualTo(3);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("process.exit")).IsTrue();
        await Assert.That(names.Contains("process.restart")).IsTrue();
        await Assert.That(names.Contains("process.relaunch")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // AppInfoPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task AppInfoPlugin_Name_ReturnsApp()
    {
        // 安排
        var plugin = new AppInfoPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("app");
    }

    [Test]
    public async Task AppInfoPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new AppInfoPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task AppInfoPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new AppInfoPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 4 个 app.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(4);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("app.getName")).IsTrue();
        await Assert.That(names.Contains("app.getVersion")).IsTrue();
        await Assert.That(names.Contains("app.getDescription")).IsTrue();
        await Assert.That(names.Contains("app.getTauriVersion")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // PathPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task PathPlugin_Name_ReturnsPath()
    {
        // 安排
        var plugin = new PathPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("path");
    }

    [Test]
    public async Task PathPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new PathPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task PathPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new PathPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册多个 path.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(11);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("path.appDataDir")).IsTrue();
        await Assert.That(names.Contains("path.appConfigDir")).IsTrue();
        await Assert.That(names.Contains("path.homeDir")).IsTrue();
        await Assert.That(names.Contains("path.tempDir")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // OsInfoPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task OsInfoPlugin_Name_ReturnsOs()
    {
        // 安排
        var plugin = new OsInfoPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("os");
    }

    [Test]
    public async Task OsInfoPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new OsInfoPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task OsInfoPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new OsInfoPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 13 个命令（6 个 os.* + 7 个 system.*，system.* 与前端 wails.system.* API 一致）
        await Assert.That(context.Commands.Count).IsEqualTo(13);
        var names = context.Commands.GetCommandNames().ToList();
        // os.* 命令（历史名，向后兼容）
        await Assert.That(names.Contains("os.platform")).IsTrue();
        await Assert.That(names.Contains("os.hostname")).IsTrue();
        await Assert.That(names.Contains("os.arch")).IsTrue();
        await Assert.That(names.Contains("os.locale")).IsTrue();
        await Assert.That(names.Contains("os.version")).IsTrue();
        await Assert.That(names.Contains("os.type")).IsTrue();
        // system.* 命令（与前端 wails.system.* API 一致）
        await Assert.That(names.Contains("system.platform")).IsTrue();
        await Assert.That(names.Contains("system.hostname")).IsTrue();
        await Assert.That(names.Contains("system.arch")).IsTrue();
        await Assert.That(names.Contains("system.locale")).IsTrue();
        await Assert.That(names.Contains("system.version")).IsTrue();
        await Assert.That(names.Contains("system.type")).IsTrue();
        await Assert.That(names.Contains("system.timezone")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // GlobalShortcutPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task GlobalShortcutPlugin_Name_ReturnsGlobalShortcut()
    {
        // 安排
        var plugin = new GlobalShortcutPlugin();

        // 操作与断言：注意源码中 Name 返回 "globalshortcut"（无连字符）
        await Assert.That(plugin.Name).IsEqualTo("globalshortcut");
    }

    [Test]
    public async Task GlobalShortcutPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new GlobalShortcutPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task GlobalShortcutPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new GlobalShortcutPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 4 个 globalshortcut.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(4);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("globalshortcut.register")).IsTrue();
        await Assert.That(names.Contains("globalshortcut.unregister")).IsTrue();
        await Assert.That(names.Contains("globalshortcut.unregisterAll")).IsTrue();
        await Assert.That(names.Contains("globalshortcut.isRegistered")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // ClipboardPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task ClipboardPlugin_Name_ReturnsClipboard()
    {
        // 安排
        var plugin = new ClipboardPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("clipboard");
    }

    [Test]
    public async Task ClipboardPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new ClipboardPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task ClipboardPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new ClipboardPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 7 个 clipboard.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(7);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("clipboard.getText")).IsTrue();
        await Assert.That(names.Contains("clipboard.setText")).IsTrue();
        await Assert.That(names.Contains("clipboard.getHTML")).IsTrue();
        await Assert.That(names.Contains("clipboard.setHTML")).IsTrue();
        await Assert.That(names.Contains("clipboard.getImage")).IsTrue();
        await Assert.That(names.Contains("clipboard.setImage")).IsTrue();
        await Assert.That(names.Contains("clipboard.clear")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // LogPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task LogPlugin_Name_ReturnsLog()
    {
        // 安排
        var plugin = new LogPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("log");
    }

    [Test]
    public async Task LogPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new LogPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task LogPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new LogPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 7 个 log.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(7);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("log.debug")).IsTrue();
        await Assert.That(names.Contains("log.info")).IsTrue();
        await Assert.That(names.Contains("log.warn")).IsTrue();
        await Assert.That(names.Contains("log.error")).IsTrue();
        await Assert.That(names.Contains("log.trace")).IsTrue();
        await Assert.That(names.Contains("log.log")).IsTrue();
        await Assert.That(names.Contains("log.logStructured")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // StorePlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task StorePlugin_Name_ReturnsStore()
    {
        // 安排
        var plugin = new StorePlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("store");
    }

    [Test]
    public async Task StorePlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new StorePlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task StorePlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new StorePlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 6 个 store.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(6);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("store.get")).IsTrue();
        await Assert.That(names.Contains("store.set")).IsTrue();
        await Assert.That(names.Contains("store.delete")).IsTrue();
        await Assert.That(names.Contains("store.keys")).IsTrue();
        await Assert.That(names.Contains("store.clear")).IsTrue();
        await Assert.That(names.Contains("store.watch")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // UpdaterPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task UpdaterPlugin_Name_ReturnsUpdater()
    {
        // 安排
        var plugin = new UpdaterPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("updater");
    }

    [Test]
    public async Task UpdaterPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new UpdaterPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task UpdaterPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new UpdaterPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 4 个 updater.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(4);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("updater.check")).IsTrue();
        await Assert.That(names.Contains("updater.download")).IsTrue();
        await Assert.That(names.Contains("updater.install")).IsTrue();
        await Assert.That(names.Contains("updater.checkAndDownload")).IsTrue();
    }

    // ---------------------------------------------------------------------
    // NotificationPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task NotificationPlugin_Name_ReturnsNotification()
    {
        // 安排
        var plugin = new NotificationPlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("notification");
    }

    [Test]
    public async Task NotificationPlugin_ConfigureServices_DoesNotThrow()
    {
        // 安排
        var plugin = new NotificationPlugin();
        var services = new ServiceCollection();

        // 操作与断言
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    [Test]
    public async Task NotificationPlugin_Configure_RegistersCommands()
    {
        // 安排
        var plugin = new NotificationPlugin();
        var context = CreatePluginContext();

        // 操作
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();

        // 断言：应注册 5 个 notification.* 命令
        await Assert.That(context.Commands.Count).IsEqualTo(5);
        var names = context.Commands.GetCommandNames().ToList();
        await Assert.That(names.Contains("notification.show")).IsTrue();
        await Assert.That(names.Contains("notification.requestPermission")).IsTrue();
        await Assert.That(names.Contains("notification.isPermissionGranted")).IsTrue();
        await Assert.That(names.Contains("notification.cancel")).IsTrue();
        await Assert.That(names.Contains("notification.showWithId")).IsTrue();
    }
}
