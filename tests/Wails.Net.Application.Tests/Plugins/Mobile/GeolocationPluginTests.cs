using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using Wails.Net.Application.Plugins.Mobile;
using Wails.Net.Plugins.Mobile;

namespace Wails.Net.Application.Tests.Plugins.Mobile;

/// <summary>
/// GeolocationPlugin 的单元测试（TUnit）。
/// 对应 Tauri v2 geolocation 插件功能。
/// 验证命令注册、降级路径（NullGeolocationImpl 返回 none/null）、自定义实现注入与参数传递。
/// </summary>
[NotInParallel]
public sealed class GeolocationPluginTests
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
    public async Task Name_ReturnsGeolocation()
    {
        var plugin = new GeolocationPlugin();
        await Assert.That(plugin.Name).IsEqualTo("geolocation");
    }

    [Test]
    public async Task Configure_NullContext_ThrowsArgumentNullException()
    {
        var plugin = new GeolocationPlugin();
        await Assert.That(() => plugin.Configure(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ConfigureServices_NullServices_ThrowsArgumentNullException()
    {
        var plugin = new GeolocationPlugin();
        await Assert.That(() => plugin.ConfigureServices(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Configure_RegistersAllCommands()
    {
        // 安排
        var plugin = new GeolocationPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);

        // 操作
        plugin.Configure(context);

        // 断言
        await Assert.That(context.Commands.Find("geolocation.checkAvailability")).IsNotNull();
        await Assert.That(context.Commands.Find("geolocation.getCurrentPosition")).IsNotNull();
        await Assert.That(context.Commands.Find("geolocation.watchPosition")).IsNotNull();
        await Assert.That(context.Commands.Find("geolocation.clearWatch")).IsNotNull();
    }

    [Test]
    public async Task CheckAvailability_WithDefaultImpl_ReturnsNone()
    {
        // 安排
        var plugin = new GeolocationPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作
        var result = InvokeCommand(context.Commands, "geolocation.checkAvailability", cmdCtx);

        // 断言：默认 NullGeolocationImpl.CheckAvailability 返回 "none"
        await Assert.That(result).IsEqualTo("none");
    }

    [Test]
    public async Task GetCurrentPosition_WithDefaultImpl_ReturnsNull()
    {
        // 安排
        var plugin = new GeolocationPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作
        var result = await (Task<GeolocationPosition?>)InvokeCommand(
            context.Commands, "geolocation.getCurrentPosition", cmdCtx,
            new GeolocationOptions())!;

        // 断言：默认 NullGeolocationImpl.GetCurrentPositionAsync 返回 null
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task CheckAvailability_WithCustomImpl_ReturnsAvailable()
    {
        // 安排
        var customImpl = new FakeGeolocationImpl { AvailabilityResult = "available" };
        var plugin = new GeolocationPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformGeolocation>();
        services.AddSingleton<IPlatformGeolocation>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作
        var result = InvokeCommand(context.Commands, "geolocation.checkAvailability", cmdCtx);

        // 断言
        await Assert.That(result).IsEqualTo("available");
        await Assert.That(customImpl.CheckCalled).IsTrue();
    }

    [Test]
    public async Task GetCurrentPosition_WithCustomImpl_ReturnsPosition()
    {
        // 安排
        var expectedPosition = new GeolocationPosition
        {
            Coords = new GeolocationCoords { Latitude = 39.9, Longitude = 116.4, Accuracy = 10 },
            Timestamp = 1700000000000
        };
        var customImpl = new FakeGeolocationImpl { PositionResult = expectedPosition };
        var plugin = new GeolocationPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformGeolocation>();
        services.AddSingleton<IPlatformGeolocation>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作
        var result = await (Task<GeolocationPosition?>)InvokeCommand(
            context.Commands, "geolocation.getCurrentPosition", cmdCtx,
            new GeolocationOptions { EnableHighAccuracy = true })!;

        // 断言
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Coords.Latitude).IsEqualTo(39.9);
        await Assert.That(result.Coords.Longitude).IsEqualTo(116.4);
        await Assert.That(customImpl.GetPositionCalled).IsTrue();
        await Assert.That(customImpl.LastOptions?.EnableHighAccuracy).IsTrue();
    }

    [Test]
    public async Task ClearWatch_WithDefaultImpl_DoesNotThrow()
    {
        // 安排
        var plugin = new GeolocationPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作与断言：clearWatch 在 NullGeolocationImpl 上为 no-op
        await Assert.That(() => InvokeCommand(
            context.Commands, "geolocation.clearWatch", cmdCtx,
            new WatchPositionResult { WatchId = 1 })).ThrowsNothing();
    }

    /// <summary>
    /// 用于测试的自定义地理定位实现，记录调用状态。
    /// </summary>
    private sealed class FakeGeolocationImpl : IPlatformGeolocation
    {
        public string AvailabilityResult { get; set; } = "none";
        public GeolocationPosition? PositionResult { get; set; }
        public bool CheckCalled { get; private set; }
        public bool GetPositionCalled { get; private set; }
        public bool WatchCalled { get; private set; }
        public bool ClearCalled { get; private set; }
        public GeolocationOptions? LastOptions { get; private set; }
        public int LastClearedWatchId { get; private set; }

        public string CheckAvailability()
        {
            CheckCalled = true;
            return AvailabilityResult;
        }

        public Task<GeolocationPosition?> GetCurrentPositionAsync(GeolocationOptions options, CancellationToken cancellationToken)
        {
            GetPositionCalled = true;
            LastOptions = options;
            return Task.FromResult(PositionResult);
        }

        public Task<int> WatchPositionAsync(GeolocationOptions options, Action<GeolocationPosition> callback, CancellationToken cancellationToken)
        {
            WatchCalled = true;
            LastOptions = options;
            return Task.FromResult(42);
        }

        public void ClearWatch(int watchId)
        {
            ClearCalled = true;
            LastClearedWatchId = watchId;
        }
    }
}
