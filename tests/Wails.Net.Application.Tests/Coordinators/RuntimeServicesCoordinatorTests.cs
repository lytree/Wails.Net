using TUnit.Core;
using Wails.Net.Application.Coordinators;
using Wails.Net.Application.Options;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Tests.Coordinators;

/// <summary>
/// <see cref="RuntimeServicesCoordinator"/> 的单元测试（TUnit）。
/// 对应审计 P1-1：服务生命周期顺序管理收敛到 coordinator，
/// 启动按注册顺序、关闭按注册逆序。
/// </summary>
[NotInParallel]
public sealed class RuntimeServicesCoordinatorTests
{
    /// <summary>
    /// 记录启停调用的测试服务（实现 IServiceStartup + IServiceShutdown）。
    /// </summary>
    private sealed class TrackingService : IServiceStartup, IServiceShutdown
    {
        public string Name { get; }
        private readonly List<string> _calls;

        public TrackingService(string name, List<string> calls)
        {
            Name = name;
            _calls = calls;
        }

        public Task ServiceStartup(ApplicationOptions options, CancellationToken cancellationToken)
        {
            _calls.Add($"start:{Name}");
            return Task.CompletedTask;
        }

        public Task ServiceShutdown(CancellationToken cancellationToken)
        {
            _calls.Add($"stop:{Name}");
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task StartAsync_CallsServiceStartup_InRegistrationOrder()
    {
        // 安排：注册三个服务（A、B、C）
        var registry = new ServiceRegistry();
        var calls = new List<string>();
        registry.Register(new TrackingService("A", calls));
        registry.Register(new TrackingService("B", calls));
        registry.Register(new TrackingService("C", calls));
        var coordinator = new RuntimeServicesCoordinator(registry);

        // 操作
        await coordinator.StartAsync(new ApplicationOptions(), CancellationToken.None);

        // 断言：按注册顺序启动
        await Assert.That(calls.SequenceEqual(new[] { "start:A", "start:B", "start:C" })).IsTrue();
    }

    [Test]
    public async Task StopAsync_CallsServiceShutdown_InReverseRegistrationOrder()
    {
        // 安排
        var registry = new ServiceRegistry();
        var calls = new List<string>();
        registry.Register(new TrackingService("A", calls));
        registry.Register(new TrackingService("B", calls));
        registry.Register(new TrackingService("C", calls));
        var coordinator = new RuntimeServicesCoordinator(registry);

        // 操作
        await coordinator.StopAsync(CancellationToken.None);

        // 断言：逆序关闭
        await Assert.That(calls.SequenceEqual(new[] { "stop:C", "stop:B", "stop:A" })).IsTrue();
    }

    [Test]
    public async Task StartStop_NonLifecycleServices_AreSkipped()
    {
        // 安排：注册一个不实现生命周期接口的普通对象
        var registry = new ServiceRegistry();
        var calls = new List<string>();
        registry.Register(new object());
        registry.Register(new TrackingService("A", calls));
        var coordinator = new RuntimeServicesCoordinator(registry);

        // 操作
        await coordinator.StartAsync(new ApplicationOptions(), CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);

        // 断言：仅生命周期服务被调用
        await Assert.That(calls.SequenceEqual(new[] { "start:A", "stop:A" })).IsTrue();
    }

    [Test]
    public async Task Name_IsStableIdentifier()
    {
        var coordinator = new RuntimeServicesCoordinator(new ServiceRegistry());
        await Assert.That(coordinator.Name).IsEqualTo("runtime-services");
    }
}
