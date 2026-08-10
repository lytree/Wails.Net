using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TUnit.Core;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// 权限 strict 模式单元测试（OPT-SEC-001）。
/// 覆盖 PermissionMode.Compatibility / Strict 两种模式下的权限行为：
/// <list type="bullet">
/// <item>Compatibility + Enabled=false：默认放行（旧应用迁移行为）。</item>
/// <item>Strict：即便 Enabled=false 也强制启用权限检查（最小权限）。</item>
/// <item>Strict + 未授权：拒绝；Strict + Capability 授权：放行。</item>
/// </list>
/// </summary>
[NotInParallel]
public sealed class PermissionModeTests
{
    private static PermissionManager CreateManager(
        PermissionMode mode,
        bool enabled,
        bool denyByDefault = true,
        params string[] permissions)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PermissionOptions
        {
            Mode = mode,
            Enabled = enabled,
            DenyByDefault = denyByDefault,
            Permissions = permissions.ToList(),
        });
        return new PermissionManager(options, NullLogger<PermissionManager>.Instance);
    }

    [Test]
    public async Task DefaultMode_IsCompatibility()
    {
        var options = new PermissionOptions();

        await Assert.That(options.Mode).IsEqualTo(PermissionMode.Compatibility);
        await Assert.That(options.Enabled).IsFalse();
    }

    [Test]
    public async Task CompatibilityMode_Disabled_AllowsAll()
    {
        // 兼容模式 + Enabled=false：未授权命令默认放行
        var manager = CreateManager(PermissionMode.Compatibility, enabled: false);

        await Assert.That(manager.Enabled).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
    }

    [Test]
    public async Task CompatibilityMode_Enabled_Ungranted_Rejects()
    {
        // 兼容模式 + Enabled=true + DenyByDefault=true：未授权拒绝
        var manager = CreateManager(PermissionMode.Compatibility, enabled: true);

        await Assert.That(manager.Enabled).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read")).IsFalse();
    }

    [Test]
    public async Task StrictMode_ForcesEnabled_EvenWhenFlagFalse()
    {
        // Strict 模式：即便用户漏配 Enabled=true，也强制启用权限检查
        var manager = CreateManager(PermissionMode.Strict, enabled: false);

        await Assert.That(manager.Enabled).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-read")).IsFalse();
    }

    [Test]
    public async Task StrictMode_UngrantedPermission_Rejects()
    {
        var manager = CreateManager(PermissionMode.Strict, enabled: true);

        await Assert.That(manager.IsGranted("fs:allow-read")).IsFalse();
    }

    [Test]
    public async Task StrictMode_GrantedPermission_Allows()
    {
        var manager = CreateManager(PermissionMode.Strict, enabled: true, denyByDefault: true, "fs:allow-read");

        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
    }

    [Test]
    public async Task StrictMode_AfterCapabilityGrant_Allows()
    {
        var manager = CreateManager(PermissionMode.Strict, enabled: true);

        // 通过 capability 显式授权后放行（对应 capabilities 文件显式授权路径）
        manager.DeclareCapability(new Capability("main-capability")
        {
            Permissions = { "store:default" },
        });
        manager.Grant("store:default");

        await Assert.That(manager.IsGranted("store:default")).IsTrue();
    }

    [Test]
    public async Task StrictMode_DenyWinsOverGrant()
    {
        var manager = CreateManager(PermissionMode.Strict, enabled: true, denyByDefault: true, "shell:exec");

        manager.Deny("shell:exec");

        await Assert.That(manager.IsGranted("shell:exec")).IsFalse();
    }
}
