using NSubstitute;
using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Platform;
using Wails.Net.Application.Platform.ServerMode;

namespace Wails.Net.Application.Tests.Streams;

/// <summary>
/// Streams P0-1：验证 <see cref="Application.GenerateRuntimeJs"/> 在 server 模式注入
/// <c>window._wails.streamFactory</c>（对应 Wails v3 Go 版本 <c>stream_prelude_server.go</c>），
/// 桌面模式则不注入（走 held-poll，由 <c>@wails-net/runtime</c> 的 <c>Stream(name)</c> 提供）。
/// </summary>
[NotInParallel]
public sealed class StreamRuntimeJsTests
{
    /// <summary>
    /// server 模式：应注入 streamFactory，且指向 <c>/wails/stream/ws</c> 端点。
    /// </summary>
    [Test]
    public async Task GenerateRuntimeJs_ServerMode_InjectsStreamFactory()
    {
        var options = new ApplicationOptions { Name = "StreamServer" };
        var app = new Application(options);
        app.SetPlatformApp(new ServerPlatformApp(options));

        var js = app.GenerateRuntimeJs(false);

        await Assert.That(js.Contains("streamFactory", StringComparison.Ordinal)).IsTrue();
        await Assert.That(js.Contains("/wails/stream/ws", StringComparison.Ordinal)).IsTrue();
        await Assert.That(js.Contains("isServerMode: true", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>
    /// 桌面模式：不应注入 streamFactory（由前端 runtime 的 held-poll 实现承担）。
    /// </summary>
    [Test]
    public async Task GenerateRuntimeJs_DesktopMode_DoesNotInjectStreamFactory()
    {
        var options = new ApplicationOptions { Name = "StreamDesktop" };
        var app = new Application(options);
        app.SetPlatformApp(Substitute.For<IPlatformApp>());

        var js = app.GenerateRuntimeJs(false);

        await Assert.That(js.Contains("streamFactory", StringComparison.Ordinal)).IsFalse();
        await Assert.That(js.Contains("isServerMode: false", StringComparison.Ordinal)).IsTrue();
    }
}
