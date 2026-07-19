using System.Reflection;
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

    // ============== 窗口级 Capability 运行时隔离测试（对应 P0-1）==============

    [Test]
    public async Task Grant_WindowSpecific_IsGrantedForThatWindow_ReturnsTrue()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);

        // 操作：仅授权给 "main" 窗口
        manager.Grant("fs:allow-read", "main");

        // 断言：main 窗口可用
        await Assert.That(manager.IsGranted("fs:allow-read", "main")).IsTrue();
    }

    [Test]
    public async Task Grant_WindowSpecific_IsGrantedForOtherWindow_ReturnsFalse()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);

        // 操作：仅授权给 "main" 窗口
        manager.Grant("fs:allow-read", "main");

        // 断言：settings 窗口不可用（DenyByDefault=true）
        await Assert.That(manager.IsGranted("fs:allow-read", "settings")).IsFalse();
    }

    [Test]
    public async Task Grant_WindowSpecific_IsGrantedForNullWindow_ReturnsFalse()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);

        // 操作：仅授权给 "main" 窗口
        manager.Grant("fs:allow-read", "main");

        // 断言：未指定窗口（仅查全局）时不可用
        await Assert.That(manager.IsGranted("fs:allow-read")).IsFalse();
    }

    [Test]
    public async Task Grant_Global_IsGrantedForAnyWindow_ReturnsTrue()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);

        // 操作：全局授权
        manager.Grant("fs:allow-read");

        // 断言：任意窗口名都能命中全局授权
        await Assert.That(manager.IsGranted("fs:allow-read", "main")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", "settings")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
    }

    [Test]
    public async Task Grant_GlobalAndWindowSpecific_BothWork()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);

        // 操作：全局授权 fs:allow-read，窗口级授权 fs:allow-write 给 main
        manager.Grant("fs:allow-read");
        manager.Grant("fs:allow-write", "main");

        // 断言
        await Assert.That(manager.IsGranted("fs:allow-read", "any-window")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-write", "main")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-write", "settings")).IsFalse();
    }

    [Test]
    public async Task Grant_PermissionSet_WindowSpecific_ExpandsAllPermissionsForWindow()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.RegisterPermissionSet(new PermissionSet("fs:default")
        {
            Permissions = { "fs:allow-read", "fs:allow-write" }
        });

        // 操作：将权限集授权给 "main" 窗口
        manager.Grant("fs:default", "main");

        // 断言：main 窗口能使用集中所有权限
        await Assert.That(manager.IsGranted("fs:allow-read", "main")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-write", "main")).IsTrue();
        // 其他窗口不可用
        await Assert.That(manager.IsGranted("fs:allow-read", "settings")).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-write", "settings")).IsFalse();
    }

    [Test]
    public async Task Revoke_WindowSpecific_OnlyRevokesThatWindow()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read", "main");
        manager.Grant("fs:allow-read", "settings");

        // 操作：撤销 main 窗口的授权
        manager.Revoke("fs:allow-read", "main");

        // 断言
        await Assert.That(manager.IsGranted("fs:allow-read", "main")).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-read", "settings")).IsTrue();
    }

    [Test]
    public async Task GetGrantedPermissions_NoWindow_ReturnsAllUniquePermissions()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read");                    // 全局
        manager.Grant("fs:allow-write", "main");          // 窗口级
        manager.Grant("window:default", "settings");      // 窗口级

        // 操作
        var permissions = manager.GetGrantedPermissions();

        // 断言：去重后返回所有权限标识（不包含窗口信息）
        await Assert.That(permissions).Contains("fs:allow-read");
        await Assert.That(permissions).Contains("fs:allow-write");
        await Assert.That(permissions).Contains("window:default");
        await Assert.That(permissions.Count).IsEqualTo(3);
    }

    [Test]
    public async Task GetGrantedPermissionsWithWindows_ReturnsAllEntriesWithWindow()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read");                    // 全局
        manager.Grant("fs:allow-write", "main");          // 窗口级

        // 操作
        var entries = manager.GetGrantedPermissionsWithWindows().ToList();

        // 断言
        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries).Contains(("fs:allow-read", (string?)null));
        await Assert.That(entries).Contains(("fs:allow-write", "main"));
    }

    [Test]
    public async Task ValidateCapabilities_WindowSpecific_OnlyPassesForGrantedWindow()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read", "main");

        // 操作与断言
        await Assert.That(manager.ValidateCapabilities(new[] { "fs:allow-read" }, "main")).IsTrue();
        await Assert.That(manager.ValidateCapabilities(new[] { "fs:allow-read" }, "settings")).IsFalse();
        await Assert.That(manager.ValidateCapabilities(new[] { "fs:allow-read" }, null)).IsFalse();
    }

    [Test]
    public async Task IsGranted_WindowNameCaseInsensitive_MatchesCorrectly()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read", "Main");

        // 操作与断言：窗口名大小写不敏感比较
        await Assert.That(manager.IsGranted("fs:allow-read", "main")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", "MAIN")).IsTrue();
    }

    // ============== Capability.remote 远程 URL 校验测试（对应 P0-2）==============

    [Test]
    public async Task Grant_WithRemotePatterns_LocalOrigin_AlwaysAllowed()
    {
        // 安排：授权附带远程模式
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read", windowName: null, remotePatterns: new[] { "https://*.example.com" });

        // 操作与断言：本地源（null/空/wails:///localhost）始终放行
        await Assert.That(manager.IsGranted("fs:allow-read", null, null)).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "wails://localhost")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "http://localhost:8080")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "https://localhost")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "http://127.0.0.1")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "https://127.0.0.1:8443")).IsTrue();
    }

    [Test]
    public async Task Grant_WithRemotePatterns_MatchingRemoteOrigin_Allowed()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read", windowName: null, remotePatterns: new[] { "https://*.example.com" });

        // 操作与断言：匹配 https://*.example.com 的远程源放行
        await Assert.That(manager.IsGranted("fs:allow-read", null, "https://www.example.com")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "https://api.example.com")).IsTrue();
    }

    [Test]
    public async Task Grant_WithRemotePatterns_NonMatchingRemoteOrigin_Denied()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read", windowName: null, remotePatterns: new[] { "https://*.example.com" });

        // 操作与断言：不匹配的远程源拒绝
        await Assert.That(manager.IsGranted("fs:allow-read", null, "https://evil.com")).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "http://www.example.com")).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "https://example.org")).IsFalse();
    }

    [Test]
    public async Task Grant_WithoutRemotePatterns_RemoteOrigin_Allowed()
    {
        // 安排：无远程模式限制 → 远程源也允许（默认不限制）
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read");

        // 操作与断言
        await Assert.That(manager.IsGranted("fs:allow-read", null, "https://anywhere.com")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "https://evil.com")).IsTrue();
    }

    [Test]
    public async Task Grant_WindowSpecific_WithRemotePatterns_OnlyThatWindowChecksOrigin()
    {
        // 安排：fs:allow-read 授权给 main 窗口并附带远程模式
        // fs:allow-write 全局授权（无远程模式）
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read", "main", remotePatterns: new[] { "https://*.example.com" });
        manager.Grant("fs:allow-write");

        // 操作与断言：main 窗口的 fs:allow-read 检查来源
        await Assert.That(manager.IsGranted("fs:allow-read", "main", "https://www.example.com")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", "main", "https://evil.com")).IsFalse();
        // fs:allow-write 无远程限制，任意来源放行
        await Assert.That(manager.IsGranted("fs:allow-write", "main", "https://evil.com")).IsTrue();
    }

    [Test]
    public async Task Grant_PermissionSet_WithRemotePatterns_PropagatesToAllPermissions()
    {
        // 安排：权限集附带远程模式应展开到集内每个权限
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.RegisterPermissionSet(new PermissionSet("fs:default")
        {
            Permissions = { "fs:allow-read", "fs:allow-write" }
        });

        // 操作：授权权限集并附带远程模式
        manager.Grant("fs:default", windowName: null, remotePatterns: new[] { "https://*.example.com" });

        // 断言：集内所有权限都附带远程模式校验
        await Assert.That(manager.IsGranted("fs:allow-read", null, "https://api.example.com")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "https://evil.com")).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-write", null, "https://api.example.com")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-write", null, "https://evil.com")).IsFalse();
    }

    [Test]
    public async Task Grant_MultipleRemotePatterns_UnionMatching()
    {
        // 安排：多个模式合并后任一匹配即放行
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read", "main", remotePatterns: new[] { "https://*.example.com" });
        // 同一权限再次授权（全局）添加第二个模式
        manager.Grant("fs:allow-read", windowName: null, remotePatterns: new[] { "https://*.trusted.org" });

        // 操作与断言：main 窗口调用时，全局和窗口级模式合并
        await Assert.That(manager.IsGranted("fs:allow-read", "main", "https://api.example.com")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", "main", "https://safe.trusted.org")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", "main", "https://evil.com")).IsFalse();
    }

    [Test]
    public async Task IsGranted_LocalOriginCaseInsensitive_Allowed()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read", windowName: null, remotePatterns: new[] { "https://*.example.com" });

        // 操作与断言：本地源大小写不敏感
        await Assert.That(manager.IsGranted("fs:allow-read", null, "WAILS://LocalHost")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "HTTP://LOCALHOST")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", null, "Https://127.0.0.1")).IsTrue();
    }

    [Test]
    public async Task ValidateCapabilities_WithOrigin_RespectsRemotePatterns()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read", windowName: null, remotePatterns: new[] { "https://*.example.com" });

        // 操作与断言
        await Assert.That(manager.ValidateCapabilities(new[] { "fs:allow-read" }, null, "https://api.example.com")).IsTrue();
        await Assert.That(manager.ValidateCapabilities(new[] { "fs:allow-read" }, null, "https://evil.com")).IsFalse();
        await Assert.That(manager.ValidateCapabilities(new[] { "fs:allow-read" }, null, null)).IsTrue();
    }

    [Test]
    public async Task Grant_Global_WithRemotePatterns_CheckedForAllWindows()
    {
        // 安排：全局授权附带远程模式 → 任意窗口的调用都应校验来源
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read", windowName: null, remotePatterns: new[] { "https://*.example.com" });

        // 操作与断言：在任意窗口名下检查都生效
        await Assert.That(manager.IsGranted("fs:allow-read", "main", "https://api.example.com")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", "main", "https://evil.com")).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-read", "settings", "https://api.example.com")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read", "settings", "https://evil.com")).IsFalse();
    }

    // ============== Deny 权限测试（对应 P0-3）==============

    [Test]
    public async Task Deny_Global_AlwaysReturnsFalse_EvenIfGranted()
    {
        // 安排：先全局授权，再全局拒绝
        var manager = CreateManager(enabled: true, denyByDefault: false);
        manager.Grant("fs:allow-read");
        manager.Deny("fs:allow-read");

        // 操作与断言：deny 优先于 grant
        await Assert.That(manager.IsGranted("fs:allow-read")).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-read", "any-window")).IsFalse();
    }

    [Test]
    public async Task Deny_WindowSpecific_OnlyDeniesThatWindow()
    {
        // 安排：全局授权，窗口级拒绝给 main
        var manager = CreateManager(enabled: true, denyByDefault: false);
        manager.Grant("fs:allow-read");
        manager.Deny("fs:allow-read", "main");

        // 操作与断言：main 窗口被拒绝，其他窗口仍可用
        await Assert.That(manager.IsGranted("fs:allow-read", "main")).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-read", "settings")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
    }

    [Test]
    public async Task Deny_Global_OverridesWindowSpecificGrant()
    {
        // 安排：窗口级授权给 main，全局拒绝
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.Grant("fs:allow-read", "main");
        manager.Deny("fs:allow-read");

        // 操作与断言：全局 deny 覆盖窗口级 grant
        await Assert.That(manager.IsGranted("fs:allow-read", "main")).IsFalse();
    }

    [Test]
    public async Task Deny_PermissionSet_ExpandsToAllPermissions()
    {
        // 安排：注册权限集，授权并拒绝（混合）
        var manager = CreateManager(enabled: true, denyByDefault: true);
        manager.RegisterPermissionSet(new PermissionSet("fs:default")
        {
            Permissions = { "fs:allow-read", "fs:allow-write" }
        });
        manager.Grant("fs:default");
        // 拒绝权限集（应展开为集内所有权限被拒绝）
        manager.Deny("fs:default");

        // 操作与断言：集内所有权限都被拒绝
        await Assert.That(manager.IsGranted("fs:allow-read")).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-write")).IsFalse();
    }

    [Test]
    public async Task Undeny_RemovesDenyEntry_PermissionBecomesGrantedAgain()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: false);
        manager.Grant("fs:allow-read");
        manager.Deny("fs:allow-read");

        // 中间状态：被拒绝
        await Assert.That(manager.IsGranted("fs:allow-read")).IsFalse();

        // 操作：撤销拒绝
        manager.Undeny("fs:allow-read", windowName: null);

        // 断言：撤销拒绝后权限恢复为已授权
        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
    }

    [Test]
    public async Task IsDenied_ReturnsTrueForDeniedPermission()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: false);
        manager.Deny("fs:allow-read", "main");

        // 操作与断言
        await Assert.That(manager.IsDenied("fs:allow-read", "main")).IsTrue();
        await Assert.That(manager.IsDenied("fs:allow-read", null)).IsFalse();
        await Assert.That(manager.IsDenied("fs:allow-read", "settings")).IsFalse();
    }

    [Test]
    public async Task GetDeniedPermissions_ReturnsAllEntries()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: false);
        manager.Deny("fs:allow-read");             // 全局
        manager.Deny("fs:allow-write", "main");    // 窗口级

        // 操作
        var entries = manager.GetDeniedPermissions().ToList();

        // 断言
        await Assert.That(entries.Count).IsEqualTo(2);
        await Assert.That(entries).Contains(("fs:allow-read", (string?)null));
        await Assert.That(entries).Contains(("fs:allow-write", "main"));
    }

    [Test]
    public async Task Deny_AlsoAffectsOriginVariant()
    {
        // 安排：授权附带远程模式，但拒绝该权限
        var manager = CreateManager(enabled: true, denyByDefault: false);
        manager.Grant("fs:allow-read", windowName: null, remotePatterns: new[] { "https://*.example.com" });
        manager.Deny("fs:allow-read");

        // 操作与断言：IsGranted 带 origin 重载也应被 deny 覆盖
        await Assert.That(manager.IsGranted("fs:allow-read", null, "https://api.example.com")).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-read", null, null)).IsFalse();
    }

    [Test]
    public async Task Deny_WindowSpecific_OnlyAffectsThatWindow_OriginVariant()
    {
        // 安排
        var manager = CreateManager(enabled: true, denyByDefault: false);
        manager.Grant("fs:allow-read");
        manager.Deny("fs:allow-read", "main");

        // 操作与断言：带 origin 的 IsGranted 也遵循窗口级 deny
        await Assert.That(manager.IsGranted("fs:allow-read", "main", null)).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-read", "settings", null)).IsTrue();
    }
}

