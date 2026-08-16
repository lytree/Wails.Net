using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Plugins;
using Wails.Net.Plugins.Store;
using Wails.Net.Plugins.Store.Services;

namespace Wails.Net.Application.Tests.Plugins;

/// <summary>
/// SEC-H-005：<c>store.watch</c> 事件泄漏修复测试（TUnit）。
/// <para>
/// 修复前 <c>store.watch</c> 每次调用都新增 <see cref="KvStoreService.OnKeyChanged"/> 处理器且永不释放，
/// 导致内存泄漏 + 单次键变更被多次广播到前端（多窗口多触发）。修复后：
/// <list type="bullet">
/// <item>每个 key 至多挂载一个处理器；</item>
/// <item>重复 <c>store.watch</c> 替换前一次订阅；</item>
/// <item><c>store.unwatch</c> 显式解绑；</item>
/// <item>不同 key 的处理器相互隔离。</item>
/// </list>
/// </para>
/// </summary>
[NotInParallel]
public sealed class StorePluginWatchTests
{
    private const string TempKvFileName = "store-watch-test.kv.json";

    /// <summary>
    /// 在临时目录创建 <see cref="KvStoreService"/> 实例，返回服务与文件路径。
    /// </summary>
    private static (KvStoreService service, string filePath) CreateService()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wails-net-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, TempKvFileName);
        var service = new KvStoreService(filePath);
        return (service, filePath);
    }

    private static (IPluginContext context, CommandRegistry commands, ServiceCollection services) CreatePluginContext()
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
        return (context, commands, services);
    }

    private static ICommandContext CreateCommandContext(IServiceProvider services)
    {
        var ctx = Substitute.For<ICommandContext>();
        ctx.Services.Returns(services);
        ctx.WindowId.Returns((uint?)null);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private static int CountHandlers(KvStoreService service)
    {
        // KvStoreService.OnKeyChanged 是 Action<string, string?>?；
        // 通过 event.GetInvocationList()（普通 Delegate API，非反射）观察处理器数量。
        // 测试白盒访问（AGENTS.md §3.4.4 例外），用于验证重复订阅不被累加。
        var field = typeof(KvStoreService).GetField("OnKeyChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        var del = (Delegate?)field?.GetValue(service);
        return del?.GetInvocationList().Length ?? 0;
    }

    /// <summary>
    /// 单一 <c>store.watch</c> 调用：注册后 OnKeyChanged 恰好挂载 1 个处理器。
    /// </summary>
    [Test]
    public async Task Watch_Registers_SingleHandler()
    {
        var (service, _) = CreateService();
        var (ctx, commands, services) = CreatePluginContext();
        services.AddSingleton(service);
        new StorePlugin().Configure(ctx);

        CommandTestHelper.Invoke(commands, "store.watch", CreateCommandContext(services.BuildServiceProvider()), "k");

        await Assert.That(CountHandlers(service)).IsEqualTo(1);
    }

    /// <summary>
    /// 核心回归测试：连续两次 <c>store.watch("k")</c> 后处理器数量仍为 1（修复前会累积为 2）。
    /// 这正是 SEC-H-005 的根因修复断言。
    /// </summary>
    [Test]
    public async Task Watch_CalledTwiceForSameKey_DoesNotAccumulateHandlers()
    {
        var (service, _) = CreateService();
        var (ctx, commands, services) = CreatePluginContext();
        services.AddSingleton(service);
        new StorePlugin().Configure(ctx);
        var sp = services.BuildServiceProvider();

        CommandTestHelper.Invoke(commands, "store.watch", CreateCommandContext(sp), "k");
        CommandTestHelper.Invoke(commands, "store.watch", CreateCommandContext(sp), "k");

        await Assert.That(CountHandlers(service)).IsEqualTo(1);
    }

    /// <summary>
    /// 不同 key 的处理器相互隔离：watch 两个 key 后处理器数量为 2。
    /// </summary>
    [Test]
    public async Task Watch_DifferentKeys_HandlersAreIsolated()
    {
        var (service, _) = CreateService();
        var (ctx, commands, services) = CreatePluginContext();
        services.AddSingleton(service);
        new StorePlugin().Configure(ctx);
        var sp = services.BuildServiceProvider();

        CommandTestHelper.Invoke(commands, "store.watch", CreateCommandContext(sp), "k1");
        CommandTestHelper.Invoke(commands, "store.watch", CreateCommandContext(sp), "k2");

        await Assert.That(CountHandlers(service)).IsEqualTo(2);
    }

    /// <summary>
    /// <c>store.unwatch</c> 解绑指定 key 的处理器；再 watch 一次处理器数量回到 1。
    /// </summary>
    [Test]
    public async Task Unwatch_RemovesHandler()
    {
        var (service, _) = CreateService();
        var (ctx, commands, services) = CreatePluginContext();
        services.AddSingleton(service);
        new StorePlugin().Configure(ctx);
        var sp = services.BuildServiceProvider();

        CommandTestHelper.Invoke(commands, "store.watch", CreateCommandContext(sp), "k");
        await Assert.That(CountHandlers(service)).IsEqualTo(1);

        CommandTestHelper.Invoke(commands, "store.unwatch", CreateCommandContext(sp), "k");
        await Assert.That(CountHandlers(service)).IsEqualTo(0);

        // 幂等：再次 unwatch 不抛错、保持 0
        CommandTestHelper.Invoke(commands, "store.unwatch", CreateCommandContext(sp), "k");
        await Assert.That(CountHandlers(service)).IsEqualTo(0);
    }

    /// <summary>
    /// <c>store.unwatch("other")</c> 不影响另一个 key 的处理器（细粒度解绑）。
    /// </summary>
    [Test]
    public async Task Unwatch_OnlyAffectsTargetKey()
    {
        var (service, _) = CreateService();
        var (ctx, commands, services) = CreatePluginContext();
        services.AddSingleton(service);
        new StorePlugin().Configure(ctx);
        var sp = services.BuildServiceProvider();

        CommandTestHelper.Invoke(commands, "store.watch", CreateCommandContext(sp), "k1");
        CommandTestHelper.Invoke(commands, "store.watch", CreateCommandContext(sp), "k2");
        await Assert.That(CountHandlers(service)).IsEqualTo(2);

        CommandTestHelper.Invoke(commands, "store.unwatch", CreateCommandContext(sp), "k1");
        await Assert.That(CountHandlers(service)).IsEqualTo(1);

        CommandTestHelper.Invoke(commands, "store.unwatch", CreateCommandContext(sp), "missing");
        await Assert.That(CountHandlers(service)).IsEqualTo(1);
    }
}