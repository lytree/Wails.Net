using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Security;

/// <summary>
/// Capability 自动加载单元测试（TUnit）。
/// 对应变更 4：验证 DesktopApplicationBuilder.Build() 在 PermissionOptions.Enabled=true 时
/// 自动从 CapabilitiesDirectory 加载 Capability JSON 文件并注册到 PermissionManager。
/// </summary>
[NotInParallel]
public sealed class CapabilityAutoLoadTests
{
    /// <summary>
    /// 创建临时目录并写入一个 Capability JSON 文件。
    /// </summary>
    /// <param name="identifier">能力标识。</param>
    /// <param name="permissions">声明的权限列表。</param>
    /// <returns>临时目录的绝对路径。</returns>
    private static string CreateTempCapabilitiesDir(string identifier, params string[] permissions)
    {
        var dir = Path.Combine(Path.GetTempPath(), "wailsnet-cap-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var perms = string.Join(", ", permissions.Select(p => $"\"{p}\""));
        var json = $"{{ \"identifier\": \"{identifier}\", \"description\": \"测试能力\", \"permissions\": [{perms}] }}";
        File.WriteAllText(Path.Combine(dir, "test.json"), json);

        return dir;
    }

    /// <summary>
    /// 清理临时目录。
    /// </summary>
    private static void Cleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // 清理失败不影响测试结果
        }
    }

    [Test]
    public async Task Build_WithCapabilitiesDirectory_LoadsCapabilitiesAutomatically()
    {
        // 安排：创建含 test:permission-auto 权限的 capabilities 目录
        var capDir = CreateTempCapabilitiesDir("auto-load-cap", "test:permission-auto");
        try
        {
            var builder = DesktopApplicationBuilder.CreateBuilder();
            builder.Services.Configure<PermissionOptions>(o =>
            {
                o.Enabled = true;
                o.DenyByDefault = true;
                o.CapabilitiesDirectory = capDir; // 绝对路径
            });

            // 操作
            var app = builder.Build();

            // 断言：权限已自动加载并授权
            var manager = app.Services.GetRequiredService<PermissionManager>();
            var granted = manager.GetGrantedPermissions();
            await Assert.That(granted).Contains("test:permission-auto");
            await Assert.That(manager.IsGranted("test:permission-auto")).IsTrue();
        }
        finally
        {
            Cleanup(capDir);
        }
    }

    [Test]
    public async Task Build_WithCapabilitiesDirectoryNotExists_SkipsSilently()
    {
        // 安排：指向不存在的目录
        var nonExistentDir = Path.Combine(Path.GetTempPath(), "wailsnet-no-such-" + Guid.NewGuid().ToString("N"));

        var builder = DesktopApplicationBuilder.CreateBuilder();
        builder.Services.Configure<PermissionOptions>(o =>
        {
            o.Enabled = true;
            o.DenyByDefault = true;
            o.CapabilitiesDirectory = nonExistentDir;
        });

        // 操作：Build 不抛异常
        var app = builder.Build();

        // 断言：未加载任何能力
        var manager = app.Services.GetRequiredService<PermissionManager>();
        await Assert.That(manager.GetGrantedPermissions()).IsEmpty();
    }

    [Test]
    public async Task Build_WithPermissionsDisabled_DoesNotLoadCapabilities()
    {
        // 安排：Enabled=false 但目录存在并含权限文件
        var capDir = CreateTempCapabilitiesDir("disabled-cap", "test:should-not-load");
        try
        {
            var builder = DesktopApplicationBuilder.CreateBuilder();
            builder.Services.Configure<PermissionOptions>(o =>
            {
                o.Enabled = false; // 未启用
                o.CapabilitiesDirectory = capDir;
            });

            // 操作
            var app = builder.Build();

            // 断言：权限未启用，能力文件未加载
            var manager = app.Services.GetRequiredService<PermissionManager>();
            await Assert.That(manager.Enabled).IsFalse();
            await Assert.That(manager.GetGrantedPermissions()).DoesNotContain("test:should-not-load");
        }
        finally
        {
            Cleanup(capDir);
        }
    }

    [Test]
    public async Task Build_WithCapabilitiesDirectoryNull_DoesNotLoadCapabilities()
    {
        // 安排：Enabled=true 但显式禁用 CapabilitiesDirectory（设为 null）
        var capDir = CreateTempCapabilitiesDir("null-dir-cap", "test:null-dir-perm");
        try
        {
            var builder = DesktopApplicationBuilder.CreateBuilder();
            builder.Services.Configure<PermissionOptions>(o =>
            {
                o.Enabled = true;
                o.DenyByDefault = true;
                o.CapabilitiesDirectory = null; // 显式禁用自动加载
            });

            // 操作
            var app = builder.Build();

            // 断言：未加载任何能力（目录虽存在但未配置）
            var manager = app.Services.GetRequiredService<PermissionManager>();
            await Assert.That(manager.GetGrantedPermissions()).DoesNotContain("test:null-dir-perm");
        }
        finally
        {
            Cleanup(capDir);
        }
    }

    [Test]
    public async Task Build_WithCustomCapabilitiesDirectory_LoadsFromCustomPath()
    {
        // 安排：使用自定义目录名（非默认 "capabilities"）
        var customDir = CreateTempCapabilitiesDir("custom-path-cap", "custom:permission");
        try
        {
            var builder = DesktopApplicationBuilder.CreateBuilder();
            builder.Services.Configure<PermissionOptions>(o =>
            {
                o.Enabled = true;
                o.DenyByDefault = true;
                o.CapabilitiesDirectory = customDir;
            });

            // 操作
            var app = builder.Build();

            // 断言：从自定义路径加载
            var manager = app.Services.GetRequiredService<PermissionManager>();
            var granted = manager.GetGrantedPermissions();
            await Assert.That(granted).Contains("custom:permission");
            await Assert.That(manager.IsGranted("custom:permission")).IsTrue();
        }
        finally
        {
            Cleanup(customDir);
        }
    }
}
