using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Manifests;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Manifests;

/// <summary>
/// <see cref="ManifestGenerator"/> 的单元测试（TUnit）。
/// 对应审计 P1-2：命令/capability 清单生成与"敏感命令必须被 capability 覆盖"一致性校验；
/// 以及 C-1：插件清单（plugins.manifest.json）生成。
/// </summary>
[NotInParallel]
public sealed class ManifestGeneratorTests
{
    [Test]
    public async Task GenerateCommandsManifest_IncludesRegisteredCommands_WithAnonymousFlag()
    {
        // 安排：注册一条匿名命令和一条敏感命令
        var registry = new CommandRegistry();
        registry.MapCommand("app.greet", () => "Hello");
        registry.MapCommand("fs.readFile", (string path) => { /* noop */ }, "fs:allow-read");

        // 操作
        var manifest = ManifestGenerator.GenerateCommandsManifest(registry);

        // 断言
        await Assert.That(manifest.Version).IsEqualTo(ManifestGenerator.ManifestVersion);
        await Assert.That(manifest.Commands).Count().IsEqualTo(2);

        var greet = manifest.Commands.Single(c => c.Name == "app.greet");
        await Assert.That(greet.Anonymous).IsTrue();
        await Assert.That(greet.RequiredCapabilities).IsEmpty();

        var read = manifest.Commands.Single(c => c.Name == "fs.readFile");
        await Assert.That(read.Anonymous).IsFalse();
        await Assert.That(read.RequiredCapabilities).Contains("fs:allow-read");
    }

