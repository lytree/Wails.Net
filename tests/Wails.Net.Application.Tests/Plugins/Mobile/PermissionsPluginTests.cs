using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.Mobile;

namespace Wails.Net.Application.Tests.Plugins.Mobile;

/// <summary>
/// PermissionsPlugin 的单元测试（TUnit）。
/// 对应 Tauri v2 permissions 插件功能。
/// 验证命令注册、降级路径（NullPermissionsImpl 返回 granted）、自定义实现注入与多权限请求结果。
/// </summary>
[NotInParallel]
public sealed class PermissionsPluginTests
{
    private static (IPluginContext context, ServiceCollection services) CreatePluginContext()
    {
        var services = new ServiceCollection();
        var commands = new CommandRegistry();
        var config = new ConfigurationBuilder().Build();
        var loggerFactory = LoggerFactory.Create(_ => { });

        var context = Substitute.For<IPluginContext>();
        context.Services.Returns(services);
        context.Commands.Returns(commands);
        context.Configuration.Returns(config);
        context.LoggerFactory.Returns(loggerFactory);
        return (context, services);
    }

    private static ICommandContext CreateCommandContext(IServiceProvider serviceProvider)
    {
        var ctx = Substitute.For<ICommandContext>();
        ctx.Services.Returns(serviceProvider);
        ctx.WindowId.Returns((uint?)null);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private static object? InvokeCommand(CommandRegistry registry, string name, params object?[] args)
        => CommandTestHelper.Invoke(registry, name, args);

    // ---------------------------------------------------------------------
    // 基础测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task Name_ReturnsPermissions()
    {
        var plugin = new PermissionsPlugin();
        await Assert.That(plugin.Name).IsEqualTo("permissions");
    }

    [Test]
    public async Task Configure_NullContext_ThrowsArgumentNullException()
    {
        var plugin = new PermissionsPlugin();
        await Assert.That(() => plugin.Configure(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ConfigureServices_NullServices_ThrowsArgumentNullException()
    {
        var plugin = new PermissionsPlugin();
        await Assert.That(() => plugin.ConfigureServices(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ConfigureServices_RegistersDefaultPermissionsImpl()
    {
        var plugin = new PermissionsPlugin();
        var services = new ServiceCollection();

        plugin.ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        var impl = provider.GetService<IPlatformPermissions>();
        await Assert.That(impl).IsNotNull();
    }

    [Test]
    public async Task Configure_RegistersAllCommands()
    {
        var plugin = new PermissionsPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);

        plugin.Configure(context);

        await Assert.That(context.Commands.Find("permissions.check")).IsNotNull();
        await Assert.That(context.Commands.Find("permissions.request")).IsNotNull();
    }

    // ---------------------------------------------------------------------
    // 降级路径测试（NullPermissionsImpl 全部返回 granted）
    // ---------------------------------------------------------------------

    [Test]
    public async Task Check_WithDefaultImpl_ReturnsGranted()
    {
        var plugin = new PermissionsPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        var result = await (Task<string>)InvokeCommand(
            context.Commands, "permissions.check", cmdCtx, "android.permission.CAMERA")!;

        await Assert.That(result).IsEqualTo("granted");
    }

    [Test]
    public async Task Request_WithDefaultImpl_ReturnsAllGranted()
    {
        var plugin = new PermissionsPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        var permissions = new[] { "android.permission.CAMERA", "android.permission.RECORD_AUDIO" };
        var result = await (Task<PermissionRequestResult[]>)InvokeCommand(
            context.Commands, "permissions.request", cmdCtx, permissions)!;

        await Assert.That(result).Count().IsEqualTo(2);
        await Assert.That(result[0].Permission).IsEqualTo("android.permission.CAMERA");
        await Assert.That(result[0].State).IsEqualTo("granted");
        await Assert.That(result[1].Permission).IsEqualTo("android.permission.RECORD_AUDIO");
        await Assert.That(result[1].State).IsEqualTo("granted");
    }

    // ---------------------------------------------------------------------
    // 自定义实现注入测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task Check_WithCustomImpl_ReturnsPrompt()
    {
        var customImpl = new FakePermissionsImpl { CheckResult = "prompt" };
        var plugin = new PermissionsPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformPermissions>();
        services.AddSingleton<IPlatformPermissions>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        var result = await (Task<string>)InvokeCommand(
            context.Commands, "permissions.check", cmdCtx, "android.permission.CAMERA")!;

        await Assert.That(result).IsEqualTo("prompt");
        await Assert.That(customImpl.CheckCalled).IsTrue();
        await Assert.That(customImpl.LastCheckedPermission).IsEqualTo("android.permission.CAMERA");
    }

    [Test]
    public async Task Request_WithCustomImpl_ReturnsMixedResults()
    {
        var customImpl = new FakePermissionsImpl
        {
            RequestResults = new[]
            {
                new PermissionRequestResult { Permission = "android.permission.CAMERA", State = "granted" },
                new PermissionRequestResult { Permission = "android.permission.RECORD_AUDIO", State = "denied" },
            },
        };
        var plugin = new PermissionsPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformPermissions>();
        services.AddSingleton<IPlatformPermissions>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        var permissions = new[] { "android.permission.CAMERA", "android.permission.RECORD_AUDIO" };
        var result = await (Task<PermissionRequestResult[]>)InvokeCommand(
            context.Commands, "permissions.request", cmdCtx, permissions)!;

        await Assert.That(result).Count().IsEqualTo(2);
        await Assert.That(result[0].State).IsEqualTo("granted");
        await Assert.That(result[1].State).IsEqualTo("denied");
        await Assert.That(customImpl.RequestCalled).IsTrue();
    }

    /// <summary>
    /// 用于测试的假权限实现，记录方法调用。
    /// </summary>
    private sealed class FakePermissionsImpl : IPlatformPermissions
    {
        public string CheckResult { get; set; } = "granted";
        public PermissionRequestResult[] RequestResults { get; set; } = Array.Empty<PermissionRequestResult>();
        public bool CheckCalled { get; private set; }
        public bool RequestCalled { get; private set; }
        public string? LastCheckedPermission { get; private set; }
        public string[]? LastRequestedPermissions { get; private set; }

        public Task<string> CheckAsync(string permission)
        {
            CheckCalled = true;
            LastCheckedPermission = permission;
            return Task.FromResult(CheckResult);
        }

        public Task<PermissionRequestResult[]> RequestAsync(string[] permissions, CancellationToken cancellationToken)
        {
            RequestCalled = true;
            LastRequestedPermissions = permissions;
            return Task.FromResult(RequestResults);
        }
    }
}
