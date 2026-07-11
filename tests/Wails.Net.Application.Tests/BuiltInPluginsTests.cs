using System.Reflection;
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
/// 内置插件（DeepLink、WindowState、PowerManagement）的单元测试（TUnit）。
/// </summary>
[NotInParallel]
public sealed class BuiltInPluginsTests
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
    // DeepLinkPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task Name_ReturnsDeepLink()
    {
        // 安排
        var plugin = new DeepLinkPlugin(new[] { "myapp" });

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("deep-link");
    }

    [Test]
    public async Task Constructor_WithValidSchemes_StoresSchemes()
    {
        // 安排与操作
        var plugin = new DeepLinkPlugin(new[] { "myapp", "test" });

        // 断言：通过反射读取私有字段验证 schemes 已存储
        var schemesField = typeof(DeepLinkPlugin).GetField("_schemes",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var schemes = (List<string>)schemesField!.GetValue(plugin)!;

        await Assert.That(schemes.Count).IsEqualTo(2);
        await Assert.That(schemes.Contains("myapp")).IsTrue();
        await Assert.That(schemes.Contains("test")).IsTrue();
    }

    [Test]
    public async Task Constructor_WithEmptySchemes_DoesNotThrow()
    {
        // 操作与断言
        await Assert.That(() => new DeepLinkPlugin(Array.Empty<string>())).ThrowsNothing();
    }

    [Test]
    public async Task RegisterScheme_OnWindows_DoesNotThrow()
    {
        // 跳过非 Windows 平台
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 安排
        var plugin = new DeepLinkPlugin(Array.Empty<string>());
        const string scheme = "wailsnet-test-register-scheme";

        try
        {
            // 操作与断言
            await Assert.That(() => plugin.RegisterScheme(scheme)).ThrowsNothing();
        }
        finally
        {
            // 清理注册表
            plugin.UnregisterScheme(scheme);
        }
    }

    [Test]
    public async Task UnregisterScheme_OnWindows_DoesNotThrow()
    {
        // 跳过非 Windows 平台
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 安排
        var plugin = new DeepLinkPlugin(Array.Empty<string>());
        const string scheme = "wailsnet-test-unregister-scheme";
        plugin.RegisterScheme(scheme);

        // 操作与断言
        await Assert.That(() => plugin.UnregisterScheme(scheme)).ThrowsNothing();
    }

    [Test]
    public async Task GetCurrentUrl_NoUrlInArgs_ReturnsNull()
    {
        // 安排
        var plugin = new DeepLinkPlugin(Array.Empty<string>());

        // 操作
        var url = plugin.GetCurrentUrl();

        // 断言：在测试运行环境中命令行参数不含 URL
        await Assert.That(url).IsNull();
    }

    // ---------------------------------------------------------------------
    // WindowStatePlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task Name_ReturnsWindowState()
    {
        // 安排
        var plugin = new WindowStatePlugin();

        // 操作与断言
        await Assert.That(plugin.Name).IsEqualTo("window-state");
    }

    [Test]
    public async Task Constructor_DefaultFileName_ReturnsValidPath()
    {
        // 安排与操作
        var plugin = new WindowStatePlugin();

        // 断言：默认文件名为 window-state.json，路径非空且包含该文件名
        await Assert.That(string.IsNullOrEmpty(plugin.StateFilePath)).IsFalse();
        await Assert.That(plugin.StateFilePath.EndsWith("window-state.json")).IsTrue();
    }

    [Test]
    public async Task StateFilePath_ContainsFileName()
    {
        // 安排与操作
        var plugin = new WindowStatePlugin("custom-state.json");

        // 断言
        await Assert.That(plugin.StateFilePath.Contains("custom-state.json")).IsTrue();
    }

    [Test]
    public async Task AutoRestore_DefaultTrue()
    {
        // 安排与操作
        var plugin = new WindowStatePlugin();

        // 断言
        await Assert.That(plugin.AutoRestore).IsTrue();
    }

    [Test]
    public async Task ClearState_NoFile_DoesNotThrow()
    {
        // 安排：使用唯一文件名确保状态文件不存在
        var plugin = new WindowStatePlugin("test-clear-no-file.json");
        plugin.ClearState(); // 确保文件不存在

        // 操作与断言
        await Assert.That(() => plugin.ClearState()).ThrowsNothing();
    }

    [Test]
    public async Task SaveState_NoApp_DoesNotThrow()
    {
        // 安排：使用唯一文件名避免与其它测试冲突
        var plugin = new WindowStatePlugin("test-save-no-app.json");

        try
        {
            // 操作与断言：Application.Get() 返回 null 时应直接返回
            await Assert.That(() => plugin.SaveState()).ThrowsNothing();
        }
        finally
        {
            // 清理可能生成的文件
            plugin.ClearState();
        }
    }

    [Test]
    public async Task RestoreState_NoFile_DoesNotThrow()
    {
        // 安排：使用唯一文件名确保状态文件不存在
        var plugin = new WindowStatePlugin("test-restore-no-file.json");
        plugin.ClearState(); // 确保文件不存在

        // 操作与断言：文件不存在时应直接返回
        await Assert.That(() => plugin.RestoreState()).ThrowsNothing();
    }

    // ---------------------------------------------------------------------
    // PowerManagementPlugin
    // ---------------------------------------------------------------------

    [Test]
    public async Task Name_ReturnsPowerManagement()
    {
        // 安排与操作
        var plugin = new PowerManagementPlugin();

        // 断言
        await Assert.That(plugin.Name).IsEqualTo("power-management");
    }

    [Test]
    public async Task RequestWakeLock_OnWindows_ReturnsTrue()
    {
        // 跳过非 Windows 平台
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 安排
        var plugin = new PowerManagementPlugin();

        try
        {
            // 操作
            var result = plugin.RequestWakeLock();

            // 断言
            await Assert.That(result).IsTrue();
        }
        finally
        {
            // 释放唤醒锁
            plugin.Dispose();
        }
    }

    [Test]
    public async Task ReleaseWakeLock_OnWindows_ReturnsTrue()
    {
        // 跳过非 Windows 平台
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 安排
        var plugin = new PowerManagementPlugin();

        try
        {
            // 先请求唤醒锁再释放
            plugin.RequestWakeLock();

            // 操作
            var result = plugin.ReleaseWakeLock();

            // 断言
            await Assert.That(result).IsTrue();
        }
        finally
        {
            plugin.Dispose();
        }
    }

    [Test]
    public async Task OnSuspend_DoesNotThrow()
    {
        // 安排
        var plugin = new PowerManagementPlugin();

        try
        {
            // 操作与断言
            await Assert.That(() => plugin.OnSuspend()).ThrowsNothing();
        }
        finally
        {
            plugin.Dispose();
        }
    }

    [Test]
    public async Task OnResume_DoesNotThrow()
    {
        // 安排
        var plugin = new PowerManagementPlugin();

        try
        {
            // 操作与断言
            await Assert.That(() => plugin.OnResume()).ThrowsNothing();
        }
        finally
        {
            plugin.Dispose();
        }
    }

    [Test]
    public async Task Dispose_WithoutWakeLock_DoesNotThrow()
    {
        // 安排
        var plugin = new PowerManagementPlugin();

        // 操作与断言：未持有唤醒锁时 Dispose 不应抛出
        await Assert.That(() => plugin.Dispose()).ThrowsNothing();
    }

    [Test]
    public async Task Suspend_Event_TriggersOnSuspend()
    {
        // 安排
        var plugin = new PowerManagementPlugin();
        var triggered = false;
        plugin.Suspend += () => triggered = true;

        try
        {
            // 操作
            plugin.OnSuspend();

            // 断言
            await Assert.That(triggered).IsTrue();
        }
        finally
        {
            plugin.Dispose();
        }
    }

    [Test]
    public async Task Resume_Event_TriggersOnResume()
    {
        // 安排
        var plugin = new PowerManagementPlugin();
        var triggered = false;
        plugin.Resume += () => triggered = true;

        try
        {
            // 操作
            plugin.OnResume();

            // 断言
            await Assert.That(triggered).IsTrue();
        }
        finally
        {
            plugin.Dispose();
        }
    }
}
