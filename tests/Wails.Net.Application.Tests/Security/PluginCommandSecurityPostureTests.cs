using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// 内置插件命令的安全姿态 drift 测试（SEC-H-003 收尾，2026-11-14）。
/// <para>
/// 契约：每条内置插件命令要么声明所需 capability（<see cref="CommandSecurity.Require"/>），
/// 要么显式标记为公开命令（<see cref="CommandSecurity.Public"/>）。
/// 未声明者在 Strict + DenyByDefault 下会被运行时拒绝，且在
/// <see cref="Application.Manifests.ManifestGenerator.FindUncoveredCapabilities"/> 中报告未覆盖（B2 flip）。
/// </para>
/// <para>
/// 覆盖范围：<c>tests/Wails.Net.Application.Tests</c> 已引用的全部插件项目。
/// 未引用的插件（<c>Wails.Net.Plugins.Keychain</c> / <c>Wails.Net.Plugins.Updater</c>）由各自的
/// 插件测试项目与 <c>wails-net check</c> 覆盖，后续补引用时一并纳入本测试。
/// </para>
/// <para>
/// 说明：本测试直接调用 <see cref="IPlugin.Configure"/> 收集注册结果，不启动 Host，
/// 因此不依赖平台实现；插件内部的服务解析（如 Dialog/Tray 的原生句柄）只在运行时命令体内发生。
/// </para>
/// </summary>
[NotInParallel]
public sealed class PluginCommandSecurityPostureTests
{
    /// <summary>
    /// 构造全部已引用插件实例。使用显式列表而不用反射（AGENTS.md §3.4）。
    /// </summary>
    private static IPlugin[] CreatePlugins() =>
    [
        new Wails.Net.Plugins.AppInfo.AppInfoPlugin(),
        new Wails.Net.Plugins.Application.ApplicationPlugin(),
        new Wails.Net.Plugins.Autostart.AutostartPlugin(),
        new Wails.Net.Plugins.Cli.CliPlugin(),
        new Wails.Net.Plugins.Clipboard.ClipboardPlugin(),
        new Wails.Net.Plugins.Cookie.CookiePlugin(),
        new Wails.Net.Plugins.DeepLink.DeepLinkPlugin(["wails-test"]),
        new Wails.Net.Plugins.Dialog.DialogPlugin(),
        new Wails.Net.Plugins.DpiScale.DpiScalePlugin(),
        new Wails.Net.Plugins.FileAssociation.FileAssociationPlugin(["wailstest"]),
        new Wails.Net.Plugins.FileSystem.FileSystemPlugin(),
        new Wails.Net.Plugins.FsWatch.FsWatchPlugin(),
        new Wails.Net.Plugins.GlobalShortcut.GlobalShortcutPlugin(),
        new Wails.Net.Plugins.Http.HttpPlugin(),
        new Wails.Net.Plugins.Localhost.LocalhostPlugin(),
        new Wails.Net.Plugins.Localization.LocalizationPlugin(),
        new Wails.Net.Plugins.Log.LogPlugin(),
        new Wails.Net.Plugins.Menu.MenuPlugin(),
        new Wails.Net.Plugins.Mobile.BarcodeScannerPlugin(),
        new Wails.Net.Plugins.Mobile.BiometricPlugin(),
        new Wails.Net.Plugins.Mobile.CameraPlugin(),
        new Wails.Net.Plugins.Mobile.GeolocationPlugin(),
        new Wails.Net.Plugins.Mobile.HapticsPlugin(),
        new Wails.Net.Plugins.Mobile.NfcPlugin(),
        new Wails.Net.Plugins.Mobile.PermissionsPlugin(),
        new Wails.Net.Plugins.Notification.NotificationPlugin(),
        new Wails.Net.Plugins.Opener.OpenerPlugin(),
        new Wails.Net.Plugins.OsInfo.OsInfoPlugin(),
        new Wails.Net.Plugins.Path.PathPlugin(),
        new Wails.Net.Plugins.PersistedScope.PersistedScopePlugin(),
        new Wails.Net.Plugins.Positioner.PositionerPlugin(),
        new Wails.Net.Plugins.PowerManagement.PowerManagementPlugin(),
        new Wails.Net.Plugins.Process.ProcessPlugin(),
        new Wails.Net.Plugins.Screen.ScreenPlugin(),
        new Wails.Net.Plugins.Shell.ShellPlugin(),
        new Wails.Net.Plugins.Sql.SqlPlugin(),
        new Wails.Net.Plugins.Store.StorePlugin(),
        new Wails.Net.Plugins.Stronghold.StrongholdPlugin(),
        new Wails.Net.Plugins.Tray.TrayPlugin(),
        new Wails.Net.Plugins.Upload.UploadPlugin(),
        new Wails.Net.Plugins.WebSocket.WebSocketPlugin(),
        new Wails.Net.Plugins.Window.WindowPlugin(),
        new Wails.Net.Plugins.WindowState.WindowStatePlugin(),
        new Wails.Net.Plugins.Windows.WindowsPlugin(),
    ];

    /// <summary>
    /// 全部内置插件命令必须显式声明安全姿态。
    /// </summary>
    [Test]
    public async Task AllBuiltinPluginCommands_DeclareExplicitSecurityPosture()
    {
        var registry = new CommandRegistry();
        var services = new ServiceCollection();
        var context = new PluginContext(
            services,
            registry,
            new ConfigurationBuilder().Build(),
            NullLoggerFactory.Instance);

        foreach (var plugin in CreatePlugins())
        {
            plugin.ConfigureServices(services);
            plugin.Configure(context);
        }

        var commandNames = registry.GetCommandNames().ToList();
        await Assert.That(commandNames).IsNotEmpty();

        var undeclared = new List<string>();
        foreach (var name in commandNames)
        {
            var entry = registry.Find(name);
            if (entry is null)
            {
                continue;
            }

            // 合格姿态：显式 Public，或声明了至少一个所需 capability。
            if (entry.Security is { IsPublic: true })
            {
                continue;
            }

            if (entry.RequiredCapabilities.Count > 0)
            {
                continue;
            }

            undeclared.Add(name);
        }

        await Assert.That(undeclared)
            .IsEmpty()
            .Because($"以下内置命令未显式声明安全姿态（{commandNames.Count} 条命令中）：{string.Join(", ", undeclared)}");
    }

    /// <summary>
    /// B2 flip 交叉验证：在空 capability 清单下，未声明的命令必须被
    /// <see cref="Application.Manifests.ManifestGenerator.FindUncoveredCapabilities"/> 报告为未覆盖，
    /// 而不是因为"未声明"被静默跳过。
    /// </summary>
    [Test]
    public async Task AllBuiltinPluginCommands_AreNotSilentlyExemptedFromCoverageCheck()
    {
        var registry = new CommandRegistry();
        var services = new ServiceCollection();
        var context = new PluginContext(
            services,
            registry,
            new ConfigurationBuilder().Build(),
            NullLoggerFactory.Instance);

        foreach (var plugin in CreatePlugins())
        {
            plugin.ConfigureServices(services);
            plugin.Configure(context);
        }

        var uncovered = global::Wails.Net.Application.Manifests.ManifestGenerator.FindUncoveredCapabilities(registry, []);
        var undeclared = uncovered
            .Where(m => m.Contains("未显式声明安全姿态", StringComparison.Ordinal))
            .ToList();

        await Assert.That(undeclared)
            .IsEmpty()
            .Because($"存在未显式声明安全姿态的命令：{string.Join(" | ", undeclared)}");
    }
}
