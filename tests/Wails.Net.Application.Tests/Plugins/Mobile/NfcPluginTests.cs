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
/// NfcPlugin 的单元测试（TUnit）。
/// 对应 Tauri v2 nfc 插件功能。
/// 验证命令注册、降级路径（NullNfcImpl）、自定义实现注入与参数传递。
/// </summary>
[NotInParallel]
public sealed class NfcPluginTests
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
    public async Task Name_ReturnsNfc()
    {
        var plugin = new NfcPlugin();
        await Assert.That(plugin.Name).IsEqualTo("nfc");
    }

    [Test]
    public async Task Configure_NullContext_ThrowsArgumentNullException()
    {
        var plugin = new NfcPlugin();
        await Assert.That(() => plugin.Configure(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Configure_RegistersAllCommands()
    {
        // 安排
        var plugin = new NfcPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);

        // 操作
        plugin.Configure(context);

        // 断言
        await Assert.That(context.Commands.Find("nfc.read")).IsNotNull();
        await Assert.That(context.Commands.Find("nfc.write")).IsNotNull();
        await Assert.That(context.Commands.Find("nfc.cancel")).IsNotNull();
    }

    [Test]
    public async Task Read_WithDefaultImpl_ReturnsEmptyString()
    {
        // 安排
        var plugin = new NfcPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作：命令返回 Task<string>，需 await 后获取实际结果
        var result = await (Task<string>)InvokeCommand(context.Commands, "nfc.read", cmdCtx)!;

        // 断言
        await Assert.That(result).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Write_WithDefaultImpl_DoesNotThrow()
    {
        // 安排
        var plugin = new NfcPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作与断言
        await Assert.That(() => InvokeCommand(context.Commands, "nfc.write", cmdCtx,
            new NfcWriteOptions { Data = "hello" })).ThrowsNothing();
    }

    [Test]
    public async Task Cancel_WithDefaultImpl_DoesNotThrow()
    {
        // 安排
        var plugin = new NfcPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作与断言
        await Assert.That(() => InvokeCommand(context.Commands, "nfc.cancel", cmdCtx)).ThrowsNothing();
    }

    [Test]
    public async Task Write_WithCustomImpl_PassesDataCorrectly()
    {
        // 安排：使用自定义实现捕获写入的数据
        var customImpl = new FakeNfcImpl();
        var plugin = new NfcPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformNfc>();
        services.AddSingleton<IPlatformNfc>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作
        await (Task)InvokeCommand(context.Commands, "nfc.write", cmdCtx, new NfcWriteOptions { Data = "tag-data-123" })!;

        // 断言
        await Assert.That(customImpl.WriteCalled).IsTrue();
        await Assert.That(customImpl.LastWrittenData).IsEqualTo("tag-data-123");
    }

    [Test]
    public async Task Read_WithCustomImpl_ReturnsExpectedValue()
    {
        // 安排
        var customImpl = new FakeNfcImpl { ReadResult = "nfc-payload" };
        var plugin = new NfcPlugin();
        var (context, services) = CreatePluginContext();
        plugin.ConfigureServices(services);
        services.RemoveAll<IPlatformNfc>();
        services.AddSingleton<IPlatformNfc>(customImpl);
        plugin.Configure(context);
        var provider = services.BuildServiceProvider();
        var cmdCtx = CreateCommandContext(provider);

        // 操作
        var result = await (Task<string>)InvokeCommand(context.Commands, "nfc.read", cmdCtx)!;

        // 断言
        await Assert.That(result).IsEqualTo("nfc-payload");
    }

    private sealed class FakeNfcImpl : IPlatformNfc
    {
        public string ReadResult { get; set; } = string.Empty;
        public bool ReadCalled { get; private set; }
        public bool WriteCalled { get; private set; }
        public string? LastWrittenData { get; private set; }
        public bool CancelCalled { get; private set; }

        public Task<string> ReadAsync()
        {
            ReadCalled = true;
            return Task.FromResult(ReadResult);
        }

        public Task WriteAsync(string data)
        {
            WriteCalled = true;
            LastWrittenData = data;
            return Task.CompletedTask;
        }

        public void Cancel()
        {
            CancelCalled = true;
        }
    }
}
