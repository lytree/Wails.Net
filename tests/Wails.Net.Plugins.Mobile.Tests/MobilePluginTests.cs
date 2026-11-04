using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.Mobile;
using Wails.Net.Application.Security;
using Wails.Net.Plugins.Mobile;

namespace Wails.Net.Plugins.Mobile.Tests;

/// <summary>
/// Mobile 插件族测试。
/// <para>
/// 本仓库与 Tauri v2 一致，按能力拆分为独立插件（<c>barcode-scanner</c>、<c>biometric</c>、<c>camera</c>、
/// <c>geolocation</c>、<c>haptics</c>、<c>nfc</c>、<c>permissions</c>），不存在聚合入口 <c>MobilePlugin</c>；
/// 因此逐插件校验插件 ID（kebab-case）、命令前缀、安全能力声明、权限声明与 DI 降级注册。
/// </para>
/// </summary>
[NotInParallel]
public sealed class MobilePluginTests
{
    /// <summary>
    /// 插件与其命令的对应表（命令名采用 <c>{插件 ID}.&lt;action&gt;</c> 格式）。
    /// </summary>
    private static readonly (IPlugin Plugin, string[] Commands)[] s_pluginCommands =
    [
        (new BarcodeScannerPlugin(), ["barcode-scanner.scan", "barcode-scanner.cancel"]),
        (new BiometricPlugin(), ["biometric.checkAvailability", "biometric.authenticate"]),
        (new CameraPlugin(), ["camera.checkAvailability", "camera.capture", "camera.cancel"]),
        (new GeolocationPlugin(),
            ["geolocation.checkAvailability", "geolocation.getCurrentPosition", "geolocation.watchPosition", "geolocation.clearWatch"]),
        (new HapticsPlugin(), ["haptics.vibrate", "haptics.cancel", "haptics.notification"]),
        (new NfcPlugin(), ["nfc.read", "nfc.write", "nfc.cancel"]),
        (new PermissionsPlugin(), ["permissions.check", "permissions.request"]),
    ];

    [Test]
    public async Task PluginIds_MatchKebabCasePluginIds()
    {
        foreach (var (plugin, commands) in s_pluginCommands)
        {
            await Assert.That(plugin.Name).IsEqualTo(commands[0].Split('.')[0]);
        }
    }

    [Test]
    public async Task Configure_RegistersCommandsWithPluginPrefix()
    {
        foreach (var (plugin, expectedCommands) in s_pluginCommands)
        {
            var (context, _) = CreatePluginContext();

            plugin.Configure(context);

            await Assert.That(context.Commands.Count).IsEqualTo(expectedCommands.Length);
            foreach (var name in expectedCommands)
            {
                var entry = context.Commands.Find(name);
                await Assert.That(entry).IsNotNull();
                await Assert.That(entry!.Name.StartsWith(plugin.Name + ".", StringComparison.Ordinal)).IsTrue();
            }
        }
    }

    [Test]
    public async Task Configure_EachCommandRequiresMobileDefaultCapability()
    {
        foreach (var (plugin, expectedCommands) in s_pluginCommands)
        {
            var (context, _) = CreatePluginContext();

            plugin.Configure(context);

            foreach (var name in expectedCommands)
            {
                var entry = context.Commands.Find(name);

                await Assert.That(entry).IsNotNull();
                await Assert.That(entry!.Security).IsNotNull();
                await Assert.That(entry.Security!.RequiredCapabilities).Contains("mobile:allow-default");
            }
        }
    }

