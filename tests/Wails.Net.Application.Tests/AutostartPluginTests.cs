using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Plugins.BuiltIn;

namespace Wails.Net.Application.Tests;

/// <summary>
/// AutostartPlugin 的单元测试（TUnit）。
/// 验证插件名称、命令注册、自启动启用/禁用/查询功能。
/// </summary>
[NotInParallel]
public sealed class AutostartPluginTests
{
    // ---------------------------------------------------------------------
    // 插件基本信息
    // ---------------------------------------------------------------------

    [Test]
    public async Task AutostartPlugin_Name_ReturnsAutostart()
    {
        var plugin = new AutostartPlugin();
        string name = plugin.Name;
        await Assert.That(name).IsEqualTo("autostart");
    }

    [Test]
    public async Task AutostartPlugin_ConfigureServices_DoesNotThrow()
    {
        var plugin = new AutostartPlugin();
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        await Assert.That(() => plugin.ConfigureServices(services)).ThrowsNothing();
    }

    // ---------------------------------------------------------------------
    // GetAppName
    // ---------------------------------------------------------------------

    [Test]
    public async Task AutostartPlugin_GetAppName_ReturnsNonEmptyString()
    {
        var appName = AutostartPlugin.GetAppName();
        await Assert.That(string.IsNullOrEmpty(appName)).IsFalse();
    }

    // ---------------------------------------------------------------------
    // Enable/Disable/IsEnabled 往返测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task AutostartPlugin_EnableDisable_RoundTrip()
    {
        // 使用唯一的测试应用名避免与其他测试冲突
        var appName = $"wails_test_{Guid.NewGuid():N}";

        try
        {
            // 启用
            bool enabled = AutostartPlugin.EnableAutostart(appName);
            await Assert.That(enabled).IsTrue();

            // 查询应返回已启用
            bool isEnabled = AutostartPlugin.IsAutostartEnabled(appName);
            await Assert.That(isEnabled).IsTrue();

            // 禁用
            bool disabled = AutostartPlugin.DisableAutostart(appName);
            await Assert.That(disabled).IsTrue();

            // 查询应返回未启用
            bool isEnabledAfter = AutostartPlugin.IsAutostartEnabled(appName);
            await Assert.That(isEnabledAfter).IsFalse();
        }
        finally
        {
            // 清理
            AutostartPlugin.DisableAutostart(appName);
        }
    }

    [Test]
    public async Task AutostartPlugin_DisableNotEnabled_ReturnsTrue()
    {
        // 禁用一个未启用的应用名应返回 true（幂等）
        var appName = $"wails_nonexist_{Guid.NewGuid():N}";
        bool result = AutostartPlugin.DisableAutostart(appName);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task AutostartPlugin_IsEnabled_NotEnabled_ReturnsFalse()
    {
        var appName = $"wails_notexist_{Guid.NewGuid():N}";
        bool result = AutostartPlugin.IsAutostartEnabled(appName);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task AutostartPlugin_EnableThenDisable_CleansUp()
    {
        var appName = $"wails_cleanup_{Guid.NewGuid():N}";

        try
        {
            AutostartPlugin.EnableAutostart(appName);
            AutostartPlugin.DisableAutostart(appName);

            // 再次禁用应该仍然成功（幂等）
            bool result = AutostartPlugin.DisableAutostart(appName);
            await Assert.That(result).IsTrue();
        }
        finally
        {
            AutostartPlugin.DisableAutostart(appName);
        }
    }
}
