using System.Net;
using System.Net.Sockets;
using TUnit.Assertions;
using TUnit.Core;
using Wails.Net.Cli.Build;

namespace Wails.Net.Cli.Tests;

/// <summary>
/// FrontendDevServerReadiness 单元测试。
/// 对应任务 P2-11：<c>wails dev</c> 前端 dev server HTTP 就绪检查
/// （对齐 Wails v3 beta.28 <c>internal/commands/watcher.go</c> 的 applyFrontendReadiness）。
/// 覆盖：立即可达、先失败后成功重试、始终不可达超时，以及单次探测的状态分类。
/// </summary>
[NotInParallel]
public sealed class FrontendDevServerReadinessTests
{
    private static readonly Uri Target = new("http://127.0.0.1:59999/");

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
                FrontendDevServerReadiness.ProbeStatus.Ready, "HTTP 200"));
        }

        // Act
        var result = await FrontendDevServerReadiness.WaitForReadyAsync(
            Probe, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10));

        // Assert
        await Assert.That(result.Ready).IsTrue();
        await Assert.That(attempts).IsEqualTo(1);
        await Assert.That(result.Message).Contains("HTTP 200");
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
                    FrontendDevServerReadiness.ProbeStatus.Ready, "HTTP 200");
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

    // ============== ProbeHttpOnceAsync（HTTP 状态分类） ==============

    [Test]
    public async Task ProbeHttpOnceAsync_2xx_ReturnsReady()
    {
        using var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await FrontendDevServerReadiness.ProbeHttpOnceAsync(client, Target, TimeSpan.FromSeconds(1));

        await Assert.That(result.Status).IsEqualTo(FrontendDevServerReadiness.ProbeStatus.Ready);
        await Assert.That(result.Detail).Contains("200");
    }

    [Test]
    public async Task ProbeHttpOnceAsync_3xx_ReturnsReady()
    {
        using var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found)));

        var result = await FrontendDevServerReadiness.ProbeHttpOnceAsync(client, Target, TimeSpan.FromSeconds(1));

        await Assert.That(result.Status).IsEqualTo(FrontendDevServerReadiness.ProbeStatus.Ready);
        await Assert.That(result.Detail).Contains("302");
    }

    [Test]
    public async Task ProbeHttpOnceAsync_5xx_ReturnsServerError()
    {
        using var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var result = await FrontendDevServerReadiness.ProbeHttpOnceAsync(client, Target, TimeSpan.FromSeconds(1));

        await Assert.That(result.Status).IsEqualTo(FrontendDevServerReadiness.ProbeStatus.ServerError);
        await Assert.That(result.Status).IsNotEqualTo(FrontendDevServerReadiness.ProbeStatus.Ready);
    }

    [Test]
    public async Task ProbeHttpOnceAsync_ConnectionRefused_ReturnsConnectionRefused()
    {
        using var client = CreateClient((_, _) => throw new HttpRequestException(
            "connection refused",
            new SocketException((int)SocketError.ConnectionRefused)));

        var result = await FrontendDevServerReadiness.ProbeHttpOnceAsync(client, Target, TimeSpan.FromSeconds(1));

        await Assert.That(result.Status).IsEqualTo(FrontendDevServerReadiness.ProbeStatus.ConnectionRefused);
    }

    [Test]
    public async Task ProbeHttpOnceAsync_AttemptTimeout_ReturnsTimeout()
    {
        // 探测请求挂起直至单次超时（50ms），应归类为“请求超时”。
        using var client = CreateClient(async (_, ct) =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await FrontendDevServerReadiness.ProbeHttpOnceAsync(client, Target, TimeSpan.FromMilliseconds(50));

        await Assert.That(result.Status).IsEqualTo(FrontendDevServerReadiness.ProbeStatus.Timeout);
    }

    [Test]
    public async Task ProbeHttpOnceAsync_ExternalCancellation_Throws()
    {
        using var client = CreateClient(async (_, ct) =>
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var threw = false;
        try
        {
            await FrontendDevServerReadiness.ProbeHttpOnceAsync(client, Target, TimeSpan.FromSeconds(5), cts.Token);
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    /// <summary>
    /// 使用注入的发送委托构造 HttpClient（Timeout 设为无限，由调用方控制单次超时）。
    /// </summary>
    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => new(new StubHandler(handler)) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    /// <summary>
    /// 以委托驱动的 <see cref="HttpMessageHandler"/> 桩，避免测试依赖真实网络。
    /// </summary>
    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
