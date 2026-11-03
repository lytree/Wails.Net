using System.Net;
using System.Net.Sockets;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// FrontendDevServerReadiness 单元测试。
/// 对应 <c>wails dev</c> 前端 dev server 就绪闸门
/// （对齐 Wails v3 <c>v3.0.0</c> <c>internal/commands/watcher.go</c> 的 applyFrontendReadiness：
/// <c>process.Readiness{TCP: host:port, Timeout:"60s"}</c>；beta.33 / PR #6209 起由 HTTP 改 TCP）。
/// 覆盖：立即可达、先失败后成功重试、始终不可达超时、URL→TCP 目标解析，以及单次 TCP 探测的状态分类。
/// </summary>
[NotInParallel]
public sealed class FrontendDevServerReadinessTests
{
    private static readonly FrontendDevServerReadiness.ProbeTarget Target =
        new("127.0.0.1", 59999);

    // ============== WaitForReadyAsync（注入探测委托的纯逻辑） ==============

    [Test]
    public async Task WaitForReadyAsync_ImmediatelyReady_ReturnsSuccessWithoutRetry()
    {
        // Arrange
        var attempts = 0;

        Task<FrontendDevServerReadiness.ProbeResult> Probe(CancellationToken _)
        {
            attempts++;
            return Task.FromResult(new FrontendDevServerReadiness.ProbeResult(
                FrontendDevServerReadiness.ProbeStatus.Ready, "TCP 127.0.0.1:59999 已接受连接"));
        }

        // Act
        var result = await FrontendDevServerReadiness.WaitForReadyAsync(
            Probe, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10));