    [Test]
    public async Task Configure_DeclaresOwnPermissionSetAndPermissions()
    {
        var cases = new (IPlugin Plugin, string[] PermissionSets, string[] Permissions)[]
        {
            (new BarcodeScannerPlugin(),
                ["mobile:default", "barcode-scanner:default"],
                ["mobile:allow-default", "barcode-scanner:allow-scan", "barcode-scanner:allow-cancel"]),
            (new BiometricPlugin(),
                ["mobile:default", "biometric:default"],
                ["mobile:allow-default", "biometric:allow-check-availability", "biometric:allow-authenticate"]),
            // Camera 插件未重复声明 mobile:default（由其余插件贡献），只声明自身权限集。
            (new CameraPlugin(),
                ["camera:default"],
                ["camera:allow-check-availability", "camera:allow-capture", "camera:allow-cancel"]),
            (new GeolocationPlugin(),
                ["mobile:default", "geolocation:default"],
                ["mobile:allow-default", "geolocation:allow-check-availability", "geolocation:allow-get-position", "geolocation:allow-watch-position"]),
            (new HapticsPlugin(),
                ["mobile:default", "haptics:default"],
                ["mobile:allow-default", "haptics:allow-vibrate", "haptics:allow-cancel", "haptics:allow-notify"]),
            (new NfcPlugin(),
                ["mobile:default", "nfc:default"],
                ["mobile:allow-default", "nfc:allow-read", "nfc:allow-write", "nfc:allow-cancel"]),
            (new PermissionsPlugin(),
                ["mobile:default", "permissions:default"],
                ["mobile:allow-default", "permissions:allow-check", "permissions:allow-request"]),
        };

        foreach (var (plugin, expectedSets, expectedPermissions) in cases)
        {
            var (context, permissions) = CreatePluginContext();

            plugin.Configure(context);

            foreach (var set in expectedSets)
            {
                await Assert.That(permissions.PermissionSets).ContainsKey(set);
            }

            foreach (var permission in expectedPermissions)
            {
                await Assert.That(permissions.DeclaredPermissions).Contains(permission);
            }
        }
    }

    [Test]
    public async Task ConfigureServices_RegistersPlatformFallbackImplementation()
    {
        var cases = new (IPlugin Plugin, Type ServiceType)[]
        {
            (new BarcodeScannerPlugin(), typeof(IPlatformBarcodeScanner)),
            (new BiometricPlugin(), typeof(IPlatformBiometric)),
            (new CameraPlugin(), typeof(IPlatformCamera)),
            (new GeolocationPlugin(), typeof(IPlatformGeolocation)),
            (new HapticsPlugin(), typeof(IPlatformHaptics)),
            (new NfcPlugin(), typeof(IPlatformNfc)),
            (new PermissionsPlugin(), typeof(IPlatformPermissions)),
        };

        foreach (var (plugin, serviceType) in cases)
        {
            var services = new ServiceCollection();

            plugin.ConfigureServices(services);

            await Assert.That(services.Any(d => d.ServiceType == serviceType)).IsTrue();
        }
    }

    /// <summary>
    /// 创建模拟的 <see cref="IPluginContext"/>，使用真实的 <see cref="CommandRegistry"/> 与记录型权限注册器。
    /// </summary>
    private static (IPluginContext Context, FakePermissionRegistrar Permissions) CreatePluginContext()
    {
        var services = new ServiceCollection();
        var commands = new CommandRegistry();
        var configuration = new ConfigurationBuilder().Build();
        var loggerFactory = LoggerFactory.Create(_ => { });
        var permissions = new FakePermissionRegistrar();

        var context = new FakePluginContext(services, commands, configuration, loggerFactory, permissions);
        return (context, permissions);
    }

    /// <summary>
    /// 简单的 <see cref="IPluginContext"/> 实现，用于测试。
    /// </summary>
    private sealed class FakePluginContext : IPluginContext
    {
        public FakePluginContext(
            IServiceCollection services,
            CommandRegistry commands,
            IConfiguration configuration,
            ILoggerFactory loggerFactory,
            IPermissionRegistrar permissions)
        {
            Services = services;
            Commands = commands;
            Configuration = configuration;
            LoggerFactory = loggerFactory;
            Permissions = permissions;
        }

        public IServiceCollection Services { get; }

        public CommandRegistry Commands { get; }

        public IConfiguration Configuration { get; }

        public ILoggerFactory LoggerFactory { get; }

        public IPermissionRegistrar Permissions { get; }
    }

    /// <summary>
    /// 简单的 <see cref="IPermissionRegistrar"/> 实现，记录所有权限集与权限声明。
    /// </summary>
    private sealed class FakePermissionRegistrar : IPermissionRegistrar
    {
        public Dictionary<string, string> PermissionSets { get; } = new();

        public List<string> DeclaredPermissions { get; } = new();

        public void RegisterPermissionSet(string identifier, string description, params string[] permissions)
        {
            PermissionSets[identifier] = description;
        }

        public void DeclarePermission(string identifier, string description)
        {
            DeclaredPermissions.Add(identifier);
        }

        public void BindScope(string permissionId, IScope scope)
        {
            // 测试中无需验证 BindScope
        }
    }
}
