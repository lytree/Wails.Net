using System.Net;
using System.Net.Http;
using System.Text;
using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Webview;

namespace Wails.Net.Application.Tests.WebView;

/// <summary>
/// <see cref="WebviewRequestPipeline"/> 的 dev 资产反向代理（P2-12）接线测试（TUnit）：
/// 配置了 <see cref="Application.DevAssetProxy"/> 时，GET 资源请求转发到 dev server；
/// 未配置时回退本地资源处理器（生产模式行为不变）。
/// </summary>
[NotInParallel]
public sealed class DevAssetProxyPipelineTests
{
    private static WebviewRequestPipeline CreatePipeline(out List<string> assetPaths)
    {
        var paths = new List<string>();
        assetPaths = paths;

        return new WebviewRequestPipeline(
            ipcHandler: (_, _, _, _) => Task.FromResult<object?>(new { ok = true }),
            assetHandler: (path, _, _) =>
            {
                lock (paths)
                {
                    paths.Add(path);
                }

                return Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("LOCAL"));
            });
    }

    private static WebviewRequest GetRequest(string path, string? query = null) => new()
    {
        Method = "GET",
        Path = path,
        Query = query,
        WindowId = 1,
    };

    [Test]
    public async Task HandleAsync_GetAsset_WithDevProxy_ReturnsProxiedResponseAndSkipsLocalHandler()
    {
        var app = new Application(new ApplicationOptions());
        var handler = new StubHandler("PROXIED", "text/javascript");
        app.DevAssetProxy = new DevAssetProxy(
            new Uri("http://localhost:5173"), handler: handler, retryDelay: _ => TimeSpan.Zero);
        var pipeline = CreatePipeline(out var assetPaths);

        var response = await pipeline.HandleAsync(GetRequest("/main.js", "?import"));

        await Assert.That(response.StatusCode).IsEqualTo(200);
        await Assert.That(Encoding.UTF8.GetString(response.Body)).IsEqualTo("PROXIED");
        await Assert.That(assetPaths.Count).IsEqualTo(0); // 本地资源处理器未被调用
        await Assert.That(handler.LastUri!.Query).IsEqualTo("?import");
    }

    [Test]
    public async Task HandleAsync_GetAsset_WithoutDevProxy_UsesLocalHandler()
    {
        _ = new Application(new ApplicationOptions());
        var pipeline = CreatePipeline(out var assetPaths);

        var response = await pipeline.HandleAsync(GetRequest("/main.js"));

        await Assert.That(Encoding.UTF8.GetString(response.Body)).IsEqualTo("LOCAL");
        await Assert.That(assetPaths.Count).IsEqualTo(1);
    }

    [Test]
    public async Task HandleAsync_IpcRequest_IsNotProxied()
    {
        var app = new Application(new ApplicationOptions());
        var handler = new StubHandler("PROXIED", "text/javascript");
        app.DevAssetProxy = new DevAssetProxy(
            new Uri("http://localhost:5173"), handler: handler, retryDelay: _ => TimeSpan.Zero);
        var pipeline = CreatePipeline(out var assetPaths);

        var ipcRequest = new WebviewRequest
        {
            Method = "POST",
            Path = "/wails/call",
            WindowId = 1,
        };
        var response = await pipeline.HandleAsync(ipcRequest);

        await Assert.That(response.StatusCode).IsEqualTo(200);
        await Assert.That(response.ContentType).IsEqualTo("application/json");
        await Assert.That(handler.CallCount).IsEqualTo(0); // IPC 不经过 dev 代理
        await Assert.That(assetPaths.Count).IsEqualTo(0);
    }

    /// <summary>固定返回 200 + 指定正文的伪处理器。</summary>
    private sealed class StubHandler(string body, string contentType) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType),
            });
        }
    }
}
