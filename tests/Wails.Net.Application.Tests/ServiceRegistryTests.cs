using Wails.Net.Application.Services;

namespace Wails.Net.Application.Tests;

/// <summary>
/// ServiceRegistry 的单元测试（TUnit）。
/// </summary>
public sealed class ServiceRegistryTests
{
    /// <summary>
    /// 用于测试的简单服务类。
    /// </summary>
    private class TestService { }

    [Test]
    public async Task Register_AddsServiceToCollection()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service = new TestService();

        // 操作
        registry.Register(service);

        // 断言
        await Assert.That(registry.Services.Count).IsEqualTo(1);
        await Assert.That(registry.Services[0]).IsSameReferenceAs(service);
    }

    [Test]
    public async Task Unregister_RemovesServiceFromCollection()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service = new TestService();
        registry.Register(service);

        // 操作
        registry.Unregister(service);

        // 断言
        await Assert.That(registry.Services.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetService_ReturnsCorrectType()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service = new TestService();
        registry.Register(service);

        // 操作
        var result = registry.GetService<TestService>();

        // 断言
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsSameReferenceAs(service);
    }

    [Test]
    public async Task GetService_ReturnsNullWhenNotFound()
    {
        // 安排
        var registry = new ServiceRegistry();

        // 操作
        var result = registry.GetService<TestService>();

        // 断言
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetServices_ReturnsAllMatching()
    {
        // 安排
        var registry = new ServiceRegistry();
        var service1 = new TestService();
        var service2 = new TestService();
        registry.Register(service1);
        registry.Register(service2);

        // 操作
        var result = registry.GetServices<TestService>().ToList();

        // 断言
        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0]).IsSameReferenceAs(service1);
        await Assert.That(result[1]).IsSameReferenceAs(service2);
    }

    [Test]
    public async Task Clear_RemovesAllServices()
    {
        // 安排
        var registry = new ServiceRegistry();
        registry.Register(new TestService());
        registry.Register(new TestService());

        // 操作
        registry.Clear();

        // 断言
        await Assert.That(registry.Services.Count).IsEqualTo(0);
    }
}
