using System.Net;
using System.Net.Http.Headers;
using System.Text;
using TUnit.Core;
using Wails.Net.AssetServer.Results;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// IAssetResult 实现（BytesResult、StatusResult、ErrorResult）的单元测试（TUnit）。
/// 覆盖 M8 新增的 Result 模式：状态码、头部设置、响应体写入。
/// </summary>
[NotInParallel]
public sealed class AssetResultTests
{
    /// <summary>
    /// 创建一个可测试的 HttpListenerResponse 实例。
    /// 使用 HttpListener 创建真实响应对象。
    /// </summary>
    private static async Task<HttpListenerResponse> CreateResponseAsync(Action<HttpListenerResponse> configure)
    {
        var listener = new HttpListener();
        var port = 19000 + Random.Shared.Next(0, 1000);
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        using var client = new HttpClient();
        var requestTask = client.GetAsync($"http://localhost:{port}/");

        var ctx = await listener.GetContextAsync();
        var response = ctx.Response;

        configure(response);
        response.Close();

        var httpResponse = await requestTask;
        listener.Stop();

        return response;
    }

    // ========== BytesResult 测试 ==========

    [Test]
    public async Task BytesResult_Default_HasStatusCode200()
    {
        var result = new BytesResult(Encoding.UTF8.GetBytes("test"), "text/plain");
        await Assert.That(result.StatusCode).IsEqualTo(200);
    }

    [Test]
    public async Task BytesResult_Default_HasContentType()
    {
        var result = new BytesResult(Encoding.UTF8.GetBytes("test"), "text/plain");
        await Assert.That(result.ContentType).IsEqualTo("text/plain");
    }

