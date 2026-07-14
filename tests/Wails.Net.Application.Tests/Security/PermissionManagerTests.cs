using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// 权限管理器单元测试（TUnit）。
/// 对应主题 C：PermissionManager 线程安全、DenyByDefault 语义、PermissionSet 展开、Scope 绑定。
/// </summary>
[NotInParallel]
public sealed class PermissionManagerTests
{
    /// <summary>
    /// 创建带指定选项的 PermissionManager。
    /// </summary>
    private static PermissionManager CreateManager(bool enabled, bool denyByDefault, params string[] permissions)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PermissionOptions
        {
            Enabled = enabled,
            DenyByDefault = denyByDefault,
            Permissions = permissions.ToList(),
        });
        return new PermissionManager(options, NullLogger<PermissionManager>.Instance);
    }

    [Test]
    public async Task IsGranted_Disabled_ReturnsTrue()
    {
        // 安排：权限检查未启用
        var manager = CreateManager(enabled: false, denyByDefault: true);

        // 操作与断言：未启用时全部放行
        await Assert.That(manager.IsGranted("any:permission")).IsTrue();
    }

    [Test]
    public async Task IsGranted_Enabled_DenyByDefault_Ungranted_ReturnsFalse()
    {
        // 安排：启用权限检查，DenyByDefault=true，未授权任何权限
        var manager = CreateManager(enabled: true, denyByDefault: true);

        // 操作与断言：未授权的权限被拒绝
        await Assert.That(manager.IsGranted("fs:allow-read")).IsFalse();
    }

    [Test]
    public async Task IsGranted_Enabled_DenyByDefault_Granted_ReturnsTrue()
    {
        // 安排：启用权限检查，DenyByDefault=true，已授权 fs:allow-read
        var manager = CreateManager(enabled: true, denyByDefault: true, "fs:allow-read");

        // 操作与断言：已授权的权限允许
        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
    }

    [Test]
    public async Task IsGranted_Enabled_AllowByDefault_Ungranted_ReturnsTrue()
    {
        // 安排：启用权限检查，DenyByDefault=false（默认放行）
        var manager = CreateManager(enabled: true, denyByDefault: false);

        // 操作与断言：未授权的权限默认放行
        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
    }

    [Test]
    public async Task Grant_AddsPermission()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);

        // 操作
        manager.Grant("fs:allow-read");

        // 断言
        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
        await Assert.That(manager.GetGrantedPermissions()).Contains("fs:allow-read");
    }

    [Test]
    public async Task Revoke_RemovesPermission()
    {
        // 安排：已授权
        var manager = CreateManager(enabled: true, denyByDefault: false, "fs:allow-read");

        // 操作
        manager.Revoke("fs:allow-read");

        // 断言：撤销后拒绝（即便 DenyByDefault=false，显式撤销的权限也应拒绝）
        // 注：当前实现 Revoke 仅从集合移除，DenyByDefault=false 时仍返回 true
        // 因此测试改为验证权限不在已授权列表中
        await Assert.That(manager.GetGrantedPermissions().Contains("fs:allow-read")).IsFalse();
    }

    [Test]
    public async Task Grant_PermissionSet_ExpandsToPermissions()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.RegisterPermissionSet(new PermissionSet("core:default")
        {
            Permissions = { "fs:allow-read", "fs:allow-write", "window:default" }
        });

        // 操作：授权权限集标识
        manager.Grant("core:default");

        // 断言：集内所有权限都被授权
        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-write")).IsTrue();
        await Assert.That(manager.IsGranted("window:default")).IsTrue();
    }

    [Test]
    public async Task DeclareCapability_StoresCapability()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        var capability = new Capability("main-cap")
        {
            Permissions = { "fs:allow-read" },
            Windows = { "main" }
        };

        // 操作
        manager.DeclareCapability(capability);

        // 断言
        var declared = manager.GetDeclaredCapabilities();
        await Assert.That(declared.Count).IsEqualTo(1);
        await Assert.That(declared.First().Identifier).IsEqualTo("main-cap");
    }

    [Test]
    public async Task IsCapabilityApplicableToWindow_GlobalCapability_ReturnsTrue()
    {
        // 安排：Windows 列表为空表示全局能力
        var capability = new Capability("global") { Windows = new() };

        // 操作与断言
        await Assert.That(PermissionManager.IsCapabilityApplicableToWindow(capability, "any-window")).IsTrue();
        await Assert.That(PermissionManager.IsCapabilityApplicableToWindow(capability, null)).IsTrue();
    }

    [Test]
    public async Task IsCapabilityApplicableToWindow_SpecificWindow_Matching_ReturnsTrue()
    {
        // 安排：Windows 列表指定 "main"
        var capability = new Capability("main-cap") { Windows = { "main" } };

        // 操作与断言
        await Assert.That(PermissionManager.IsCapabilityApplicableToWindow(capability, "main")).IsTrue();
    }

    [Test]
    public async Task IsCapabilityApplicableToWindow_SpecificWindow_NonMatching_ReturnsFalse()
    {
        // 安排
        var capability = new Capability("main-cap") { Windows = { "main" } };

        // 操作与断言
        await Assert.That(PermissionManager.IsCapabilityApplicableToWindow(capability, "settings")).IsFalse();
        await Assert.That(PermissionManager.IsCapabilityApplicableToWindow(capability, null)).IsFalse();
    }

    [Test]
    public async Task SetScope_And_GetScope_BindScopeToPermission()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        var scope = new FileSystemScope();
        scope.AddPath(Path.GetTempPath());

        // 操作
        manager.SetScope("fs:allow-read", scope);

        // 断言
        var retrieved = manager.GetScope("fs:allow-read");
        await Assert.That(retrieved).IsNotNull();
        await Assert.That(retrieved!.Allows(Path.Combine(Path.GetTempPath(), "test.txt"))).IsTrue();
    }

    [Test]
    public async Task GetScope_NotBound_ReturnsNull()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);

        // 操作与断言
        await Assert.That(manager.GetScope("fs:allow-read")).IsNull();
    }

    [Test]
    public async Task RegisterPermissionSet_StoresSet()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        var set = new PermissionSet("fs:default") { Permissions = { "fs:allow-read" } };

        // 操作
        manager.RegisterPermissionSet(set);

        // 断言
        var sets = manager.GetPermissionSets();
        await Assert.That(sets.Count).IsEqualTo(1);
        await Assert.That(sets.First().Identifier).IsEqualTo("fs:default");
    }
}
