using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Security;

namespace Wails.Net.Application.Tests.Plugins;

/// <summary>
/// 权限声明器单元测试（TUnit）。
/// 对应主题 H-1：PermissionRegistrar 委托注册、NullPermissionRegistrar 空实现、
/// PluginContext 集成 PermissionManager。
/// </summary>
[NotInParallel]
public sealed class PermissionRegistrarTests
{
    /// <summary>
    /// 创建带指定选项的 PermissionManager。
    /// </summary>
    private static PermissionManager CreateManager(bool enabled = true, bool denyByDefault = true, params string[] permissions)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new PermissionOptions
        {
            Enabled = enabled,
            DenyByDefault = denyByDefault,
            Permissions = permissions.ToList(),
        });
        return new PermissionManager(options, NullLogger<PermissionManager>.Instance);
    }

    /// <summary>
    /// 创建带 PermissionManager 的 PluginContext。
    /// </summary>
    private static PluginContext CreateContext(PermissionManager manager)
    {
        var services = new ServiceCollection();
        var commands = new CommandRegistry();
        var config = new ConfigurationBuilder().Build();
        return new PluginContext(services, commands, config, NullLoggerFactory.Instance, manager);
    }

    [Test]
    public async Task RegisterPermissionSet_DelegatesToPermissionManager()
    {
        // 安排
        var manager = CreateManager();
        var registrar = new PermissionRegistrar(manager, NullLogger<PermissionManager>.Instance);

        // 操作
        registrar.RegisterPermissionSet("test:default", "测试默认权限集",
            "test:allow-read", "test:allow-write");

        // 断言：权限集已注册到 PermissionManager
        var sets = manager.GetPermissionSets().ToList();
        await Assert.That(sets.Count).IsEqualTo(1);
        await Assert.That(sets[0].Identifier).IsEqualTo("test:default");
        await Assert.That(sets[0].Description).IsEqualTo("测试默认权限集");
        await Assert.That(sets[0].Permissions.Count).IsEqualTo(2);
        await Assert.That(sets[0].Permissions).Contains("test:allow-read");
        await Assert.That(sets[0].Permissions).Contains("test:allow-write");
    }

    [Test]
    public async Task RegisterPermissionSet_GrantExpandsToPermissions()
    {
        // 安排
        var manager = CreateManager();
        var registrar = new PermissionRegistrar(manager, NullLogger<PermissionManager>.Instance);
        registrar.RegisterPermissionSet("fs:default", "文件系统默认权限集",
            "fs:allow-read", "fs:allow-write");

        // 操作：授权权限集
        manager.Grant("fs:default");

        // 断言：权限集展开为细粒度权限
        await Assert.That(manager.IsGranted("fs:allow-read")).IsTrue();
        await Assert.That(manager.IsGranted("fs:allow-write")).IsTrue();
    }

    [Test]
    public async Task DeclarePermission_RecordsPermission()
    {
        // 安排
        var manager = CreateManager();
        var registrar = new PermissionRegistrar(manager, NullLogger<PermissionManager>.Instance);

        // 操作
        registrar.DeclarePermission("fs:allow-read", "允许读取文件");
        registrar.DeclarePermission("fs:allow-write", "允许写入文件");

        // 断言：声明的权限可通过 GetDeclaredPermissionIds 获取
        var declaredIds = registrar.GetDeclaredPermissionIds().ToList();
        await Assert.That(declaredIds.Count).IsEqualTo(2);
        await Assert.That(declaredIds).Contains("fs:allow-read");
        await Assert.That(declaredIds).Contains("fs:allow-write");
    }

    [Test]
    public async Task BindScope_DelegatesToPermissionManager()
    {
        // 安排
        var manager = CreateManager();
        var registrar = new PermissionRegistrar(manager, NullLogger<PermissionManager>.Instance);
        var scope = new FileSystemScope();
        scope.AddPath("./data");

        // 操作
        registrar.BindScope("fs:allow-read", scope);

        // 断言：Scope 已绑定到 PermissionManager
        var boundScope = manager.GetScope("fs:allow-read");
        await Assert.That(boundScope).IsNotNull();
        await Assert.That(boundScope!.Allows("./data/file.txt")).IsTrue();
        await Assert.That(boundScope.Allows("./etc/passwd")).IsFalse();
    }

    [Test]
    public async Task NullPermissionRegistrar_AllOperationsAreNoOp()
    {
        // 安排
        var registrar = NullPermissionRegistrar.Instance;
        var scope = new FileSystemScope();

        // 操作：所有调用不应抛异常
        registrar.RegisterPermissionSet("test:default", "Test", "test:allow-x");
        registrar.DeclarePermission("test:allow-x", "Test permission");
        registrar.BindScope("test:allow-x", scope);

        // 断言：NullPermissionRegistrar 是单例
        await Assert.That(NullPermissionRegistrar.Instance).IsSameReferenceAs(registrar);
    }

    [Test]
    public async Task PluginContext_WithPermissionManager_ReturnsRealRegistrar()
    {
        // 安排
        var manager = CreateManager();

        // 操作
        var context = CreateContext(manager);

        // 断言：Permissions 返回真实 PermissionRegistrar（非 NullPermissionRegistrar）
        await Assert.That(context.Permissions).IsNotNull();
        await Assert.That(context.Permissions).IsTypeOf<PermissionRegistrar>();
    }

    [Test]
    public async Task PluginContext_WithoutPermissionManager_ReturnsNullRegistrar()
    {
        // 安排
        var services = new ServiceCollection();
        var commands = new CommandRegistry();
        var config = new ConfigurationBuilder().Build();

        // 操作：构造不带 PermissionManager 的 PluginContext
        var context = new PluginContext(services, commands, config, NullLoggerFactory.Instance, null);

        // 断言：Permissions 返回 NullPermissionRegistrar
        await Assert.That(context.Permissions).IsNotNull();
        await Assert.That(context.Permissions).IsTypeOf<NullPermissionRegistrar>();
    }

    [Test]
    public async Task PluginContext_Permissions_CanRegisterAndVerifyEndToEnd()
    {
        // 安排
        var manager = CreateManager();
        var context = CreateContext(manager);

        // 操作：通过 context.Permissions 注册权限集
        context.Permissions.RegisterPermissionSet("test:default", "测试权限集",
            "test:allow-a", "test:allow-b");
        context.Permissions.DeclarePermission("test:allow-a", "允许A操作");
        context.Permissions.BindScope("test:allow-a", new UrlScope());

        // 授权权限集
        manager.Grant("test:default");

        // 断言：权限已注册、声明、绑定，授权展开正确
        await Assert.That(manager.GetPermissionSets().Any(s => s.Identifier == "test:default")).IsTrue();
        await Assert.That(manager.IsGranted("test:allow-a")).IsTrue();
        await Assert.That(manager.IsGranted("test:allow-b")).IsTrue();
        await Assert.That(manager.GetScope("test:allow-a")).IsNotNull();
    }
}