    [Test]
    public async Task GenerateCommandsManifestJson_IsValidJson_WithExpectedShape()
    {
        // 安排
        var registry = new CommandRegistry();
        registry.MapCommand("app.greet", () => "Hello");

        // 操作
        var json = ManifestGenerator.GenerateCommandsManifestJson(registry);

        // 断言：可解析且包含 version/commands 属性
        using var doc = JsonDocument.Parse(json);
        await Assert.That(doc.RootElement.TryGetProperty("version", out _)).IsTrue();
        await Assert.That(doc.RootElement.TryGetProperty("generatedAt", out _)).IsTrue();
        await Assert.That(doc.RootElement.TryGetProperty("commands", out var commands)).IsTrue();
        await Assert.That(commands.GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task GenerateCapabilitiesManifest_IncludesDeclarations()
    {
        // 安排
        var capabilities = new List<Capability>
        {
            new("main-capability")
            {
                Permissions = { "core:default", "fs:allow-read" },
                Windows = { "main" },
                Remote = { "https://*.example.com" },
                Platforms = { "windows", "linux" },
            },
        };

        // 操作
        var manifest = ManifestGenerator.GenerateCapabilitiesManifest(capabilities);

        // 断言
        await Assert.That(manifest.Capabilities).Count().IsEqualTo(1);
        var entry = manifest.Capabilities[0];
        await Assert.That(entry.Identifier).IsEqualTo("main-capability");
        await Assert.That(entry.Permissions).Contains("fs:allow-read");
        await Assert.That(entry.Windows).Contains("main");
        await Assert.That(entry.Platforms).Count().IsEqualTo(2);
    }

    [Test]
    public async Task FindUncoveredCapabilities_ReportsSensitiveCommandsWithoutCoverage()
    {
        // 安排：匿名命令（跳过）、已覆盖敏感命令、未覆盖敏感命令
        var registry = new CommandRegistry();
        registry.MapCommand("app.greet", () => "Hello");
        registry.MapCommand("fs.readFile", (string path) => { /* noop */ }, "fs:allow-read");
        registry.MapCommand("shell.open", (string url) => { /* noop */ }, "shell:allow-open");

        var capabilities = new List<Capability>
        {
            new("main") { Permissions = { "fs:allow-read" } },
        };

        // 操作
        var uncovered = ManifestGenerator.FindUncoveredCapabilities(registry, capabilities);

        // 断言：仅 shell.open 未覆盖
        await Assert.That(uncovered).Count().IsEqualTo(1);
        await Assert.That(uncovered[0]).Contains("shell.open");
        await Assert.That(uncovered[0]).Contains("shell:allow-open");
    }

    [Test]
    public async Task FindUncoveredCapabilities_DenyPrefixDoesNotCountAsCoverage()
    {
        // 安排：capability 只有显式拒绝（! 前缀）
        var registry = new CommandRegistry();
        registry.MapCommand("shell.open", (string url) => { /* noop */ }, "shell:allow-open");

        var capabilities = new List<Capability>
        {
            new("main") { Permissions = { "!shell:allow-open" } },
        };

        // 操作
        var uncovered = ManifestGenerator.FindUncoveredCapabilities(registry, capabilities);

        // 断言：显式拒绝不计入覆盖，仍判定为未覆盖
        await Assert.That(uncovered).Count().IsEqualTo(1);
    }

    [Test]
    public async Task FindUncoveredCapabilities_ReturnsEmpty_WhenAllCovered()
    {
        // 安排
        var registry = new CommandRegistry();
        registry.MapCommand("fs.readFile", (string path) => { /* noop */ }, "fs:allow-read");
        registry.MapCommand("app.greet", () => "Hello");

        var capabilities = new List<Capability>
        {
            new("main") { Permissions = { "fs:allow-read" } },
        };

        // 操作
        var uncovered = ManifestGenerator.FindUncoveredCapabilities(registry, capabilities);

        // 断言：全部覆盖（匿名命令不参与）
        await Assert.That(uncovered).IsEmpty();
    }

    [Test]
    public async Task TypeToTypeScript_MapsPrimitiveTypes()
    {
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(string))).IsEqualTo("string");
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(char))).IsEqualTo("string");
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(int))).IsEqualTo("number");
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(long))).IsEqualTo("number");
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(double))).IsEqualTo("number");
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(bool))).IsEqualTo("boolean");
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(Guid))).IsEqualTo("string");
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(DateTime))).IsEqualTo("string");
    }

    [Test]
    public async Task TypeToTypeScript_MapsEnumNullableAndCollections()
    {
        // 枚举 → string（前端以语义化字符串传参）
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(DayOfWeek))).IsEqualTo("string");

        // 可空值类型剥壳
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(int?))).IsEqualTo("number");

        // 数组与常见集合 → 数组
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(string[]))).IsEqualTo("string[]");
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(List<int>))).IsEqualTo("number[]");
        await Assert.That(ManifestGenerator.TypeToTypeScript(typeof(IReadOnlyList<string>))).IsEqualTo("string[]");
    }

    [Test]
    public async Task GeneratePluginsManifest_IncludesPlugins_SortedByName()
    {
        // 安排：两个测试插件（乱序注册）
        var plugins = new IPlugin[] { new SecondTestPlugin(), new FirstTestPlugin() };

        // 操作
        var manifest = ManifestGenerator.GeneratePluginsManifest(plugins);

        // 断言
        await Assert.That(manifest.Version).IsEqualTo(ManifestGenerator.ManifestVersion);
        await Assert.That(manifest.Plugins).Count().IsEqualTo(2);

        // 按名称序排列
        await Assert.That(manifest.Plugins[0].Name).IsEqualTo("first-test");
        await Assert.That(manifest.Plugins[1].Name).IsEqualTo("second-test");

        // 类型全名与名称一致（C-1：插件包元数据机器可读）
        var first = manifest.Plugins[0];
        await Assert.That(first.TypeName).IsEqualTo(typeof(FirstTestPlugin).FullName);
    }

    [Test]
    public async Task GeneratePluginsManifestJson_IsValidJson_WithExpectedShape()
    {
        // 安排
        var plugins = new IPlugin[] { new FirstTestPlugin() };

        // 操作
        var json = ManifestGenerator.GeneratePluginsManifestJson(plugins);

        // 断言：可解析且包含 version/plugins 属性
        using var doc = JsonDocument.Parse(json);
        await Assert.That(doc.RootElement.TryGetProperty("version", out _)).IsTrue();
        await Assert.That(doc.RootElement.TryGetProperty("generatedAt", out _)).IsTrue();
        await Assert.That(doc.RootElement.TryGetProperty("plugins", out var pluginList)).IsTrue();
        await Assert.That(pluginList.GetArrayLength()).IsEqualTo(1);
        await Assert.That(pluginList[0].GetProperty("name").GetString()).IsEqualTo("first-test");
    }

    [Test]
    public async Task GeneratePluginsManifest_EmptyList_ProducesEmptyManifest()
    {
        // 操作：空插件列表
        var manifest = ManifestGenerator.GeneratePluginsManifest(Array.Empty<IPlugin>());

        // 断言
        await Assert.That(manifest.Plugins).IsEmpty();
        await Assert.That(manifest.Version).IsEqualTo(ManifestGenerator.ManifestVersion);
    }

    /// <summary>
    /// 测试用插件（名称 first-test），验证清单按名称排序。
    /// </summary>
    private sealed class FirstTestPlugin : IPlugin
    {
        /// <inheritdoc />
        public string Name => "first-test";

        /// <inheritdoc />
        public void ConfigureServices(IServiceCollection services) { }

        /// <inheritdoc />
        public void Configure(IPluginContext context) { }
    }

    /// <summary>
    /// 测试用插件（名称 second-test）。
    /// </summary>
    private sealed class SecondTestPlugin : IPlugin
    {
        /// <inheritdoc />
        public string Name => "second-test";

        /// <inheritdoc />
        public void ConfigureServices(IServiceCollection services) { }

        /// <inheritdoc />
        public void Configure(IPluginContext context) { }
    }
}