    [Test]
    public async Task BytesResult_WithRange_HasStatusCode206()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        var result = new BytesResult(content, "text/plain", 0, 5);
        await Assert.That(result.StatusCode).IsEqualTo(206);
    }

    [Test]
    public async Task BytesResult_WithRange_HasRangeInfo()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        var result = new BytesResult(content, "text/plain", 2, 5);
        await Assert.That(result.Range).IsNotNull();
        await Assert.That(result.Range!.Value.Offset).IsEqualTo(2L);
        await Assert.That(result.Range!.Value.Length).IsEqualTo(5L);
    }

    [Test]
    public async Task BytesResult_WithETag_HasETag()
    {
        var result = new BytesResult(Encoding.UTF8.GetBytes("test"), "text/plain", eTag: "\"abc123\"");
        await Assert.That(result.ETag).IsEqualTo("\"abc123\"");
    }

    [Test]
    public async Task BytesResult_WithLastModified_HasLastModified()
    {
        var time = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var result = new BytesResult(Encoding.UTF8.GetBytes("test"), "text/plain", lastModified: time);
        await Assert.That(result.LastModified).IsEqualTo(time);
    }

    [Test]
    public async Task BytesResult_NullContent_ThrowsArgumentNullException()
    {
        await Assert.That(() => new BytesResult(null!, "text/plain"))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task BytesResult_TotalLength_EqualsContentLength()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        var result = new BytesResult(content, "text/plain");
        await Assert.That(result.TotalLength).IsEqualTo(content.Length);
    }

    // ========== StatusResult 测试 ==========

    [Test]
    public async Task StatusResult_304_HasStatusCode304()
    {
        var result = new StatusResult(304);
        await Assert.That(result.StatusCode).IsEqualTo(304);
    }

    [Test]
    public async Task StatusResult_404_HasStatusCode404()
    {
        var result = new StatusResult(404);
        await Assert.That(result.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task StatusResult_416_HasStatusCode416()
    {
        var result = new StatusResult(416);
        await Assert.That(result.StatusCode).IsEqualTo(416);
    }

    [Test]
    public async Task StatusResult_ContentType_IsAlwaysNull()
    {
        var result = new StatusResult(304);
        await Assert.That(result.ContentType).IsNull();
    }

    // ========== ErrorResult 测试 ==========

    [Test]
    public async Task ErrorResult_Default_HasStatusCode500()
    {
        var result = new ErrorResult("Something went wrong");
        await Assert.That(result.StatusCode).IsEqualTo(500);
    }

    [Test]
    public async Task ErrorResult_CustomStatusCode_HasCustomCode()
    {
        var result = new ErrorResult("Not found", 404);
        await Assert.That(result.StatusCode).IsEqualTo(404);
    }

    [Test]
    public async Task ErrorResult_HasTextPlainContentType()
    {
        var result = new ErrorResult("error");
        await Assert.That(result.ContentType).IsEqualTo("text/plain; charset=utf-8");
    }

    [Test]
    public async Task ErrorResult_HasMessage()
    {
        var result = new ErrorResult("Test error message");
        await Assert.That(result.Message).IsEqualTo("Test error message");
    }

    [Test]
    public async Task ErrorResult_NullMessage_ThrowsArgumentNullException()
    {
        await Assert.That(() => new ErrorResult(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ErrorResult_EmptyMessage_ThrowsArgumentException()
    {
        await Assert.That(() => new ErrorResult(string.Empty)).ThrowsExactly<ArgumentException>();
    }

    // ========== IAssetResult 接口契约测试 ==========

    [Test]
    public async Task BytesResult_ImplementsIAssetResult()
    {
        IAssetResult result = new BytesResult(Encoding.UTF8.GetBytes("test"), "text/plain");
        await Assert.That(result).IsAssignableTo<IAssetResult>();
    }

    [Test]
    public async Task StatusResult_ImplementsIAssetResult()
    {
        IAssetResult result = new StatusResult(304);
        await Assert.That(result).IsAssignableTo<IAssetResult>();
    }

    [Test]
    public async Task ErrorResult_ImplementsIAssetResult()
    {
        IAssetResult result = new ErrorResult("error");
        await Assert.That(result).IsAssignableTo<IAssetResult>();
    }

    // ========== WriteAsync 集成测试 ==========

    [Test]
    public async Task BytesResult_WriteAsync_WritesContentToResponse()
    {
        using var listener = new HttpListener();
        var port = 19500 + Random.Shared.Next(0, 500);
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var content = Encoding.UTF8.GetBytes("<html><body>Test</body></html>");
        var result = new BytesResult(content, "text/html", "\"etag123\"");

        var requestTask = Task.Run(async () =>
        {
            using var client = new HttpClient();
            return await client.GetAsync($"http://localhost:{port}/");
        });

        var ctx = await listener.GetContextAsync();
        await result.WriteAsync(ctx.Response);
        ctx.Response.Close();

        var httpResponse = await requestTask;
        var body = await httpResponse.Content.ReadAsByteArrayAsync();

        await Assert.That((int)httpResponse.StatusCode).IsEqualTo(200);
        await Assert.That(httpResponse.Content.Headers.ContentType?.MediaType).IsEqualTo("text/html");
        await Assert.That(httpResponse.Headers.ETag?.Tag).IsEqualTo("\"etag123\"");
        await Assert.That(body).IsEquivalentTo(content);

        listener.Stop();
    }

    [Test]
    public async Task StatusResult_WriteAsync_SetsStatusCode()
    {
        using var listener = new HttpListener();
        var port = 19500 + Random.Shared.Next(500, 1000);
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var result = new StatusResult(304);

        var requestTask = Task.Run(async () =>
        {
            using var client = new HttpClient();
            return await client.GetAsync($"http://localhost:{port}/");
        });

        var ctx = await listener.GetContextAsync();
        await result.WriteAsync(ctx.Response);
        ctx.Response.Close();

        var httpResponse = await requestTask;

        await Assert.That((int)httpResponse.StatusCode).IsEqualTo(304);

        listener.Stop();
    }

    [Test]
    public async Task ErrorResult_WriteAsync_WritesErrorMessage()
    {
        using var listener = new HttpListener();
        var port = 19500 + Random.Shared.Next(1000, 1500);
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var result = new ErrorResult("Internal error occurred", 500);

        var requestTask = Task.Run(async () =>
        {
            using var client = new HttpClient();
            return await client.GetAsync($"http://localhost:{port}/");
        });

        var ctx = await listener.GetContextAsync();
        await result.WriteAsync(ctx.Response);
        ctx.Response.Close();

        var httpResponse = await requestTask;
        var body = await httpResponse.Content.ReadAsStringAsync();

        await Assert.That((int)httpResponse.StatusCode).IsEqualTo(500);
        await Assert.That(body).IsEqualTo("Internal error occurred");

        listener.Stop();
    }

    [Test]
    public async Task BytesResult_WriteAsync_WithRange_WritesPartialContent()
    {
        using var listener = new HttpListener();
        var port = 19500 + Random.Shared.Next(1500, 2000);
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var content = Encoding.UTF8.GetBytes("hello world, this is a test");
        var result = new BytesResult(content, "text/plain", 6, 5); // "world"

        var requestTask = Task.Run(async () =>
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Range = new RangeHeaderValue(6, 10);
            return await client.GetAsync($"http://localhost:{port}/");
        });

        var ctx = await listener.GetContextAsync();
        await result.WriteAsync(ctx.Response);
        ctx.Response.Close();

        var httpResponse = await requestTask;
        var body = await httpResponse.Content.ReadAsByteArrayAsync();

        await Assert.That((int)httpResponse.StatusCode).IsEqualTo(206);
        await Assert.That(body).IsEquivalentTo(Encoding.UTF8.GetBytes("world"));

        listener.Stop();
    }
}
