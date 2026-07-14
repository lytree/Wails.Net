using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Options;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.Mobile;

namespace Wails.Net.Application.Tests.Plugins.Mobile;

/// <summary>
/// HapticsPlugin 的单元测试（TUnit）。
/// 对应 Tauri v2 haptics 插件功能。
/// 验证命令注册、参数解析、降级路径（NullHapticsImpl no-op）。
/// </summary>
[NotInParallel]
public sealed class HapticsPluginTests
{
    /// <summary>
    /// 创建模拟的 <see cref="IPluginContext"/>，并预先调用 <see cref="HapticsPlugin.ConfigureServices"/> 注册默认实现。
    /// </summary>
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

    /// <summary>
    /// 创建模拟的 <see cref="ICommandContext"/>，绑定到指定的服务容器。
    /// </summary>
    private static ICommandContext CreateCommandContext(IServiceProvider serviceProvider)
    {
        var ctx = Substitute.For<ICommandContext>();
        ctx.Services.Returns(serviceProvider);
        ctx.WindowId.Returns((uint?)null);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    /// <summary>
    /// 通过命令注册表调用命令。
    /// 反射调用抛出的 <see cref="TargetInvocationException"/> 会被解包为内部异常。
    /// </summary>
    private static object? InvokeCommand(CommandRegistry registry, string name, params object?[] args)
    {
        var entry = registry.Find(name);
        if (entry is null)
        {
            throw new InvalidOperationException($"命令未找到: {name}");
        }
        try
        {
            return entry.Method.Invoke(entry.Instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    // ---------------------------------------------------------------------
    // 基础测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task Name_ReturnsHaptics()
    {
        var plugin = new HapticsPlugin();
        await Assert.That(plugin.Name).IsEqualTo("haptics");
    }

    [Test]
    public async Task Configure_NullContext_ThrowsArgumentNullException()
    {
        var plugin = new HapticsPlugin();
        await Assert.That(() => plugin.Configure(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ConfigureServices_NullServices_ThrowsArgumentNullException()
    {
        var plugin = new HapticsPlugin();
        await Assert.That(() => plugin.ConfigureServices(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ConfigureServices_RegistersDefaultHapticsImpl()
    {
        // 安排
        var plugin = new HapticsPlugin();
        var services = new ServiceCollection();

        // 操作
        plugin.ConfigureServices(services);

        // 断言：IPlatformHaptics 已注册
        var provider = services.BuildServiceProvider();
        var impl = provider.GetService<IPlatformHaptics>();
        await Assert.That(impl).IsNotNull();
    }

    [Test]
    public async Task Configure_RegistersAllCommands()
    {
        // 安排
        var plugin = new HapticsPlugin();
        var (context, _) = CreatePluginContext();
        plugin.ConfigureServices(context.Services);

        // 操作
        plugin.Configure(context);

        // 断言：3 个命令已注册
        await Assert.That(context.Commands.Find("haptics.vibrate")).IsNotNull();
        await Assert.That(context.Commands.Find("haptics.cancel")).IsNotNull();
        await Assert.That(context.Commands.Find("haptics.notification")).IsNotNull();
    }

    // ---------------------------------------------------------------------
    // 降级路径测试（NullHapticsImpl no-op）
    // ---------------------------------------------------------------------

    [Test]
    public async Task Vibrate_WithDefaultImpl_DoesNotThrow()
    {
        // 安排
        var plugin = new HapticsPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作与断言：默认 NullHapticsImpl 是 no-op，不抛异常
        await Assert.That(() => InvokeCommand(context.Commands, "haptics.vibrate", cmdCtx,
            new HapticsVibrateOptions { Duration = 200 })).ThrowsNothing();
    }

    [Test]
    public async Task Cancel_WithDefaultImpl_DoesNotThrow()
    {
        // 安排
        var plugin = new HapticsPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作与断言
        await Assert.That(() => InvokeCommand(context.Commands, "haptics.cancel", cmdCtx)).ThrowsNothing();
    }

    [Test]
    public async Task Notification_WithDefaultImpl_DoesNotThrow()
    {
        // 安排
        var plugin = new HapticsPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作与断言：所有 NotificationType 枚举值都不会抛异常
        foreach (var type in Enum.GetValues<NotificationType>())
        {
            await Assert.That(() => InvokeCommand(context.Commands, "haptics.notification", cmdCtx,
                new HapticsNotificationOptions { Type = type })).ThrowsNothing();
        }
    }

    // ---------------------------------------------------------------------
    // 自定义实现注入测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task Vibrate_WithCustomImpl_CallsVibrate()
    {
        // 安排：使用自定义实现替换默认 NullHapticsImpl
        var customImpl = new FakeHapticsImpl();
        var plugin = new HapticsPlugin();
        var (context, services) = CreatePluginContext();
        // 先注册默认实现，再覆盖
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformHaptics>();
        services.AddSingleton<IPlatformHaptics>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作
        InvokeCommand(context.Commands, "haptics.vibrate", cmdCtx, new HapticsVibrateOptions { Duration = 500 });

        // 断言：自定义实现的 Vibrate 被调用，参数正确
        await Assert.That(customImpl.VibrateCalled).IsTrue();
        await Assert.That(customImpl.LastDurationMs).IsEqualTo(500);
    }

    /// <summary>
    /// 用于测试的假震动实现，记录方法调用。
    /// </summary>
    private sealed class FakeHapticsImpl : IPlatformHaptics
    {
        public bool VibrateCalled { get; private set; }
        public int LastDurationMs { get; private set; }
        public bool CancelCalled { get; private set; }
        public NotificationType? LastNotificationType { get; private set; }

        public void Vibrate(int durationMs)
        {
            VibrateCalled = true;
            LastDurationMs = durationMs;
        }

        public void Cancel()
        {
            CancelCalled = true;
        }

        public void Notify(NotificationType type)
        {
            LastNotificationType = type;
        }
    }
}
