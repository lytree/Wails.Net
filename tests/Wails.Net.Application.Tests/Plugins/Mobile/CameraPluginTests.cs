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
/// CameraPlugin 的单元测试（TUnit）。
/// 对应 Tauri v2 camera 插件功能。
/// 验证命令注册、降级路径（NullCameraImpl 返回 none / 空数组）、自定义实现注入与 Base64 编码。
/// </summary>
[NotInParallel]
public sealed class CameraPluginTests
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
    public async Task Name_ReturnsCamera()
    {
        var plugin = new CameraPlugin();
        await Assert.That(plugin.Name).IsEqualTo("camera");
    }

    [Test]
    public async Task Configure_NullContext_ThrowsArgumentNullException()
    {
        var plugin = new CameraPlugin();
        await Assert.That(() => plugin.Configure(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ConfigureServices_NullServices_ThrowsArgumentNullException()
    {
        var plugin = new CameraPlugin();
        await Assert.That(() => plugin.ConfigureServices(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ConfigureServices_RegistersDefaultCameraImpl()
    {
        var plugin = new CameraPlugin();
        var services = new ServiceCollection();

        plugin.ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        var impl = provider.GetService<IPlatformCamera>();
        await Assert.That(impl).IsNotNull();
    }

    [Test]
    public async Task Configure_RegistersAllCommands()
    {
        var plugin = new CameraPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);

        plugin.Configure(context);

        await Assert.That(context.Commands.Find("camera.checkAvailability")).IsNotNull();
        await Assert.That(context.Commands.Find("camera.capture")).IsNotNull();
        await Assert.That(context.Commands.Find("camera.cancel")).IsNotNull();
    }

    // ---------------------------------------------------------------------
    // 降级路径测试（NullCameraImpl no-op）
    // ---------------------------------------------------------------------

    [Test]
    public async Task CheckAvailability_WithDefaultImpl_ReturnsNone()
    {
        var plugin = new CameraPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        var result = (string)InvokeCommand(context.Commands, "camera.checkAvailability", cmdCtx)!;

        await Assert.That(result).IsEqualTo("none");
    }

    [Test]
    public async Task Capture_WithDefaultImpl_ReturnsFailedResult()
    {
        var plugin = new CameraPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        var result = await (Task<CameraCaptureResult>)InvokeCommand(
            context.Commands, "camera.capture", cmdCtx)!;

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Base64Data).IsEmpty();
    }

    [Test]
    public async Task Cancel_WithDefaultImpl_DoesNotThrow()
    {
        var plugin = new CameraPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        await Assert.That(() => InvokeCommand(context.Commands, "camera.cancel", cmdCtx)).ThrowsNothing();
    }

    // ---------------------------------------------------------------------
    // 自定义实现注入测试
    // ---------------------------------------------------------------------

    [Test]
    public async Task CheckAvailability_WithCustomImpl_ReturnsAvailable()
    {
        var customImpl = new FakeCameraImpl { AvailabilityResult = "available" };
        var plugin = new CameraPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformCamera>();
        services.AddSingleton<IPlatformCamera>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        var result = (string)InvokeCommand(context.Commands, "camera.checkAvailability", cmdCtx)!;

        await Assert.That(result).IsEqualTo("available");
        await Assert.That(customImpl.CheckCalled).IsTrue();
    }

    [Test]
    public async Task Capture_WithCustomImpl_ReturnsBase64EncodedData()
    {
        // 模拟 JPEG 字节数据
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        var customImpl = new FakeCameraImpl { CapturedBytes = jpegBytes };
        var plugin = new CameraPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformCamera>();
        services.AddSingleton<IPlatformCamera>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        var result = await (Task<CameraCaptureResult>)InvokeCommand(
            context.Commands, "camera.capture", cmdCtx)!;

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Error).IsNull();
        await Assert.That(customImpl.CaptureCalled).IsTrue();
        // 验证 Base64 编码正确
        var expectedBase64 = Convert.ToBase64String(jpegBytes);
        await Assert.That(result.Base64Data).IsEqualTo(expectedBase64);
    }

    [Test]
    public async Task Cancel_WithCustomImpl_InvokesCancel()
    {
        var customImpl = new FakeCameraImpl();
        var plugin = new CameraPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformCamera>();
        services.AddSingleton<IPlatformCamera>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        InvokeCommand(context.Commands, "camera.cancel", cmdCtx);

        await Assert.That(customImpl.CancelCalled).IsTrue();
    }

    /// <summary>
    /// 用于测试的假相机实现，记录方法调用。
    /// </summary>
    private sealed class FakeCameraImpl : IPlatformCamera
    {
        public string AvailabilityResult { get; set; } = "none";
        public byte[] CapturedBytes { get; set; } = Array.Empty<byte>();
        public bool CheckCalled { get; private set; }
        public bool CaptureCalled { get; private set; }
        public bool CancelCalled { get; private set; }

        public string CheckAvailability()
        {
            CheckCalled = true;
            return AvailabilityResult;
        }

        public Task<byte[]> CaptureAsync(CancellationToken cancellationToken)
        {
            CaptureCalled = true;
            return Task.FromResult(CapturedBytes);
        }

        public void Cancel()
        {
            CancelCalled = true;
        }
    }
}
