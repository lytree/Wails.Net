using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;

namespace Wails.Net.Application.Tests.Hosting;

/// <summary>
/// <see cref="DesktopHostedService"/> 的单元测试（TUnit）。
/// 验证生命周期适配：StartAsync 启动 UI 线程、StopAsync 触发 Shutdown、Dispose 触发 Shutdown。
/// 使用 NSubstitute 模拟 <see cref="Application"/> 以避免启动真实平台主循环。
/// 注：使用 <see cref="NullLogger{T}"/> 代替 NSubstitute 模拟 ILogger，
/// 因为 DesktopHostedService 是 internal 类型，DynamicProxy 无法为强命名程序集中的
/// ILogger&lt;T&gt; 创建代理。
/// </summary>
[NotInParallel]
public sealed class DesktopHostedServiceTests
{
    private static IHostApplicationLifetime CreateLifetime()
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Returns(new CancellationToken(false));
        lifetime.ApplicationStopping.Returns(new CancellationToken(false));
        lifetime.ApplicationStopped.Returns(new CancellationToken(false));
        return lifetime;
    }

    [Test]
    public async Task Constructor_AcceptsDependencies()
    {
        // 安排
        var logger = NullLogger<DesktopHostedService>.Instance;
        var application = new Application(new ApplicationOptions());
        var lifetime = CreateLifetime();

        // 操作
        var service = new DesktopHostedService(logger, application, lifetime);

        // 断言：不抛异常即构造成功
        await Assert.That(service).IsNotNull();
    }

    [Test]
    public async Task StartAsync_CompletesImmediately_DoesNotBlock()
    {
        // 安排
        var logger = NullLogger<DesktopHostedService>.Instance;
        var application = Substitute.For<Application>(new ApplicationOptions());
        // Run() 被调用时立即返回（默认 Substitute 行为：void 方法不做任何事）
        var lifetime = CreateLifetime();
        var service = new DesktopHostedService(logger, application, lifetime);

        // 操作
        await service.StartAsync(CancellationToken.None);

        // 断言：StartAsync 应该立即返回（不阻塞）
        // 注：UI 线程已在后台启动，会调用 application.Run()（substitute 的 no-op）
        await Assert.That(application).IsNotNull();
    }

    [Test]
    public async Task StopAsync_CallsApplicationShutdown()
    {
        // 安排
        var logger = NullLogger<DesktopHostedService>.Instance;
        var application = Substitute.For<Application>(new ApplicationOptions());
        var lifetime = CreateLifetime();
        var service = new DesktopHostedService(logger, application, lifetime);
        await service.StartAsync(CancellationToken.None);

        // 操作
        await service.StopAsync(CancellationToken.None);

        // 断言：StopAsync 应触发 Application.Shutdown
        await Assert.That(() => application.Received(1).Shutdown())
            .ThrowsNothing();
    }

    [Test]
    public async Task Dispose_CallsApplicationShutdown()
    {
        // 安排
        var logger = NullLogger<DesktopHostedService>.Instance;
        var application = Substitute.For<Application>(new ApplicationOptions());
        var lifetime = CreateLifetime();
        var service = new DesktopHostedService(logger, application, lifetime);

        // 操作
        service.Dispose();

        // 断言：Dispose 应触发 Application.Shutdown
        await Assert.That(() => application.Received(1).Shutdown())
            .ThrowsNothing();
    }

    [Test]
    public async Task StartAsync_NotifiesHostStop_WhenUiThreadExits()
    {
        // 安排
        var logger = NullLogger<DesktopHostedService>.Instance;
        var application = Substitute.For<Application>(new ApplicationOptions());
        // Run() 立即返回 → UI 线程立即退出 → 应通知 Host 停止
        var lifetime = CreateLifetime();
        var service = new DesktopHostedService(logger, application, lifetime);

        // 操作
        await service.StartAsync(CancellationToken.None);
        // 给 UI 线程时间执行（Run() 是 no-op，立即退出）
        await Task.Delay(300);

        // 断言：UI 线程退出后调用 _lifetime.StopApplication()
        await Assert.That(() => lifetime.Received(1).StopApplication())
            .ThrowsNothing();
    }
}
