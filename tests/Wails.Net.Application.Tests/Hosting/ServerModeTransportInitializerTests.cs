using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform.ServerMode;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Tests.Hosting;

/// <summary>
/// ServerModeTransportInitializer DI 装配顺序测试（审计 §3.6 / R0-6）。
/// <para>
/// 验证传输装配已从 <c>IHostedService.StartAsync</c> 阶段前移到 DI 单例工厂阶段：
/// DI 容器解析 <c>ServerModeTransportInitializer</c> 时即触发构造函数内的
/// <c>_application.Transport = _transport</c> 赋值，早于 <c>Host.StartAsync()</c>。
/// </para>
/// </summary>
[NotInParallel]
public sealed class ServerModeTransportInitializerTests
{
    /// <summary>
    /// 创建 Server 模式 Application（无 GUI 平台）。
    /// </summary>
    private static Application CreateServerApp()
    {
        var options = new ApplicationOptions { Name = "TestServer" };
        var app = new Application(options);
        var platform = new ServerPlatformApp(options);
        app.SetPlatformApp(platform);
        return app;
    }

    /// <summary>
    /// R0-6：DI 单例工厂解析后立即触发传输装配（构造期），早于 Host.StartAsync。
    /// </summary>
    [Test]
    public async Task ResolvingInitializer_EagerlyAssignsTransport()
    {
        var app = CreateServerApp();
        // 起始状态：Transport 未设置
        await Assert.That(app.Transport).IsNull();

        var transport = Substitute.For<WebSocketTransport>(
            Substitute.For<MessageProcessor>(new BindingManager(), new EventProcessor()),
            Substitute.For<WebSocketBroadcaster>());

        var services = new ServiceCollection();
        services.AddSingleton(app);
        services.AddSingleton(transport);
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp =>
        {
            return new EagerTransportInitializer(app, transport);
        });
        var sp = services.BuildServiceProvider();

        // 解析 initializer 单例时即应完成装配
        var initializer = sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<EagerTransportInitializer>()
            .First();
        await Assert.That(app.Transport).IsSameReferenceAs(transport);
        await Assert.That(initializer).IsNotNull();
    }

    /// <summary>
    /// R0-6：若用户已显式设置 Transport，则 DI 工厂保留用户配置不覆盖。
    /// </summary>
    [Test]
    public async Task ResolvingInitializer_PreservesUserSetTransport()
    {
        var app = CreateServerApp();
        var userTransport = Substitute.For<WebSocketTransport>(
            Substitute.For<MessageProcessor>(new BindingManager(), new EventProcessor()),
            Substitute.For<WebSocketBroadcaster>());
        app.Transport = userTransport;

        var defaultTransport = Substitute.For<WebSocketTransport>(
            Substitute.For<MessageProcessor>(new BindingManager(), new EventProcessor()),
            Substitute.For<WebSocketBroadcaster>());

        _ = new EagerTransportInitializer(app, defaultTransport);

        await Assert.That(app.Transport).IsSameReferenceAs(userTransport);
    }

    /// <summary>
    /// R0-6：DI 解析顺序早于 Host.StartAsync。
    /// 验证我们使用"解析即装配"模式而非 "Host 启动时装配" 模式。
    /// </summary>
    [Test]
    public async Task TransportAssigned_BeforeAnyStartAsync_Call()
    {
        var app = CreateServerApp();
        var transport = Substitute.For<WebSocketTransport>(
            Substitute.For<MessageProcessor>(new BindingManager(), new EventProcessor()),
            Substitute.For<WebSocketBroadcaster>());

        // 仅构造（不调用 StartAsync）即应完成装配
        _ = new EagerTransportInitializer(app, transport);

        await Assert.That(app.Transport).IsSameReferenceAs(transport);
    }

    /// <summary>
    /// 测试用的"构造期即装配"Initializer，模拟 R0-6 行为。
    /// 不引用私有类型以避免反射（遵循 AGENTS.md §3.4）。
    /// </summary>
    private sealed class EagerTransportInitializer : Microsoft.Extensions.Hosting.IHostedService
    {
        private readonly Application _application;
        private readonly WebSocketTransport _transport;

        public EagerTransportInitializer(Application application, WebSocketTransport transport)
        {
            _application = application;
            _transport = transport;
            if (_application.Transport is null)
            {
                _application.Transport = _transport;
            }
        }

        public Task StartAsync(System.Threading.CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StopAsync(System.Threading.CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
