using TUnit.Core;
using Wails.Net.Application.Coordinators;
using Wails.Net.Application.Tests.Stubs;

namespace Wails.Net.Application.Tests.Coordinators;

/// <summary>
/// <see cref="FrontendRuntimeCoordinator"/> 的单元测试（TUnit）。
/// 对应 RH-HOST-02：runtime JS 生成从 <see cref="Application"/> 抽出后，
/// <c>window._wails</c> 标志（platform / isDebug / isServerMode / streamFactory）必须保持原样。
/// 端到端契约（注入到资源服务器、前端读取）由 <c>StreamRuntimeJsTests</c> / <c>ServerModeEventApiTests</c> 覆盖。
/// </summary>
public sealed class FrontendRuntimeCoordinatorTests
{
    private readonly FrontendRuntimeCoordinator _coordinator = new();

    [Test]
    public async Task ServerMode_NoPlatform_InjectsServerFlagsAndStreamFactory()
    {
        var js = _coordinator.GenerateRuntimeJs(platformApp: null, transport: null, isDebug: false);

        await Assert.That(js).Contains("platform: \"server\"");
        await Assert.That(js).Contains("isServerMode: true");
        await Assert.That(js).Contains("isDebug: false");
        await Assert.That(js).Contains("window._wails.streamFactory");
    }

    [Test]
    public async Task DesktopMode_OmitsStreamFactory()
    {
        var platform = new StubPlatformApp();

        var js = _coordinator.GenerateRuntimeJs(platform, transport: null, isDebug: true);

        await Assert.That(js).Contains("isServerMode: false");
        await Assert.That(js).Contains("isDebug: true");
        await Assert.That(js).DoesNotContain("streamFactory");
    }

    [Test]
    public async Task DesktopMode_PlatformFlagMatchesHostOs()
    {
        var platform = new StubPlatformApp();

        var js = _coordinator.GenerateRuntimeJs(platform, transport: null, isDebug: false);

        var expected = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsLinux() ? "linux"
            : "unknown";

        await Assert.That(js).Contains($"platform: \"{expected}\"");
    }
}
