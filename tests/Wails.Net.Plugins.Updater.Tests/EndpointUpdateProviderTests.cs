using System.Net;
using System.Text;
using TUnit.Core;
using Wails.Net.Plugins.Updater.Services;

namespace Wails.Net.Plugins.Updater.Tests;

/// <summary>
/// <see cref="EndpointUpdateProvider"/> 的单元测试（TUnit）。
/// 覆盖 Wails Update Manifest 解析、字段映射、平台/架构资产选择、校验信息映射、
/// 不支持协议/缺失字段的明确错误，以及自定义协议加载器（如 <c>wails://</c>）与本地文件来源。
/// 测试通过注入的 <see cref="HttpMessageHandler"/> 或本地文件避免真实网络访问。
/// </summary>
[NotInParallel]
public sealed class EndpointUpdateProviderTests
{
    /// <summary>
    /// 可记录请求的 HTTP 消息处理器，按请求返回预设响应。
    /// </summary>
    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        /// <summary>已记录（深拷贝）的请求列表。</summary>
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        /// <summary>构造始终返回指定 JSON 内容的处理器。</summary>
        public static StubHttpHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK)
            => new(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        /// <summary>构造始终返回指定状态码（无内容）的处理器。</summary>
        public static StubHttpHandler Status(HttpStatusCode status)
            => new(_ => new HttpResponseMessage(status));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(Clone(request));
            return Task.FromResult(_responder(request));
        }

        private static HttpRequestMessage Clone(HttpRequestMessage original)
        {
            var clone = new HttpRequestMessage(original.Method, original.RequestUri);
            foreach (var header in original.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, string.Join(",", header.Value));
            }

            return clone;
        }
    }

    private static EndpointUpdateProvider Create(
        StubHttpHandler handler,
        string url,
        string channel = "",
        HTTPHeader[]? headers = null,
        string assetNamePattern = "",
        Func<string, CancellationToken, Task<Stream?>>? customSchemeLoader = null,
        string platform = "windows",
        string arch = "amd64")
        => new(new HttpClient(handler), url, channel, headers, assetNamePattern, customSchemeLoader)
        {
            Platform = platform,
            Arch = arch
        };

    // ============== 构造与元数据 ==============

    [Test]
    public async Task EndpointProvider_Name_ReturnsEndpoint()
    {
        var provider = Create(StubHttpHandler.Json("{}"), "https://example.com/manifest.json");
        await Assert.That(provider.Name).IsEqualTo("endpoint");
    }

    [Test]
    public async Task EndpointProvider_NullHttpClient_ThrowsArgumentNullException()
    {
        await Assert.That(() => new EndpointUpdateProvider(null!, "https://example.com/manifest.json"))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task EndpointProvider_EmptyUrl_ThrowsArgumentException()
    {
        var handler = StubHttpHandler.Json("{}");
        await Assert.That(() => new EndpointUpdateProvider(new HttpClient(handler), "   "))
            .ThrowsExactly<ArgumentException>();
    }

    // ============== 字段映射 ==============

    [Test]
    public async Task CheckAsync_ParsesManifestAndMapsFields()
    {
        // 安排：含 v 前缀版本、发行说明、发布时间与匹配当前平台的资产
        var json = """
        {
            "schemaVersion": 1,
            "version": "v2.0.0",
            "channel": "stable",
            "name": "Big Release",
            "notes": "## Changes\n- fix",
            "publishedAt": "2026-07-15T10:30:00Z",
            "artifacts": [
                {"url": "https://cdn.example.com/app-windows-amd64.zip", "size": 4242,
                 "platform": "windows", "arch": "amd64"}
            ]
        }
        """;
        var handler = StubHttpHandler.Json(json);
        var provider = Create(handler, "https://example.com/manifest.json", channel: "stable");

        // 操作
        var manifest = await provider.CheckAsync();

        // 断言
        await Assert.That(manifest).IsNotNull();
        await Assert.That(manifest!.Version).IsEqualTo("2.0.0");
        await Assert.That(manifest.ReleaseNotes).IsEqualTo("## Changes\n- fix");
        await Assert.That(manifest.DownloadURL).IsEqualTo("https://cdn.example.com/app-windows-amd64.zip");
        await Assert.That(manifest.ContentLength).IsEqualTo(4242L);
        await Assert.That(manifest.PublishedDate).IsNotNull();
    }

    [Test]
    public async Task CheckAsync_MissingPublishedAt_LeavesPublishedDateNull()
    {
        var json = """{"version":"2.0.0","artifacts":[{"url":"https://example.com/a.zip"}]}""";
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/manifest.json");

        var manifest = await provider.CheckAsync();

        await Assert.That(manifest!.PublishedDate).IsNull();
        await Assert.That(manifest.ContentLength).IsNull();
    }

    // ============== URL / 占位符 / 请求头 ==============

    [Test]
    public async Task CheckAsync_SendsPlatformAndArchAsQueryParams()
    {
        var json = """{"version":"2.0.0","artifacts":[{"url":"https://example.com/a.zip"}]}""";
        var handler = StubHttpHandler.Json(json);
        var provider = Create(handler, "https://example.com/manifest.json", platform: "darwin", arch: "arm64");

        await provider.CheckAsync();

        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        var uri = handler.Requests[0].RequestUri!;
        await Assert.That(uri.Query).Contains("platform=darwin");
        await Assert.That(uri.Query).Contains("arch=arm64");
    }

    [Test]
    public async Task CheckAsync_SubstitutesPlaceholdersAndKeepsUnconsumedAsQuery()
    {
        // 安排：模板含 {{platform}}/{{arch}}/{{channel}}，未含 {{version}}；channel/version 由配置提供
        var json = """{"version":"2.0.0","channel":"beta","artifacts":[{"url":"app.zip"}]}""";
        var handler = StubHttpHandler.Json(json);
        var provider = Create(
            handler,
            "https://example.com/updates/{{platform}}/{{arch}}/{{channel}}.json",
            channel: "beta",
            platform: "darwin",
            arch: "arm64");
        provider.CurrentVersion = "1.0.0";

        await provider.CheckAsync();

        var uri = handler.Requests[0].RequestUri!;
        await Assert.That(uri.AbsolutePath).IsEqualTo("/updates/darwin/arm64/beta.json");
        await Assert.That(uri.Query).IsEqualTo("?version=1.0.0");
    }

    [Test]
    public async Task CheckAsync_SendsConfiguredHeaders()
    {
        var json = """{"version":"2.0.0","artifacts":[{"url":"https://example.com/a.zip"}]}""";
        var handler = StubHttpHandler.Json(json);
        var headers = new HTTPHeader[] { new() { Key = "Authorization", Value = "License abc-123" } };
        var provider = Create(handler, "https://example.com/manifest.json", headers: headers);

        await provider.CheckAsync();

        var request = handler.Requests[0];
        await Assert.That(request.Headers.Contains("Authorization")).IsTrue();
        await Assert.That(string.Join(",", request.Headers.GetValues("Authorization")))
            .IsEqualTo("License abc-123");
        await Assert.That(string.Join(",", request.Headers.GetValues("Accept"))).Contains("application/json");
    }

    [Test]
    public async Task CheckAsync_ResolvesRelativeArtifactUrlAgainstManifest()
    {
        var json = """{"version":"2.0.0","artifacts":[{"url":"app.zip"}]}""";
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/updates/manifest.json");

        var manifest = await provider.CheckAsync();

        await Assert.That(manifest!.DownloadURL).IsEqualTo("https://example.com/updates/app.zip");
    }

    // ============== HTTP 状态处理 ==============

    [Test]
    public async Task CheckAsync_NoContentOrNotFound_ReturnsNull()
    {
        foreach (var status in new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound })
        {
            var provider = Create(StubHttpHandler.Status(status), "https://example.com/manifest.json");
            var manifest = await provider.CheckAsync();
            await Assert.That(manifest).IsNull();
        }
    }

    [Test]
    public async Task CheckAsync_ServerError_ThrowsHttpRequestException()
    {
        var provider = Create(
            StubHttpHandler.Status(HttpStatusCode.InternalServerError),
            "https://example.com/manifest.json");

        await Assert.That(() => provider.CheckAsync()).ThrowsExactly<HttpRequestException>();
    }

    // ============== channel / schema / 缺失字段 ==============

    [Test]
    public async Task CheckAsync_ChannelMismatch_ReturnsNull()
    {
        var json = """{"version":"2.0.0","channel":"beta","artifacts":[{"url":"a.zip"}]}""";
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json", channel: "stable");

        var manifest = await provider.CheckAsync();

        await Assert.That(manifest).IsNull();
    }

    [Test]
    public async Task CheckAsync_ManifestChannelEmpty_Proceeds()
    {
        var json = """{"version":"2.0.0","artifacts":[{"url":"https://example.com/a.zip"}]}""";
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json", channel: "stable");

        var manifest = await provider.CheckAsync();

        await Assert.That(manifest).IsNotNull();
        await Assert.That(manifest!.Version).IsEqualTo("2.0.0");
    }

    [Test]
    public async Task CheckAsync_SchemaVersionTooNew_ThrowsInvalidOperationException()
    {
        var json = """{"schemaVersion":2,"version":"2.0.0","artifacts":[{"url":"a.zip"}]}""";
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json");

        await Assert.That(() => provider.CheckAsync()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task CheckAsync_MissingVersion_ThrowsInvalidOperationException()
    {
        var json = """{"artifacts":[{"url":"a.zip"}]}""";
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json");

        await Assert.That(() => provider.CheckAsync()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task CheckAsync_MalformedJson_ThrowsInvalidOperationException()
    {
        var provider = Create(StubHttpHandler.Json("{ not json"), "https://example.com/m.json");

        await Assert.That(() => provider.CheckAsync()).ThrowsExactly<InvalidOperationException>();
    }

    // ============== 资产选择 ==============

    [Test]
    public async Task CheckAsync_SelectsArtifactByPlatformAliases()
    {
        // 安排：darwin/arm64 目标须跳过 win/linux 资产，命中 macos/aarch64 别名
        var json = """
        {
            "version": "2.0.0",
            "artifacts": [
                {"url": "https://example.com/win.exe", "platform": "win", "arch": "x86_64"},
                {"url": "https://example.com/mac.zip", "platform": "macos", "arch": "aarch64"},
                {"url": "https://example.com/linux.tgz", "platform": "linux", "arch": "x86_64"}
            ]
        }
        """;
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json",
            platform: "darwin", arch: "arm64");

        var manifest = await provider.CheckAsync();

        await Assert.That(manifest!.DownloadURL).IsEqualTo("https://example.com/mac.zip");
    }

    [Test]
    public async Task CheckAsync_UniversalArtifactWithoutPlatformMatches()
    {
        var json = """{"version":"2.0.0","artifacts":[{"url":"https://example.com/universal.zip"}]}""";
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json");

        var manifest = await provider.CheckAsync();

        await Assert.That(manifest!.DownloadURL).IsEqualTo("https://example.com/universal.zip");
    }

    [Test]
    public async Task CheckAsync_NoMatchingArtifact_ThrowsInvalidOperationException()
    {
        var json = """{"version":"2.0.0","artifacts":[{"url":"https://example.com/win.exe","platform":"windows","arch":"amd64"}]}""";
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json",
            platform: "linux", arch: "arm64");

        await Assert.That(() => provider.CheckAsync()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task CheckAsync_AssetNamePattern_TakesPrecedenceOverPlatform()
    {
        // 安排：即使当前平台为 linux，用户显式模式也按用户选择 win 资产
        var json = """
        {
            "version": "2.0.0",
            "artifacts": [
                {"url": "https://example.com/app-linux-arm64.tar.gz", "platform": "linux", "arch": "arm64"},
                {"url": "https://example.com/app-windows-amd64.zip", "platform": "windows", "arch": "amd64"}
            ]
        }
        """;
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json",
            assetNamePattern: "windows-amd64", platform: "linux", arch: "arm64");

        var manifest = await provider.CheckAsync();

        await Assert.That(manifest!.DownloadURL).IsEqualTo("https://example.com/app-windows-amd64.zip");
    }

    // ============== 校验信息映射 ==============

    [Test]
    public async Task CheckAsync_Sha256Digest_MapsToChecksum()
    {
        // 安排：使用无填充 base64（raw 编码）以覆盖 pad 逻辑；期望十六进制校验和
        var digestBytes = new byte[32];
        var rawDigest = Convert.ToBase64String(digestBytes).TrimEnd('=');
        var expectedChecksum = Convert.ToHexStringLower(digestBytes);
        var json = $$"""
        {
            "version": "2.0.0",
            "artifacts": [
                {"url": "https://example.com/a.zip", "platform": "windows", "arch": "amd64",
                 "digestAlgo": "SHA256", "digest": "{{rawDigest}}"}
            ]
        }
        """;
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json");

        var manifest = await provider.CheckAsync();

        await Assert.That(manifest!.Checksum).IsEqualTo(expectedChecksum);
    }

    [Test]
    public async Task CheckAsync_MinisignSignature_MapsToSignature()
    {
        var signature = Convert.ToBase64String(Encoding.UTF8.GetBytes("minisign-blob"));
        var json = $$"""
        {
            "version": "2.0.0",
            "artifacts": [
                {"url": "https://example.com/a.zip", "platform": "windows", "arch": "amd64",
                 "signatureAlgo": "minisign", "signature": "{{signature}}"}
            ]
        }
        """;
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json");

        var manifest = await provider.CheckAsync();

        await Assert.That(manifest!.Signature).IsEqualTo(signature);
    }

    [Test]
    public async Task CheckAsync_SignatureWithoutAlgo_ThrowsInvalidDataException()
    {
        var signature = Convert.ToBase64String(Encoding.UTF8.GetBytes("blob"));
        var json = $$"""
        {
            "version": "2.0.0",
            "artifacts": [
                {"url": "https://example.com/a.zip", "platform": "windows", "arch": "amd64",
                 "signature": "{{signature}}"}
            ]
        }
        """;
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json");

        await Assert.That(() => provider.CheckAsync()).ThrowsExactly<InvalidDataException>();
    }

    [Test]
    public async Task CheckAsync_UndecodableDigest_ThrowsInvalidDataException()
    {
        var json = """
        {
            "version": "2.0.0",
            "artifacts": [
                {"url": "https://example.com/a.zip", "platform": "windows", "arch": "amd64",
                 "digestAlgo": "sha256", "digest": "!!! not base64 !!!"}
            ]
        }
        """;
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json");

        await Assert.That(() => provider.CheckAsync()).ThrowsExactly<InvalidDataException>();
    }

    // ============== 协议处理（含自定义协议 / 本地文件） ==============

    [Test]
    public async Task CheckAsync_UnsupportedManifestScheme_ThrowsNotSupportedException()
    {
        // 无自定义加载器时，非 http/https/file 的清单协议应明确报错
        var provider = Create(StubHttpHandler.Json("{}"), "wails://updates/current.json");

        await Assert.That(() => provider.CheckAsync()).ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task CheckAsync_CustomSchemeLoader_IsUsed()
    {
        // 安排：wails:// 协议通过注入的加载器提供清单内容（模拟宿主内置资源服务）
        var json = """{"version":"2.0.0","artifacts":[{"url":"https://example.com/a.zip","platform":"windows","arch":"amd64"}]}""";
        var handler = StubHttpHandler.Json("{}");
        var provider = Create(
            handler,
            "wails://updates/current.json",
            customSchemeLoader: (_, _) => Task.FromResult<Stream?>(
                new MemoryStream(Encoding.UTF8.GetBytes(json))));

        var manifest = await provider.CheckAsync();

        await Assert.That(manifest).IsNotNull();
        await Assert.That(manifest!.Version).IsEqualTo("2.0.0");
        // 自定义协议路径不应发起 HTTP 请求
        await Assert.That(handler.Requests.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CheckAsync_UnsupportedArtifactScheme_ThrowsNotSupportedException()
    {
        var json = """{"version":"2.0.0","artifacts":[{"url":"ftp://example.com/a.zip","platform":"windows","arch":"amd64"}]}""";
        var provider = Create(StubHttpHandler.Json(json), "https://example.com/m.json");

        await Assert.That(() => provider.CheckAsync()).ThrowsExactly<NotSupportedException>();
    }

    [Test]
    public async Task CheckAsync_LoadsManifestFromLocalFile()
    {
        // 安排：本地 file:// 清单，无需网络
        var json = """{"version":"3.1.0","artifacts":[{"url":"https://example.com/a.zip","platform":"windows","arch":"amd64"}]}""";
        var filePath = Path.Combine(Path.GetTempPath(), $"endpoint_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        try
        {
            var handler = StubHttpHandler.Json("{}");
            var fileUri = new Uri(filePath).ToString();
            var provider = Create(handler, fileUri);

            var manifest = await provider.CheckAsync();

            await Assert.That(manifest).IsNotNull();
            await Assert.That(manifest!.Version).IsEqualTo("3.1.0");
            await Assert.That(handler.Requests.Count).IsEqualTo(0);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