        // Assert
        await Assert.That(result.Ready).IsTrue();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(result.Message).Contains("已接受连接");
    }

    [Test]
    public async Task WaitForReadyAsync_FailsThenSucceeds_RetriesUntilReady()
    {
        // Arrange：前两次连接被拒，第三次就绪。
        var attempts = 0;

        Task<FrontendDevServerReadiness.ProbeResult> Probe(CancellationToken _)
        {
            attempts++;
            var outcome = attempts < 3
                ? new FrontendDevServerReadiness.ProbeResult(
                    FrontendDevServerReadiness.ProbeStatus.ConnectionRefused, "连接被拒绝")
                : new FrontendDevServerReadiness.ProbeResult(
                    FrontendDevServerReadiness.ProbeStatus.Ready, "TCP 127.0.0.1:59999 已接受连接");
            return Task.FromResult(outcome);
        }

        // Act
        var result = await FrontendDevServerReadiness.WaitForReadyAsync(
            Probe, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10));

        // Assert
        await Assert.That(result.Ready).IsTrue();
        await Assert.That(attempts).IsGreaterThanOrEqualTo(3);
        await Assert.That(result.Attempts).IsEqualTo(attempts);
    }

    [Test]
    public async Task WaitForReadyAsync_AlwaysUnreachable_TimesOutAndFails()
    {
        // Arrange：始终连接被拒，短超时避免慢测。
        Task<FrontendDevServerReadiness.ProbeResult> Probe(CancellationToken _)
            => Task.FromResult(new FrontendDevServerReadiness.ProbeResult(
                FrontendDevServerReadiness.ProbeStatus.ConnectionRefused, "连接被拒绝"));

        // Act
        var result = await FrontendDevServerReadiness.WaitForReadyAsync(
            Probe, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(10));

        // Assert
        await Assert.That(result.Ready).IsFalse();
        await Assert.That(result.LastStatus).IsEqualTo(FrontendDevServerReadiness.ProbeStatus.ConnectionRefused);
        await Assert.That(result.Attempts).IsGreaterThanOrEqualTo(2);
        await Assert.That(result.Message).Contains("超时");
        await Assert.That(result.Message).Contains("连接被拒绝");
    }

    [Test]
    public async Task WaitForReadyAsync_ExternalCancellation_Throws()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Task<FrontendDevServerReadiness.ProbeResult> Probe(CancellationToken _)
            => Task.FromResult(new FrontendDevServerReadiness.ProbeResult(
                FrontendDevServerReadiness.ProbeStatus.ConnectionRefused, "连接被拒绝"));

        // Act & Assert
        var threw = false;
        try
        {
            await FrontendDevServerReadiness.WaitForReadyAsync(
                Probe, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10), cts.Token);
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    // ============== TryCreateTarget（URL → TCP 目标，对齐 Go frontendTCPTarget） ==============

    [Test]
    public async Task TryCreateTarget_HttpWithExplicitPort_ReturnsHostAndPort()
    {
        var created = FrontendDevServerReadiness.TryCreateTarget(
            new Uri("http://localhost:5173/"), out var target);

        await Assert.That(created).IsTrue();
        await Assert.That(target.Host).IsEqualTo("localhost");
        await Assert.That(target.Port).IsEqualTo(5173);
    }

    [Test]
    public async Task TryCreateTarget_WithoutPort_UsesSchemeDefault()
    {
        // 对齐 Go：未指定端口时 http → 80、https → 443。
        var httpCreated = FrontendDevServerReadiness.TryCreateTarget(
            new Uri("http://127.0.0.1/"), out var httpTarget);
        var httpsCreated = FrontendDevServerReadiness.TryCreateTarget(
            new Uri("https://app.example.com/"), out var httpsTarget);

        await Assert.That(httpCreated).IsTrue();
        await Assert.That(httpTarget.Port).IsEqualTo(80);
        await Assert.That(httpsCreated).IsTrue();
        await Assert.That(httpsTarget.Port).IsEqualTo(443);
    }

    [Test]
    public async Task TryCreateTarget_NonHttpScheme_ReturnsFalse()
    {
        // 非 HTTP(S) 的绝对 URL 无法确定 TCP 语义（如 wails:// 自定义 scheme）。
        await Assert.That(FrontendDevServerReadiness.TryCreateTarget(
            new Uri("ftp://example.com:21/"), out _)).IsFalse();
        await Assert.That(FrontendDevServerReadiness.TryCreateTarget(
            new Uri("wails://localhost/"), out _)).IsFalse();
    }

    [Test]
    public async Task TryCreateTarget_Fragment_ReturnsFalse()
    {
        // 对齐 Go：带片段的 URL 不合法（片段不影响 TCP，但属于误配置）。
        await Assert.That(FrontendDevServerReadiness.TryCreateTarget(
            new Uri("http://localhost:5173/#app"), out _)).IsFalse();
    }

    [Test]
    public async Task TryCreateTarget_NullUrl_ThrowsArgumentNullException()
    {
        await Assert.That(() => FrontendDevServerReadiness.TryCreateTarget(null!, out _))
            .ThrowsExactly<ArgumentNullException>();
    }

    // ============== ProbeTcpOnceAsync（TCP 状态分类） ==============

    [Test]
    public async Task ProbeTcpOnceAsync_ConnectSucceeds_ReturnsReady()
    {
        // 注入连接委托：成功即就绪，无需真实网络。
        var result = await FrontendDevServerReadiness.ProbeTcpOnceAsync(
            Target,
            TimeSpan.FromSeconds(1),
            connect: (_, _) => Task.CompletedTask);

        await Assert.That(result.Status).IsEqualTo(FrontendDevServerReadiness.ProbeStatus.Ready);
        await Assert.That(result.Detail).Contains("已接受连接");
    }

    [Test]
    public async Task ProbeTcpOnceAsync_ConnectionRefused_ReturnsConnectionRefused()
    {
        var result = await FrontendDevServerReadiness.ProbeTcpOnceAsync(
            Target,
            TimeSpan.FromSeconds(1),
            connect: (_, _) => throw new SocketException((int)SocketError.ConnectionRefused));

        await Assert.That(result.Status).IsEqualTo(FrontendDevServerReadiness.ProbeStatus.ConnectionRefused);
        await Assert.That(result.Detail).Contains("被拒绝");
    }

    [Test]
    public async Task ProbeTcpOnceAsync_OtherSocketError_ReturnsOtherError()
    {
        // 例如 DNS 解析失败（HostNotFound）不应归类为「连接被拒绝」。
        var result = await FrontendDevServerReadiness.ProbeTcpOnceAsync(
            Target,
            TimeSpan.FromSeconds(1),
            connect: (_, _) => throw new SocketException((int)SocketError.HostNotFound));

        await Assert.That(result.Status).IsEqualTo(FrontendDevServerReadiness.ProbeStatus.OtherError);
    }

    [Test]
    public async Task ProbeTcpOnceAsync_AttemptTimeout_ReturnsTimeout()
    {
        // 连接挂起直至单次超时（50ms），应归类为「连接超时」。
        var result = await FrontendDevServerReadiness.ProbeTcpOnceAsync(
            Target,
            TimeSpan.FromMilliseconds(50),
            connect: async (_, ct) => await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct));

        await Assert.That(result.Status).IsEqualTo(FrontendDevServerReadiness.ProbeStatus.Timeout);
    }

    [Test]
    public async Task ProbeTcpOnceAsync_ExternalCancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var threw = false;
        try
        {
            await FrontendDevServerReadiness.ProbeTcpOnceAsync(
                Target,
                TimeSpan.FromSeconds(5),
                cts.Token,
                connect: async (_, ct) => await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct));
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task ProbeTcpOnceAsync_RealListener_ReturnsReady()
    {
        // 真实 TCP 连接回归：监听 loopback 临时端口即应判定就绪（Vite 返回 404 的场景不再误判）。
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var result = await FrontendDevServerReadiness.ProbeTcpOnceAsync(
                new FrontendDevServerReadiness.ProbeTarget("127.0.0.1", port),
                TimeSpan.FromSeconds(2));

            await Assert.That(result.Status).IsEqualTo(FrontendDevServerReadiness.ProbeStatus.Ready);
        }
        finally
        {
            listener.Stop();
        }
    }
}
