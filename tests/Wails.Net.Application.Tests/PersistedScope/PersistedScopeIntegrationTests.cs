using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Security;
using Wails.Net.Plugins.PersistedScope;

namespace Wails.Net.Application.Tests.PersistedScope;

/// <summary>
/// PersistedScope 与权限模型深度整合的单元测试（TUnit）。
/// 对应审计 P2-2：运行时动态授权的路径经 <see cref="IScope"/> 桥接，
/// 立即参与 <see cref="PermissionManager.ValidateScopes"/> 的参数级 Scope 校验，
/// 接近 Tauri v2 的 fs scope 体验。
/// </summary>
[NotInParallel]
public sealed class PersistedScopeIntegrationTests
{
    /// <summary>
    /// 创建临时目录并返回清理委托。
    /// </summary>
    private static (string Dir, Action Cleanup) CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"wails-persist-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return (dir, () => { try { Directory.Delete(dir, recursive: true); } catch { /* 忽略 */ } });
    }

    [Test]
    public async Task AddPath_PersistsToNamedScopeFile_AndReloads()
    {
        var (dir, cleanup) = CreateTempDir();
        try
        {
            var file = Path.Combine(dir, "scope-a.json");
            var allowedPath = Path.Combine(dir, "docs");

            // 第一个管理器添加并持久化
            var manager1 = new PersistedScopeManager();
            await Assert.That(manager1.AddPath(allowedPath, file)).IsTrue();

            // 新管理器从同一文件加载（模拟重启）
            var manager2 = new PersistedScopeManager();
            await Assert.That(manager2.Load(file)).IsTrue();
            await Assert.That(manager2.ListPaths(file)).Contains(allowedPath);
            await Assert.That(manager2.IsAllowed(allowedPath)).IsTrue();
        }
        finally
        {
            cleanup();
        }
    }

    [Test]
    public async Task Allows_ChecksAllNamedScopes_AsIScope()
    {
        var (dir, cleanup) = CreateTempDir();
        try
        {
            var fileA = Path.Combine(dir, "scope-a.json");
            var fileB = Path.Combine(dir, "scope-b.json");
            var pathA = Path.Combine(dir, "data-a");
            var pathB = Path.Combine(dir, "data-b");

            var manager = new PersistedScopeManager();
            manager.AddPath(pathA, fileA);
            manager.AddPath(pathB, fileB);

            // IScope.Allows 合并所有命名作用域
            await Assert.That(manager.Allows(pathA)).IsTrue();
            await Assert.That(manager.Allows(pathB)).IsTrue();
            await Assert.That(manager.Allows(Path.Combine(dir, "not-allowed"))).IsFalse();
        }
        finally
        {
            cleanup();
        }
    }

    [Test]
    public async Task ValidateScopes_AcceptsPersistedPath_AfterDynamicAuthorization()
    {
        var (dir, cleanup) = CreateTempDir();
        try
        {
            var file = Path.Combine(dir, "scope.json");
            var allowedPath = Path.Combine(dir, "docs");

            // 权限管理器（Strict + 授权 fs:allow-read + 绑定持久化 scope）
            var options = Microsoft.Extensions.Options.Options.Create(new PermissionOptions
            {
                Mode = PermissionMode.Strict,
                Enabled = true,
                DenyByDefault = true,
            });
            var permissionManager = new PermissionManager(options);
            permissionManager.Grant("fs:allow-read");
            var scope = new PersistedScopeManager();
            permissionManager.SetScope("fs:allow-read", scope);

            // 操作：运行时动态授权路径
            scope.AddPath(allowedPath, file);

            // 断言：授权路径通过参数级 Scope 校验；未授权路径被拒绝
            await Assert.That(permissionManager.ValidateScopes([("fs:allow-read", allowedPath)])).IsTrue();
            await Assert.That(
                permissionManager.ValidateScopes([("fs:allow-read", Path.Combine(dir, "secret"))])).IsFalse();
        }
        finally
        {
            cleanup();
        }
    }

    [Test]
    public async Task Plugin_Configure_BindsManagedPermissions()
    {
        // 安排：模拟插件上下文与权限声明器
        var context = Substitute.For<IPluginContext>();
        var registrar = Substitute.For<IPermissionRegistrar>();
        context.Permissions.Returns(registrar);
        context.Commands.Returns(new Wails.Net.Application.Commands.CommandRegistry());

        // 操作：配置插件（默认受管权限）
        var plugin = new PersistedScopePlugin();
        plugin.Configure(context);

        // 断言：fs:allow-read / fs:allow-write 绑定到持久化作用域
        registrar.Received(1).BindScope("fs:allow-read", Arg.Any<IScope>());
        registrar.Received(1).BindScope("fs:allow-write", Arg.Any<IScope>());
    }

    [Test]
    public async Task Plugin_ConfigureServices_RegistersPersistedScopeSingleton()
    {
        // 安排
        var services = new ServiceCollection();
        var plugin = new PersistedScopePlugin();

        // 操作
        plugin.ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        // 断言：IPersistedScope 可解析
        var resolved = provider.GetService<IPersistedScope>();
        await Assert.That(resolved).IsNotNull();
    }
}
