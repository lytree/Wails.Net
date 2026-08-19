using System.IO;
using TUnit.Core;
using Wails.Net.Application.Security;
using Wails.Net.Cli.Check;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// R3-D.A 单元测试：<see cref="CapabilityChecker.LoadKnownPermissions"/>
/// 合并 <see cref="PluginPermissionManifestRegistry"/>（源生成器聚合）
/// 与外部清单目录（<c>--permissions-manifest</c> 显式指定）。
/// </summary>
[NotInParallel]
public sealed class CapabilityCheckerManifestAggregationTests
{
    /// <summary>
    /// 在每次测试前清空全局注册表，避免 <c>[ModuleInitializer]</c> 残留数据干扰断言。
    /// </summary>
    [Before(Test)]
    public void ResetRegistry()
    {
        PluginPermissionManifestRegistry.ClearForTests();
    }

    /// <summary>
    /// 测试结束后再次清空，避免跨测试残留。
    /// </summary>
    [After(Test)]
    public void CleanupRegistry()
    {
        PluginPermissionManifestRegistry.ClearForTests();
    }

    private static string CreateManifestDir(string fileName, string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "wails-manifest-agg-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), json);
        return dir;
    }

    [Test]
    public async Task LoadKnownPermissions_RegistryOnly_ReturnsGeneratedPermissions()
    {
        // 安排：仅在注册表中注册 3 个权限
        PluginPermissionManifestRegistry.Register("window", """{"permissions":["window:default","window:allow-close"]}""");
        PluginPermissionManifestRegistry.Register("fs", """{"permissions":["fs:default"]}""");

        // 操作：不传外部清单目录
        var (perms, gen, manifest) = CapabilityChecker.LoadKnownPermissions(null);

        // 断言
        await Assert.That(gen).IsEqualTo(3);
        await Assert.That(manifest).IsEqualTo(0);
        await Assert.That(perms.Count).IsEqualTo(3);
        await Assert.That(perms).Contains("window:default");
        await Assert.That(perms).Contains("window:allow-close");
        await Assert.That(perms).Contains("fs:default");
    }

    [Test]
    public async Task LoadKnownPermissions_ManifestDirOnly_ReturnsFilePermissions()
    {
        // 安排：仅在外部目录放 2 个权限
        var dir = CreateManifestDir("window.json", """["window:default","window:allow-readonly"]""");

        try
        {
            var (perms, gen, manifest) = CapabilityChecker.LoadKnownPermissions(dir);

            // 断言：注册表空、目录 2 个、合并 2 个
            await Assert.That(gen).IsEqualTo(0);
            await Assert.That(manifest).IsEqualTo(2);
            await Assert.That(perms.Count).IsEqualTo(2);
            await Assert.That(perms).Contains("window:default");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task LoadKnownPermissions_BothSources_ReturnsUnion()
    {
        // 安排：注册表 3 个 + 外部目录 2 个（其中 1 个与注册表重叠）
        PluginPermissionManifestRegistry.Register("window", """{"permissions":["window:default","window:allow-close","window:allow-readonly"]}""");
        var dir = CreateManifestDir("extra.json", """["window:default","core:default"]""");

        try
        {
            var (perms, gen, manifest) = CapabilityChecker.LoadKnownPermissions(dir);

            // 断言：注册表 3 + 目录 2 - 重复 1 = 合并 4
            await Assert.That(gen).IsEqualTo(3);
            await Assert.That(manifest).IsEqualTo(2);
            await Assert.That(perms.Count).IsEqualTo(4);
            await Assert.That(perms).Contains("window:default");
            await Assert.That(perms).Contains("window:allow-close");
            await Assert.That(perms).Contains("window:allow-readonly");
            await Assert.That(perms).Contains("core:default");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task LoadKnownPermissions_ManifestDirMissing_ReturnsEmptyForThatSource()
    {
        // 安排：注册表有数据、目录不存在
        PluginPermissionManifestRegistry.Register("core", """{"permissions":["core:default"]}""");
        var nonexistent = Path.Combine(Path.GetTempPath(), "wails-no-such-dir-" + Guid.NewGuid().ToString("N")[..8]);

        var (perms, gen, manifest) = CapabilityChecker.LoadKnownPermissions(nonexistent);

        // 断言：注册表数据保留、目录计数 0
        await Assert.That(gen).IsEqualTo(1);
        await Assert.That(manifest).IsEqualTo(0);
        await Assert.That(perms.Count).IsEqualTo(1);
    }

    [Test]
    public async Task LoadKnownPermissions_BothEmpty_ReturnsEmpty()
    {
        var (perms, gen, manifest) = CapabilityChecker.LoadKnownPermissions(null);

        await Assert.That(gen).IsEqualTo(0);
        await Assert.That(manifest).IsEqualTo(0);
        await Assert.That(perms.Count).IsEqualTo(0);
    }

    [Test]
    public async Task LoadKnownPermissions_ManifestFileCorrupted_SkipsThatFileKeepsRest()
    {
        // 安排：目录里 1 个合法文件 + 1 个损坏文件
        var dir = Path.Combine(Path.GetTempPath(), "wails-corrupt-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "good.json"), """["fs:default"]""");
        File.WriteAllText(Path.Combine(dir, "bad.json"), "{ this is not valid json");

        try
        {
            var (perms, gen, manifest) = CapabilityChecker.LoadKnownPermissions(dir);

            // 断言：损坏文件被跳过，合法文件仍计入
            await Assert.That(gen).IsEqualTo(0);
            await Assert.That(manifest).IsEqualTo(1);
            await Assert.That(perms.Count).IsEqualTo(1);
            await Assert.That(perms).Contains("fs:default");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task LoadKnownPermissions_ObjectFormManifest_ParsesPermissionsProperty()
    {
        // 安排：清单支持对象形式 { "permissions": [...] }
        var dir = CreateManifestDir("obj.json", """{"plugin":"window","permissions":["window:default","window:allow-close"]}""");

        try
        {
            var (perms, gen, manifest) = CapabilityChecker.LoadKnownPermissions(dir);

            await Assert.That(manifest).IsEqualTo(2);
            await Assert.That(perms).Contains("window:default");
            await Assert.That(perms).Contains("window:allow-close");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}