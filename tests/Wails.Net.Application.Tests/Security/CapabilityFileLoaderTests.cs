using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// 能力文件加载器单元测试（TUnit）。
/// 对应主题 C：CapabilityFileLoader 从 JSON 文件加载、目录扫描、注册到管理器。
/// </summary>
[NotInParallel]
public sealed class CapabilityFileLoaderTests
{
    /// <summary>
    /// 创建临时目录并写入测试 JSON 文件。
    /// </summary>
    private static string CreateTempCapabilityFile(string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "wails-cap-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "capability.json");
        File.WriteAllText(file, json);
        return dir;
    }

    [Test]
    public async Task LoadFromFile_ValidJson_ReturnsCapability()
    {
        // 安排
        var dir = CreateTempCapabilityFile("""
        {
            "identifier": "main-cap",
            "description": "主窗口能力",
            "permissions": ["core:default", "fs:allow-read"],
            "windows": ["main"]
        }
        """);
        var file = Path.Combine(dir, "capability.json");

        try
        {
            // 操作
            var capability = CapabilityFileLoader.LoadFromFile(file);

            // 断言
            await Assert.That(capability).IsNotNull();
            await Assert.That(capability!.Identifier).IsEqualTo("main-cap");
            await Assert.That(capability.Description).IsEqualTo("主窗口能力");
            await Assert.That(capability.Permissions.Count).IsEqualTo(2);
            await Assert.That(capability.Windows).Contains("main");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task LoadFromFile_NonExistentFile_ReturnsNull()
    {
        var capability = CapabilityFileLoader.LoadFromFile(Path.Combine(Path.GetTempPath(), "nonexistent.json"));

        await Assert.That(capability).IsNull();
    }

    [Test]
    public async Task LoadFromFile_InvalidJson_ReturnsNull()
    {
        // 安排：写入无效 JSON
        var dir = CreateTempCapabilityFile("{ invalid json }");
        var file = Path.Combine(dir, "capability.json");

        try
        {
            var capability = CapabilityFileLoader.LoadFromFile(file);
            await Assert.That(capability).IsNull();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task LoadFromDirectory_MultipleFiles_ReturnsAllCapabilities()
    {
        // 安排：创建包含两个 JSON 文件的目录
        var dir = Path.Combine(Path.GetTempPath(), "wails-cap-multi-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "cap1.json"), """
        { "identifier": "cap1", "permissions": ["fs:allow-read"] }
        """);
        File.WriteAllText(Path.Combine(dir, "cap2.json"), """
        { "identifier": "cap2", "permissions": ["window:default"] }
        """);

        try
        {
            // 操作
            var capabilities = CapabilityFileLoader.LoadFromDirectory(dir);

            // 断言
            await Assert.That(capabilities.Count).IsEqualTo(2);
            await Assert.That(capabilities.Any(c => c.Identifier == "cap1")).IsTrue();
            await Assert.That(capabilities.Any(c => c.Identifier == "cap2")).IsTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task LoadFromDirectory_NonExistentDirectory_ReturnsEmpty()
    {
        var capabilities = CapabilityFileLoader.LoadFromDirectory(Path.Combine(Path.GetTempPath(), "nonexistent-dir"));

        await Assert.That(capabilities.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RegisterToManager_DeclaresAndGrantsPermissions()
    {
        // 安排
        var options = Microsoft.Extensions.Options.Options.Create(new PermissionOptions { Enabled = true, DenyByDefault = true });
        var manager = new PermissionManager(options, NullLogger<PermissionManager>.Instance);
        var capability = new Capability("main-cap")
        {
            Permissions = { "fs:allow-read", "window:default" }
        };

        // 操作
        CapabilityFileLoader.RegisterToManager(manager, new[] { capability });

        // 断言：能力已声明
        var declared = manager.GetDeclaredCapabilities();
        await Assert.That(declared.Count).IsEqualTo(1);
        await Assert.That(declared.First().Identifier).IsEqualTo("main-cap");

        // 断言：权限已授权
        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
        await Assert.That(manager.IsGranted("window:default")).IsTrue();
    }

    [Test]
    public async Task LoadFromDirectory_SkipsFilesWithoutIdentifier()
    {
        // 安排：创建包含无效能力文件的目录（无 identifier 字段）
        var dir = Path.Combine(Path.GetTempPath(), "wails-cap-noid-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "noid.json"), """
        { "description": "missing identifier" }
        """);
        File.WriteAllText(Path.Combine(dir, "valid.json"), """
        { "identifier": "valid-cap" }
        """);

        try
        {
            var capabilities = CapabilityFileLoader.LoadFromDirectory(dir);

            // 断言：仅加载有效的能力文件
            await Assert.That(capabilities.Count).IsEqualTo(1);
            await Assert.That(capabilities.First().Identifier).IsEqualTo("valid-cap");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ============== !prefix Deny 语法测试（对应 P0-3）==============

    [Test]
    public async Task RegisterToManager_DenyPrefix_RegistersAsDeny()
    {
        // 安排：能力中包含 "!fs:allow-read" 表示显式拒绝
        var options = Microsoft.Extensions.Options.Options.Create(new PermissionOptions { Enabled = true, DenyByDefault = false });
        var manager = new PermissionManager(options, NullLogger<PermissionManager>.Instance);
        manager.Grant("fs:allow-read"); // 先授权
        var capability = new Capability("deny-cap")
        {
            Permissions = { "!fs:allow-read" }
        };

        // 操作
        CapabilityFileLoader.RegisterToManager(manager, new[] { capability });

        // 断言：fs:allow-read 被拒绝（即便已授权）
        await Assert.That(manager.IsGranted("fs:allow-read")).IsFalse();
        await Assert.That(manager.IsDenied("fs:allow-read", null)).IsTrue();
    }

    [Test]
    public async Task RegisterToManager_MixedGrantAndDeny_DenyWins()
    {
        // 安排：同一能力中混合 grant 和 deny 权限
        var options = Microsoft.Extensions.Options.Options.Create(new PermissionOptions { Enabled = true, DenyByDefault = true });
        var manager = new PermissionManager(options, NullLogger<PermissionManager>.Instance);
        var capability = new Capability("mixed-cap")
        {
            Permissions = { "fs:allow-read", "!fs:allow-write" },
            Windows = { "main" }
        };

        // 操作
        CapabilityFileLoader.RegisterToManager(manager, new[] { capability });

        // 断言
        await Assert.That(manager.IsGranted("fs:allow-read", "main")).IsTrue();
        // fs:allow-write 即便在窗口内也被拒绝（未显式授权但被 deny）
        await Assert.That(manager.IsGranted("fs:allow-write", "main")).IsFalse();
        await Assert.That(manager.IsDenied("fs:allow-write", "main")).IsTrue();
    }

    [Test]
    public async Task RegisterToManager_DenyPrefixWithWindows_OnlyDeniesThatWindow()
    {
        // 安排：窗口级拒绝
        var options = Microsoft.Extensions.Options.Options.Create(new PermissionOptions { Enabled = true, DenyByDefault = false });
        var manager = new PermissionManager(options, NullLogger<PermissionManager>.Instance);
        manager.Grant("fs:allow-read");
        var capability = new Capability("window-deny")
        {
            Permissions = { "!fs:allow-read" },
            Windows = { "main" }
        };

        // 操作
        CapabilityFileLoader.RegisterToManager(manager, new[] { capability });

        // 断言：main 窗口拒绝，settings 窗口仍可用
        await Assert.That(manager.IsGranted("fs:allow-read", "main")).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-read", "settings")).IsTrue();
    }
}
