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
/// BarcodeScannerPlugin 的单元测试（TUnit）。
/// 对应 Tauri v2 barcode-scanner 插件功能。
/// 验证命令注册、降级路径（NullBarcodeScannerImpl 返回空字符串）、自定义实现注入。
/// </summary>
[NotInParallel]
public sealed class BarcodeScannerPluginTests
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
    public async Task Name_ReturnsBarcodeScanner()
    {
        var plugin = new BarcodeScannerPlugin();
        await Assert.That(plugin.Name).IsEqualTo("barcode-scanner");
    }

    [Test]
    public async Task Configure_NullContext_ThrowsArgumentNullException()
    {
        var plugin = new BarcodeScannerPlugin();
        await Assert.That(() => plugin.Configure(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Configure_RegistersAllCommands()
    {
        // 安排
        var plugin = new BarcodeScannerPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);

        // 操作
        plugin.Configure(context);

        // 断言
        await Assert.That(context.Commands.Find("barcode-scanner.scan")).IsNotNull();
        await Assert.That(context.Commands.Find("barcode-scanner.cancel")).IsNotNull();
    }

    [Test]
    public async Task Scan_WithDefaultImpl_ReturnsEmptyString()
    {
        // 安排
        var plugin = new BarcodeScannerPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作：命令返回 Task<string>，需 await 后获取实际结果
        var result = await (Task<string>)InvokeCommand(context.Commands, "barcode-scanner.scan", cmdCtx)!;

        // 断言：默认 NullBarcodeScannerImpl.ScanAsync 返回空字符串
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Cancel_WithDefaultImpl_DoesNotThrow()
    {
        // 安排
        var plugin = new BarcodeScannerPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作与断言
        await Assert.That(() => InvokeCommand(context.Commands, "barcode-scanner.cancel", cmdCtx)).ThrowsNothing();
    }

    [Test]
    public async Task Scan_WithCustomImpl_ReturnsExpectedValue()
    {
        // 安排：使用自定义实现返回 "9783-1234"
        var customImpl = new FakeBarcodeScannerImpl("9783-1234");
        var plugin = new BarcodeScannerPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformBarcodeScanner>();
        services.AddSingleton<IPlatformBarcodeScanner>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作
        var result = await (Task<string>)InvokeCommand(context.Commands, "barcode-scanner.scan", cmdCtx)!;

        // 断言
        await Assert.That(result).IsEqualTo("9783-1234");
        await Assert.That(customImpl.ScanCalled).IsTrue();
    }

    private sealed class FakeBarcodeScannerImpl : IPlatformBarcodeScanner
    {
        private readonly string _scanResult;

        public FakeBarcodeScannerImpl(string scanResult)
        {
            _scanResult = scanResult;
        }

        public bool ScanCalled { get; private set; }
        public bool CancelCalled { get; private set; }

        public Task<string> ScanAsync()
        {
            ScanCalled = true;
            return Task.FromResult(_scanResult);
        }

        public void Cancel()
        {
            CancelCalled = true;
        }
    }
}
