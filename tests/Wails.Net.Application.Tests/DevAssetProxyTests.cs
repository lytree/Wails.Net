using System.Net;
using System.Net.Http;
using System.Text;
using TUnit.Core;

namespace Wails.Net.Application.Tests;

/// <summary>
/// <see cref="Wails.Net.Application.Webview.DevAssetProxy"/> 单元测试（TUnit）。
/// 覆盖纯逻辑（URL 拼接）与 HTTP 转发映射（状态码 / Content-Type / 缓存头 / 重试 / 失败回退）。
/// 通过注入伪 <see cref="HttpMessageHandler"/> 避免真实网络与真实等待。
/// </summary>
[NotInParallel]
public sealed class DevAssetProxyTests
{
    private static readonly Uri Base = new("http://localhost:5173");

    private static Wails.Net.Application.Webview.DevAssetProxy CreateProxy(
        FakeHttpMessageHandler handler,
        int maxAttempts = 3)
        => new(Base, logger: null, handler: handler, maxAttempts: maxAttempts, retryDelay: _ => TimeSpan.Zero);

    // ---------- BuildTargetUri（纯逻辑） ----------

    [Test]
    public async Task BuildTargetUri_WithLeadingSlashPathAndQuestionMark_Combines()
    {
        var uri = Wails.Net.Application.Webview.DevAssetProxy.BuildTargetUri(Base, "/app.js", "?t=1&import");

        await Assert.That(uri.AbsoluteUri).IsEqualTo("http://localhost:5173/app.js?t=1&import");
    }

    [Test]
    public async Task BuildTargetUri_WithoutLeadingSlashOrQuestionMark_Normalizes()
    {
        var uri = Wails.Net.Application.Webview.DevAssetProxy.BuildTargetUri(Base, "app.js", "t=1");

        await Assert.That(uri.AbsoluteUri).IsEqualTo("http://localhost:5173/app.js?t=1");
    }

    [Test]
    public async Task BuildTargetUri_WithoutQuery_OmitsQuestionMark()
    {
        var uri = Wails.Net.Application.Webview.DevAssetProxy.BuildTargetUri(Base, "/", null);

        await Assert.That(uri.AbsoluteUri).IsEqualTo("http://localhost:5173/");
    }

    [Test]
    public async Task BuildTargetUri_WithBasePathPrefix_PreservesPrefix()
    {
        var uri = Wails.Net.Application.Webview.DevAssetProxy.BuildTargetUri(
            new Uri("http://localhost:5173/sub/"), "/x.js", null);

        await Assert.That(uri.AbsoluteUri).IsEqualTo("http://localhost:5173/sub/x.js");
    }

    // ---------- 构造校验 ----------

    [Test]
    public async Task Constructor_WithNonHttpScheme_Throws()
    {
        await Assert.That(() => new Wails.Net.Application.Webview.DevAssetProxy(new Uri("file:///tmp/x")))
            .ThrowsExactly<ArgumentException>();
    }

    // ---------- TryGetAsync（转发映射） ----------

    [Test]
    public async Task TryGetAsync_ReturnsUpstreamStatusContentTypeAndBody()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("console.log(1)", Encoding.UTF8, "text/javascript"),
        });
        using var proxy = CreateProxy(handler);

        var response = await proxy.TryGetAsync("/main.js", null);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.StatusCode).IsEqualTo(200);
        await Assert.That(response.ContentType).StartsWith("text/javascript");
        await Assert.That(Encoding.UTF8.GetString(response.Body)).IsEqualTo("console.log(1)");
        await Assert.That(handler.RequestUris[0].AbsoluteUri).IsEqualTo("http://localhost:5173/main.js");
    }

    [Test]
    public async Task TryGetAsync_ForwardsCacheHeaders()
    {
        var handler = new FakeHttpMessageHandler();
        var message = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("x", Encoding.UTF8, "text/plain"),
        };
        message.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        message.Headers.TryAddWithoutValidation("ETag", "\"abc\"");
        handler.Enqueue(_ => message);
        using var proxy = CreateProxy(handler);

        var response = await proxy.TryGetAsync("/main.js", "?t=2");

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Headers).IsNotNull();
        await Assert.That(response.Headers!["Cache-Control"]).IsEqualTo("no-cache");
        await Assert.That(response.Headers!["ETag"]).IsEqualTo("\"abc\"");
        await Assert.That(handler.RequestUris[0].Query).IsEqualTo("?t=2");
    }

    [Test]
    public async Task TryGetAsync_NonSuccessStatus_IsForwarded()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("not found", Encoding.UTF8, "text/plain"),
        });
        using var proxy = CreateProxy(handler);

        var response = await proxy.TryGetAsync("/missing", null);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task TryGetAsync_TransientFailureThenSuccess_Retries()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueThrow(new HttpRequestException("connection refused"));
        handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ok", Encoding.UTF8, "text/plain"),
        });
        using var proxy = CreateProxy(handler, maxAttempts: 3);

        var response = await proxy.TryGetAsync("/main.js", null);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.StatusCode).IsEqualTo(200);
        await Assert.That(handler.CallCount).IsEqualTo(2);
    }

    [Test]
    public async Task TryGetAsync_PersistentFailure_ReturnsNullAfterMaxAttempts()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueThrow(new HttpRequestException("down"));
        handler.EnqueueThrow(new HttpRequestException("down"));
        handler.EnqueueThrow(new HttpRequestException("down"));
        using var proxy = CreateProxy(handler, maxAttempts: 3);

        var response = await proxy.TryGetAsync("/main.js", null);

        await Assert.That(response).IsNull();
        await Assert.That(handler.CallCount).IsEqualTo(3);
    }

    [Test]
    public async Task TryGetAsync_Cancelled_PropagatesCancellation()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueThrow(new OperationCanceledException());
        using var proxy = CreateProxy(handler, maxAttempts: 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(async () => await proxy.TryGetAsync("/main.js", null, cts.Token))
            .ThrowsExactly<OperationCanceledException>();
    }

    [Test]
    public async Task TryGetAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var handler = new FakeHttpMessageHandler();
        var proxy = CreateProxy(handler);
        proxy.Dispose();

        await Assert.That(async () => await proxy.TryGetAsync("/main.js", null))
            .ThrowsExactly<ObjectDisposedException>();
    }

    /// <summary>
    /// 伪 HTTP 处理器：按脚本返回响应或抛出异常，并记录调用次数与请求 URI。
    /// </summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public int CallCount { get; private set; }

        public List<Uri> RequestUris { get; } = [];

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> factory) => _responses.Enqueue(factory);

        public void EnqueueThrow(Exception exception) =>
            _responses.Enqueue(_ => throw exception);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.RequestUri is not null)
            {
                RequestUris.Add(request.RequestUri);
            }

            var factory = _responses.Count > 0
                ? _responses.Dequeue()
                : throw new InvalidOperationException("FakeHttpMessageHandler 未预置响应。");
            return Task.FromResult(factory(request));
        }
    }
}
