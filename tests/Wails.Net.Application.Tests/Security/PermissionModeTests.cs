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
    public async Task DefaultMode_IsStrict_R3E()
    {
        // R3-E：库默认从 Compatibility 翻转为 Strict（最小权限姿态）。
        var options = new PermissionOptions();

        await Assert.That(options.Mode).IsEqualTo(PermissionMode.Strict);
        // PermissionManager.Enabled 是计算属性（Mode==Strict || options.Enabled），
        // 所以即使 options.Enabled 默认 false，Strict 模式下 PermissionManager 仍报告 Enabled=true。
        var manager = new PermissionManager(Microsoft.Extensions.Options.Options.Create(options), null);
        await Assert.That(manager.Enabled).IsTrue();
    }

    [Test]
    public async Task DefaultMode_WithCompatibilityByDefault_OverridesToCompatibility()
    {
        // R3-E：v2.x → v3.0 升级过渡入口，临时回退到宽松模式。
        PermissionOptions.CompatibilityByDefault = true;
        try
        {
            var options = new PermissionOptions();
            await Assert.That(options.Mode).IsEqualTo(PermissionMode.Compatibility);
        }
        finally
        {
            PermissionOptions.CompatibilityByDefault = false;  // 恢复
        }
    }

    [Test]
    public async Task DefaultMode_IsStrict_DoesNotAffectOtherInstances_WhenCompatibilityByDefaultToggled()
    {
        // 验证 CompatibilityByDefault 是进程级开关，在切换后实例化新对象才会生效。
        var strictOptions = new PermissionOptions();
        await Assert.That(strictOptions.Mode).IsEqualTo(PermissionMode.Strict);

        PermissionOptions.CompatibilityByDefault = true;
        try
        {
            var compatOptions = new PermissionOptions();
            await Assert.That(compatOptions.Mode).IsEqualTo(PermissionMode.Compatibility);
        }
        finally
        {
            PermissionOptions.CompatibilityByDefault = false;
        }
    }

    [Test]
    public async Task CompatibilityMode_Disabled_DenyByDefaultFalse_AllowsAll()
    {
        // 兼容模式 + Enabled=false + DenyByDefault=false：未授权命令默认放行（v2.x 旧版行为）
        var manager = CreateManager(PermissionMode.Compatibility, enabled: false, denyByDefault: false);

        await Assert.That(manager.Enabled).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
    }

    [Test]
    public async Task CompatibilityMode_Disabled_DenyByDefaultTrue_Rejects()
    {
        // 兼容模式 + Enabled=false + DenyByDefault=true：未授权命令拒绝（DenyByDefault 优先）
        var manager = CreateManager(PermissionMode.Compatibility, enabled: false, denyByDefault: true);

        await Assert.That(manager.Enabled).IsFalse();
        await Assert.That(manager.IsGranted("fs:allow-read")).IsFalse();
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
