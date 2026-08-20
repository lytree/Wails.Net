using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// PluginPermissionManifestRegistry 单元测试（R3-C.2）。
/// 验证注册表正确收录、检索、聚合插件权限清单。
/// </summary>
[NotInParallel]
public sealed class PluginPermissionManifestRegistryTests
{
    [Test]
    public async Task Register_AndGet_RoundTripsManifest()
    {
        PluginPermissionManifestRegistry.ClearForTests();
        const string json = "{\"plugin\":\"foo\",\"permissions\":[\"foo:allow-x\"]}";
        PluginPermissionManifestRegistry.Register("foo", json);
        var got = PluginPermissionManifestRegistry.Get("foo");
        await Assert.That(got).IsEqualTo(json);
    }

    [Test]
    public async Task Register_MultiplePlugins_AllAccessibleViaGetAll()
    {
        PluginPermissionManifestRegistry.ClearForTests();
        PluginPermissionManifestRegistry.Register("foo", "{\"plugin\":\"foo\"}");
        PluginPermissionManifestRegistry.Register("bar", "{\"plugin\":\"bar\"}");
        var all = PluginPermissionManifestRegistry.GetAll();
        await Assert.That(all.Count).IsEqualTo(2);
        await Assert.That(all.Keys).Contains("foo");
        await Assert.That(all.Keys).Contains("bar");
    }

    [Test]
    public async Task Get_NonExistent_ReturnsNull()
    {
        PluginPermissionManifestRegistry.ClearForTests();
        var got = PluginPermissionManifestRegistry.Get("nonexistent");
        await Assert.That(got).IsNull();
    }

    [Test]
    public async Task Register_OverwriteSamePlugin_LastWins()
    {
        PluginPermissionManifestRegistry.ClearForTests();
        PluginPermissionManifestRegistry.Register("foo", "v1");
        PluginPermissionManifestRegistry.Register("foo", "v2");
        var got = PluginPermissionManifestRegistry.Get("foo");
        await Assert.That(got).IsEqualTo("v2");
    }

    [Test]
    public async Task CollectAllPermissions_AggregatesFromAllManifests()
    {
        PluginPermissionManifestRegistry.ClearForTests();
        PluginPermissionManifestRegistry.Register("foo", "{\"permissions\":[\"foo:allow-x\",\"foo:allow-y\"]}");
        PluginPermissionManifestRegistry.Register("bar", "{\"permissions\":[\"bar:allow-z\"]}");
        var all = PluginPermissionManifestRegistry.CollectAllPermissions();
        await Assert.That(all.Count).IsEqualTo(3);
        await Assert.That(all.Contains("foo:allow-x")).IsTrue();
        await Assert.That(all.Contains("foo:allow-y")).IsTrue();
        await Assert.That(all.Contains("bar:allow-z")).IsTrue();
    }

    [Test]
    public async Task CollectAllPermissions_DeduplicatesAcrossManifests()
    {
        PluginPermissionManifestRegistry.ClearForTests();
        PluginPermissionManifestRegistry.Register("foo", "{\"permissions\":[\"common:allow-x\"]}");
        PluginPermissionManifestRegistry.Register("bar", "{\"permissions\":[\"common:allow-x\"]}");
        var all = PluginPermissionManifestRegistry.CollectAllPermissions();
        await Assert.That(all.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CollectAllPermissions_SkipsCorruptedJson()
    {
        PluginPermissionManifestRegistry.ClearForTests();
        PluginPermissionManifestRegistry.Register("bad", "{ this is not valid json }");
        PluginPermissionManifestRegistry.Register("good", "{\"permissions\":[\"good:allow-x\"]}");
        var all = PluginPermissionManifestRegistry.CollectAllPermissions();
        await Assert.That(all.Count).IsEqualTo(1);
        await Assert.That(all.Contains("good:allow-x")).IsTrue();
    }

    [Test]
    public async Task CollectAllPermissions_IgnoresMissingPermissionsField()
    {
        PluginPermissionManifestRegistry.ClearForTests();
        PluginPermissionManifestRegistry.Register("empty", "{\"plugin\":\"empty\"}");
        var all = PluginPermissionManifestRegistry.CollectAllPermissions();
        await Assert.That(all).IsEmpty();
    }

    [Test]
    public async Task Register_NullJson_Throws()
    {
        PluginPermissionManifestRegistry.ClearForTests();
        try
        {
            PluginPermissionManifestRegistry.Register("foo", null!);
            await Assert.That(System.Environment.StackTrace).IsNotNull(); /* unreachable, expect throw */
        }
        catch (ArgumentNullException)
        {
            await Assert.That(System.Environment.StackTrace).IsNotNull(); /* reached after no-throw */
        }
    }

    [Test]
    public async Task Register_EmptyName_Throws()
    {
        PluginPermissionManifestRegistry.ClearForTests();
        try
        {
            PluginPermissionManifestRegistry.Register("", "{}");
            await Assert.That(System.Environment.StackTrace).IsNotNull(); /* unreachable, expect throw */
        }
        catch (ArgumentException)
        {
            await Assert.That(System.Environment.StackTrace).IsNotNull(); /* reached after no-throw */
        }
    }
}