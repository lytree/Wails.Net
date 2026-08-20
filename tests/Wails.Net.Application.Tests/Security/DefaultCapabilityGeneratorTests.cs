using System.Text.Json;
using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// R3-D.B 单元测试：<see cref="DefaultCapabilityGenerator"/> 默认 capability JSON 生成。
/// </summary>
[NotInParallel]
public sealed class DefaultCapabilityGeneratorTests
{
    [Before(Test)]
    public void Reset()
    {
        PluginPermissionManifestRegistry.ClearForTests();
    }

    [After(Test)]
    public void Cleanup()
    {
        PluginPermissionManifestRegistry.ClearForTests();
    }

    [Test]
    public async Task DefaultIdentifier_ReturnsPluginNameColonDefault()
    {
        await Assert.That(DefaultCapabilityGenerator.DefaultIdentifier("window")).IsEqualTo("window:default");
        await Assert.That(DefaultCapabilityGenerator.DefaultIdentifier("fs")).IsEqualTo("fs:default");
    }

    [Test]
    public async Task DefaultIdentifier_EmptyPluginName_Throws()
    {
        await Assert.That(() => DefaultCapabilityGenerator.DefaultIdentifier("")).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task GenerateJson_WithManifest_IncludesAllPermissionsAndDefaultSet()
    {
        // 安排：清单含 3 个权限（无 :default 命名集）
        var json = DefaultCapabilityGenerator.GenerateJson(
            "window",
            """{"permissions":["window:allow-close","window:allow-minimize","window:allow-toggle-maximize"]}""");

        // 断言：JSON 可解析，包含 :default 命名集 + 全部细粒度权限
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("identifier").GetString()).IsEqualTo("window:default");
        var perms = root.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();
        await Assert.That(perms.Count).IsEqualTo(4);
        await Assert.That(perms[0]).IsEqualTo("window:default");
        await Assert.That(perms).Contains("window:allow-close");
        await Assert.That(perms).Contains("window:allow-minimize");
        await Assert.That(perms).Contains("window:allow-toggle-maximize");
    }

    [Test]
    public async Task GenerateJson_NullManifestJson_ProducesDefaultSetOnly()
    {
        var json = DefaultCapabilityGenerator.GenerateJson("fs", null);

        using var doc = JsonDocument.Parse(json);
        var perms = doc.RootElement.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();
        await Assert.That(perms).IsEquivalentTo(new[] { "fs:default" });
    }

    [Test]
    public async Task GenerateJson_ManifestAlreadyContainsDefaultSet_NoDuplicate()
    {
        // 安排：清单已包含 :default 命名集
        var json = DefaultCapabilityGenerator.GenerateJson(
            "core",
            """{"permissions":["core:default","core:allow-event"]}""");

        using var doc = JsonDocument.Parse(json);
        var perms = doc.RootElement.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();
        await Assert.That(perms.Count).IsEqualTo(2);
        await Assert.That(perms[0]).IsEqualTo("core:default");
        await Assert.That(perms[1]).IsEqualTo("core:allow-event");
    }

    [Test]
    public async Task GenerateJson_CorruptedManifest_StillProducesValidJson()
    {
        // 损坏的清单应被容忍，产出仅含 :default 命名集的能力 JSON
        var json = DefaultCapabilityGenerator.GenerateJson("bad", "{ not valid json");

        using var doc = JsonDocument.Parse(json);
        var perms = doc.RootElement.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();
        await Assert.That(perms).IsEquivalentTo(new[] { "bad:default" });
    }

    [Test]
    public async Task GenerateJson_ContainsDescriptionMentioningPluginName()
    {
        var json = DefaultCapabilityGenerator.GenerateJson("dialog", """{"permissions":["dialog:default"]}""");

        using var doc = JsonDocument.Parse(json);
        var desc = doc.RootElement.GetProperty("description").GetString();
        await Assert.That(desc).IsNotNull();
        await Assert.That(desc!).Contains("dialog");
        await Assert.That(desc).Contains("R3-D.B");
    }

    [Test]
    public async Task ExtractPermissions_ObjectForm_ParsesPermissionsProperty()
    {
        var result = DefaultCapabilityGenerator.ExtractPermissions(
            """{"plugin":"x","permissions":["x:default","x:allow-foo"]}""");

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result).Contains("x:default");
        await Assert.That(result).Contains("x:allow-foo");
    }

    [Test]
    public async Task ExtractPermissions_NullInput_ReturnsEmpty()
    {
        // ArgumentNullException is expected; generator should validate
        await Assert.That(() => DefaultCapabilityGenerator.ExtractPermissions(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ExtractPermissions_MalformedJson_ReturnsEmpty()
    {
        var result = DefaultCapabilityGenerator.ExtractPermissions("{ broken");
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GenerateAllDefaults_FromRegistry_ReturnsOneEntryPerPlugin()
    {
        // Arrange: register 2 plugins
        PluginPermissionManifestRegistry.Register("window", """{"permissions":["window:default","window:allow-close"]}""");
        PluginPermissionManifestRegistry.Register("fs", """{"permissions":["fs:default"]}""");

        var all = DefaultCapabilityGenerator.GenerateAllDefaults();

        await Assert.That(all.Count).IsEqualTo(2);
        await Assert.That(all.Keys).IsEquivalentTo(new[] { "window", "fs" });
        await Assert.That(all["window"]).Contains("window:default");
        await Assert.That(all["fs"]).Contains("fs:default");
    }

    [Test]
    public async Task GenerateAllDefaults_EmptyRegistry_ReturnsEmptyDictionary()
    {
        var all = DefaultCapabilityGenerator.GenerateAllDefaults();
        await Assert.That(all).IsEmpty();
    }
}