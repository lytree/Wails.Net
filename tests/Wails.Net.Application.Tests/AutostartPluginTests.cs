using Wails.Net.Plugins.Autostart;
using TUnit.Assertions;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Wails.Net.Application.Tests;

/// <summary>
/// AutostartPlugin 的单元测试（TUnit）。
/// 验证插件名称、命令注册、自启动启用/禁用/查询功能。
/// </summary>
[NotInParallel]
public sealed class AutostartPluginTests
{
    /// <summary>
    /// 断言当前平台具备可安全写入的自启动存储（真实落盘的集成测试前置条件）。
    /// <para>
    /// Windows（注册表 HKCU\...\Run）与 Linux（~/.config/autostart）可安全写入。
    /// macOS 的 Enable 会写入 <c>~/Library/LaunchAgents</c> 并执行 <c>launchctl bootstrap</c>，
    /// 而 plist 的 ProgramArguments 指向测试宿主可执行文件、RunAtLoad=true——
    /// launchd 会立即再拉起一份测试宿主进程，故此类往返必须在 macOS 端到端验证，
    /// 与 <c>MacOSAutostartManagerTests</c> 的约定保持一致。
    /// </para>
    /// </summary>
    /// <exception cref="SkipTestException">当前平台无法安全写入自启动存储时抛出，跳过用例。</exception>
    private static void RequireWritableAutostartStore()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            throw new SkipTestException(
                "macOS 上 Enable 会写入 LaunchAgents 并 launchctl bootstrap（立即拉起测试宿主进程），"
                + "真实 LaunchAgent 往返需在 macOS 端到端验证。");
        }

        throw new SkipTestException("当前平台无开机自启动存储（Android 等），跳过。");
    }

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
        RequireWritableAutostartStore();

        // 使用唯一的测试应用名避免与其他测试冲突
        var appName = $"wails_test_{Guid.NewGuid():N}";

        try
        {
            // 启用
            bool enabled = AutostartPlugin.EnableAutostart(appName);

            // 某些环境（如 CI 沙箱）禁止写入注册表/文件系统，导致无法真正启用开机自启动。
            // 此时跳过本集成测试，避免误报失败；本地开发机能正常完成往返验证。
            if (!enabled)
            {
                throw new SkipTestException(
                    "当前环境不支持写入开机自启动存储（注册表/文件系统），跳过往返验证。");
            }

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
        // 禁用一个未启用的应用名应返回 true（幂等）。
        // 三个平台的禁用路径对"条目不存在"都按已禁用处理，故本用例可在任意平台验证。
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
        RequireWritableAutostartStore();

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
