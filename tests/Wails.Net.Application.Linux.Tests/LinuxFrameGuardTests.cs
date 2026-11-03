using TUnit.Core;
using Wails.Net.Application.Platform;

namespace Wails.Net.Application.Linux.Tests;

/// <summary>
/// <see cref="LinuxFrameGuard"/> 的单元测试（TUnit），覆盖 Linux 跨源子帧消息拦截的
/// 守卫脚本生成（§11.2 第 3 项 / GHSA-2p5f-2gx8-mh8m 缓解）。
/// 全部为纯字符串断言，不触发任何 GTK/WebKit 原生调用，可在任意平台执行。
/// </summary>
[NotInParallel]
public sealed class LinuxFrameGuardTests
{
    [Test]
    public async Task GetGuardScript_IsSelfExecutingFunction()
    {
        var script = LinuxFrameGuard.GetGuardScript();

        await Assert.That(script.Contains("(function ()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(script.TrimEnd().EndsWith("})();", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task GetGuardScript_WritesTrustFlag()
    {
        var script = LinuxFrameGuard.GetGuardScript();

        await Assert.That(script.Contains(LinuxFrameGuard.TrustedFlagName, StringComparison.Ordinal)).IsTrue();
        await Assert.That(script.Contains("window.__wailsFrameTrusted = trusted;", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task GetGuardScript_DetectsCrossOriginSubframe()
    {
        var script = LinuxFrameGuard.GetGuardScript();

        // 顶层帧判定 + 同源比较（跨源读取 location 抛异常时视为不可信）。
        await Assert.That(script.Contains("window.top !== window.self", StringComparison.Ordinal)).IsTrue();
        await Assert.That(
            script.Contains(
                "window.top.location.origin === window.location.origin",
                StringComparison.Ordinal)).IsTrue();
        await Assert.That(script.Contains("trusted = false;", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task GetGuardScript_NeutralizesRuntimeFlagsInUntrustedFrame()
    {
        var script = LinuxFrameGuard.GetGuardScript();

        await Assert.That(script.Contains("Object.defineProperty(window, '_wails'", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task GetGuardScript_BlocksFetchToWailsScheme()
    {
        var script = LinuxFrameGuard.GetGuardScript();

        await Assert.That(script.Contains("url.indexOf('wails:') === 0", StringComparison.Ordinal)).IsTrue();
        await Assert.That(script.Contains("Promise.reject", StringComparison.Ordinal)).IsTrue();
        await Assert.That(script.Contains("originalFetch.apply(window, arguments)", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task GetGuardScript_BlocksXmlHttpRequestToWailsScheme()
    {
        var script = LinuxFrameGuard.GetGuardScript();

        await Assert.That(script.Contains("XMLHttpRequest.prototype.open", StringComparison.Ordinal)).IsTrue();
        await Assert.That(script.Contains("originalOpen.apply(this, arguments)", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task GetGuardScript_BlocksSendBeaconToWailsScheme()
    {
        var script = LinuxFrameGuard.GetGuardScript();

        await Assert.That(script.Contains("navigator.sendBeacon", StringComparison.Ordinal)).IsTrue();
        await Assert.That(script.Contains("originalBeacon(url, data)", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task WrapRuntimeScript_ContainsTrustConditionAndOriginalScript()
    {
        const string runtimeJs = "(function(){window._wails={platform:'linux'};})();";

        var wrapped = LinuxFrameGuard.WrapRuntimeScript(runtimeJs);

        await Assert.That(
            wrapped.Contains("if (window.__wailsFrameTrusted !== false)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(wrapped.Contains(runtimeJs, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task WrapRuntimeScript_Null_ThrowsArgumentNullException()
    {
        await Assert.That(() => LinuxFrameGuard.WrapRuntimeScript(null!))
            .ThrowsExactly<ArgumentNullException>();
    }
}
