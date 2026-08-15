using System.Text.Json;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Manifests;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Manifests;

/// <summary>
/// <see cref="ManifestGenerator"/> 的单元测试（TUnit）。
/// 对应审计 P1-2：命令/capability 清单生成与"敏感命令必须被 capability 覆盖"一致性校验。
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
}
