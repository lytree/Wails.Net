using TUnit.Core;
using Wails.Net.Application.Hosting;
using Wails.Net.Application.Options;
using Wails.Net.Application.Tests.Stubs;

namespace Wails.Net.Application.Tests.Hosting;

/// <summary>
/// <see cref="SingleInstanceCoordinator"/> 的单元测试（TUnit）。
/// 对应 RH-HOST-02：单实例检查从 <c>Application.Run</c> 抽出后，
/// 首实例/非首实例分支与退出码语义必须保持不变。
/// </summary>
public sealed class SingleInstanceCoordinatorTests
{
    private static ApplicationOptions CreateOptions(bool singleInstance, int exitCode = 0)
        => new()
        {
            Name = "single-instance-test",
            SingleInstance = singleInstance,
            SingleInstanceExitCode = exitCode,
        };

    [Test]
    public async Task Acquire_WhenDisabled_DoesNotTouchPlatform()
    {
        var platform = new StubPlatformApp { SingleInstanceLockResult = false };
        var exited = -1;
        var coordinator = new SingleInstanceCoordinator(CreateOptions(singleInstance: false), code => exited = code);

        coordinator.Acquire(platform);

        await Assert.That(coordinator.IsFirstInstance).IsTrue();
        await Assert.That(exited).IsEqualTo(-1);
    }

    [Test]
    public async Task Acquire_WhenFirstInstance_KeepsRunning()
    {
        var platform = new StubPlatformApp { SingleInstanceLockResult = true };
        var exited = -1;
        var coordinator = new SingleInstanceCoordinator(CreateOptions(singleInstance: true), code => exited = code);

        coordinator.Acquire(platform);

        await Assert.That(coordinator.IsFirstInstance).IsTrue();
        await Assert.That(exited).IsEqualTo(-1);
        await Assert.That(platform.NotifiedArgs).IsNull();
    }

    [Test]
    public async Task Acquire_WhenNotFirstInstance_NotifiesAndExitsWithConfiguredCode()
    {
        var platform = new StubPlatformApp { SingleInstanceLockResult = false };
        var exited = -1;
        var coordinator = new SingleInstanceCoordinator(
            CreateOptions(singleInstance: true, exitCode: 42),
            code => exited = code);

        coordinator.Acquire(platform);

        await Assert.That(coordinator.IsFirstInstance).IsFalse();
        await Assert.That(exited).IsEqualTo(42);
        await Assert.That(platform.NotifiedArgs).IsNotNull();
        await Assert.That(platform.NotifiedArgs!.Length > 0).IsTrue();
    }

    [Test]
    public async Task Acquire_WithoutPlatform_TreatsAsFirstInstance()
    {
        var exited = -1;
        var coordinator = new SingleInstanceCoordinator(CreateOptions(singleInstance: true), code => exited = code);

        coordinator.Acquire(null);

        await Assert.That(coordinator.IsFirstInstance).IsTrue();
        await Assert.That(exited).IsEqualTo(-1);
    }
}
