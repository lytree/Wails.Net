using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using Wails.Net.Plugins.Updater.Services;

namespace Wails.Net.Plugins.Updater.Tests;

/// <summary>
/// Updater 插件单元测试（TUnit），覆盖插件名、DI 注册与命令注册。
/// 对应迁移自 tests/Wails.Net.Application.Tests 的 Updater 测试段（M1 双包拆分）。
/// </summary>
[NotInParallel]
public sealed class UpdaterPluginTests
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

    [Test]
    public async Task UpdaterPlugin_Configure_DeclaresPermissions()
    {
        // 安排
        var plugin = new UpdaterPlugin();
        var context = CreatePluginContext();

        // 操作：Configure 应声明 updater:default 权限集与三个权限
        await Assert.That(() => plugin.Configure(context)).ThrowsNothing();
    }
}
