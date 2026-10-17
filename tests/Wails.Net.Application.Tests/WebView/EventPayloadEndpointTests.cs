using System.Text;
using TUnit.Core;
using Wails.Net.Application.Events;
using Wails.Net.Application.Options;
using Wails.Net.Application.Webview;

namespace Wails.Net.Application.Tests.WebView;

/// <summary>
/// <see cref="WebviewRequestPipeline"/> 对超大事件载荷取回端点的处理测试（TUnit）。
/// 对应事件投递硬化 P0-1 / Layer 3：<c>GET /wails/eventpayload/&lt;id&gt;</c>
/// 仅接受 GET/HEAD，读取即删，且按窗口绑定（跨窗口取回 404）。
/// </summary>
[NotInParallel]
public sealed class EventPayloadEndpointTests
{
    private static Application CreateAppWithPayload(uint windowId, string text, out string id)
    {
        var app = new Application(new ApplicationOptions());
        app.EventPayloads.TryPut(windowId, Encoding.UTF8.GetBytes(text), out id);
        return app;
    }

    private static WebviewRequestPipeline CreatePipeline(out List<string> assetRequests)
    {
        var requests = new List<string>();
        assetRequests = requests;

        return new WebviewRequestPipeline(
            ipcHandler: (_, _, _, _) => Task.FromResult<object?>(null),
            assetHandler: (path, _, _) =>
            {
                lock (requests)
                {
                    requests.Add(path);
                }

                return Task.FromResult<byte[]?>(null);
            });
    }

    private static WebviewRequest BuildRequest(string method, string path, uint windowId) => new()
    {
        Method = method,
        Path = path,
        WindowId = windowId,
    };

    [Test]
    public async Task HandleAsync_GetValidPayloadId_ReturnsJsonPayload()
    {
        var app = CreateAppWithPayload(windowId: 5, "big-payload", out var id);
        var pipeline = CreatePipeline(out _);

        var response = await pipeline.HandleAsync(
            BuildRequest("GET", EventPayloadStore.PathPrefix + id, windowId: 5));

        await Assert.That(app.EventPayloads.Count).IsEqualTo(0);
        await Assert.That(response.StatusCode).IsEqualTo(200);
        await Assert.That(response.ContentType).IsEqualTo("application/json");
        await Assert.That(Encoding.UTF8.GetString(response.Body)).IsEqualTo("big-payload");
    }

    [Test]
    public async Task HandleAsync_HeadValidPayloadId_IsAccepted()
    {
        var app = CreateAppWithPayload(windowId: 5, "head-payload", out var id);
        var pipeline = CreatePipeline(out _);

        var response = await pipeline.HandleAsync(
            BuildRequest("HEAD", EventPayloadStore.PathPrefix + id, windowId: 5));

        await Assert.That(response.StatusCode).IsEqualTo(200);
        await Assert.That(app.EventPayloads.Count).IsEqualTo(0);
    }

    [Test]
    public async Task HandleAsync_SecondTake_Returns404()
    {
        CreateAppWithPayload(windowId: 5, "once", out var id);
        var pipeline = CreatePipeline(out _);

        var first = await pipeline.HandleAsync(BuildRequest("GET", EventPayloadStore.PathPrefix + id, 5));
        var second = await pipeline.HandleAsync(BuildRequest("GET", EventPayloadStore.PathPrefix + id, 5));

        await Assert.That(first.StatusCode).IsEqualTo(200);
        await Assert.That(second.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task HandleAsync_CrossWindowTake_Returns404()
    {
        var app = CreateAppWithPayload(windowId: 5, "bound", out var id);
        var pipeline = CreatePipeline(out _);

        var response = await pipeline.HandleAsync(
            BuildRequest("GET", EventPayloadStore.PathPrefix + id, windowId: 6));

        await Assert.That(response.StatusCode).IsEqualTo(404);
        await Assert.That(app.EventPayloads.Count).IsEqualTo(1);
    }

    [Test]
    public async Task HandleAsync_NonGetMethod_Returns405()
    {
        CreateAppWithPayload(windowId: 5, "payload", out var id);
        var pipeline = CreatePipeline(out _);

        var response = await pipeline.HandleAsync(
            BuildRequest("POST", EventPayloadStore.PathPrefix + id, windowId: 5));

        await Assert.That(response.StatusCode).IsEqualTo(405);
    }

    [Test]
    public async Task HandleAsync_InvalidIdShape_Returns404()
    {
        CreateAppWithPayload(windowId: 5, "payload", out _);
        var pipeline = CreatePipeline(out _);

        var response = await pipeline.HandleAsync(
            BuildRequest("GET", EventPayloadStore.PathPrefix + "not-a-valid-id", windowId: 5));

        await Assert.That(response.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task HandleAsync_UnknownId_Returns404()
    {
        _ = new Application(new ApplicationOptions());
        var pipeline = CreatePipeline(out _);

        var response = await pipeline.HandleAsync(
            BuildRequest("GET", EventPayloadStore.PathPrefix + new string('0', 32), windowId: 5));

        await Assert.That(response.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task HandleAsync_NonPayloadPath_StillRoutedToAssetHandler()
    {
        _ = new Application(new ApplicationOptions());
        var pipeline = CreatePipeline(out var assetRequests);

        var response = await pipeline.HandleAsync(BuildRequest("GET", "/app.js", windowId: 5));

        bool routedToAssetHandler;
        lock (assetRequests)
        {
            routedToAssetHandler = assetRequests.Contains("app.js");
        }

        await Assert.That(response.StatusCode).IsEqualTo(404);
        await Assert.That(routedToAssetHandler).IsTrue();
    }
}
