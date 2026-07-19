using System.Reflection;
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
/// BiometricPlugin 的单元测试（TUnit）。
/// 对应 Tauri v2 biometric 插件功能。
/// 验证命令注册、降级路径（NullBiometricImpl 返回 none/false）、自定义实现注入与参数传递。
/// </summary>
[NotInParallel]
public sealed class BiometricPluginTests
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

    [Test]
    public async Task Name_ReturnsBiometric()
    {
        var plugin = new BiometricPlugin();
        await Assert.That(plugin.Name).IsEqualTo("biometric");
    }

    [Test]
    public async Task Configure_NullContext_ThrowsArgumentNullException()
    {
        var plugin = new BiometricPlugin();
        await Assert.That(() => plugin.Configure(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Configure_RegistersAllCommands()
    {
        // 安排
        var plugin = new BiometricPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);

        // 操作
        plugin.Configure(context);

        // 断言
        await Assert.That(context.Commands.Find("biometric.checkAvailability")).IsNotNull();
        await Assert.That(context.Commands.Find("biometric.authenticate")).IsNotNull();
    }

    [Test]
    public async Task CheckAvailability_WithDefaultImpl_ReturnsNone()
    {
        // 安排
        var plugin = new BiometricPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作
        var result = InvokeCommand(context.Commands, "biometric.checkAvailability", cmdCtx);

        // 断言：默认 NullBiometricImpl.CheckAvailability 返回 "none"
        await Assert.That(result).IsEqualTo("none");
    }

    [Test]
    public async Task Authenticate_WithDefaultImpl_ReturnsFalse()
    {
        // 安排
        var plugin = new BiometricPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作
        var result = await (Task<bool>)InvokeCommand(context.Commands, "biometric.authenticate", cmdCtx,
            new BiometricAuthOptions { Reason = "verify" })!;

        // 断言：默认 NullBiometricImpl.AuthenticateAsync 返回 false
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CheckAvailability_WithCustomImpl_ReturnsAvailable()
    {
        // 安排
        var customImpl = new FakeBiometricImpl { AvailabilityResult = "available" };
        var plugin = new BiometricPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformBiometric>();
        services.AddSingleton<IPlatformBiometric>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作
        var result = InvokeCommand(context.Commands, "biometric.checkAvailability", cmdCtx);

        // 断言
        await Assert.That(result).IsEqualTo("available");
        await Assert.That(customImpl.CheckCalled).IsTrue();
    }

    [Test]
    public async Task Authenticate_WithCustomImpl_PassesReasonAndReturnsTrue()
    {
        // 安排
        var customImpl = new FakeBiometricImpl { AuthResult = true };
        var plugin = new BiometricPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformBiometric>();
        services.AddSingleton<IPlatformBiometric>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作
        var result = await (Task<bool>)InvokeCommand(context.Commands, "biometric.authenticate", cmdCtx,
            new BiometricAuthOptions { Reason = "please verify identity" })!;

        // 断言
        await Assert.That(result).IsTrue();
        await Assert.That(customImpl.AuthenticateCalled).IsTrue();
        await Assert.That(customImpl.LastReason).IsEqualTo("please verify identity");
    }

    private sealed class FakeBiometricImpl : IPlatformBiometric
    {
        public string AvailabilityResult { get; set; } = "none";
        public bool AuthResult { get; set; } = false;
        public bool CheckCalled { get; private set; }
        public bool AuthenticateCalled { get; private set; }
        public string? LastReason { get; private set; }

        public string CheckAvailability()
        {
            CheckCalled = true;
            return AvailabilityResult;
        }

        public Task<bool> AuthenticateAsync(string reason)
        {
            AuthenticateCalled = true;
            LastReason = reason;
            return Task.FromResult(AuthResult);
        }
    }
}
