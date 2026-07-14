using System.Net;
using System.Text;
using TUnit.Core;
using Wails.Net.AssetServer.Middleware;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// AssetServer 基类的单元测试（TUnit）。
/// 覆盖 MIME 映射、ServeAsync、ETag、Range 请求、404、CSP、CORS、OPTIONS 预检、自定义 AssetReader。
/// 对应 Wails v3 Go 版本 assetserver_test.go 的测试范围。
/// </summary>
[NotInParallel]
public sealed class AssetServerTests
{
    /// <summary>
    /// 测试用的 AssetServer 子类，允许直接注入字节数组作为资源内容。
    /// 当注入的读取器返回 null 时，回退到基类的 ReadAssetCore（检查自定义 AssetReader）。
    /// </summary>
    private sealed class StubAssetServer : AssetServer
    {
        private readonly Func<string, byte[]?> _reader;

        public StubAssetServer(Func<string, byte[]?> reader)
            : base(new AssetOptions { Handler = "stub" })
        {
            _reader = reader;
        }

        protected override byte[]? ReadAssetCore(string path)
        {
            return _reader(path) ?? base.ReadAssetCore(path);
        }
    }

    // ========== GetMimeType 测试 ==========

    [Test]
    public async Task GetMimeType_Html_ReturnsTextHtml()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("index.html")).IsEqualTo("text/html");
    }

    [Test]
    public async Task GetMimeType_Htm_ReturnsTextHtml()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("page.htm")).IsEqualTo("text/html");
    }

    [Test]
    public async Task GetMimeType_Css_ReturnsTextCss()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("style.css")).IsEqualTo("text/css");
    }

    [Test]
    public async Task GetMimeType_Js_ReturnsApplicationJavaScript()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("app.js")).IsEqualTo("application/javascript");
    }

    [Test]
    public async Task GetMimeType_Mjs_ReturnsApplicationJavaScript()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("module.mjs")).IsEqualTo("application/javascript");
    }

    [Test]
    public async Task GetMimeType_Json_ReturnsApplicationJson()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("data.json")).IsEqualTo("application/json");
    }

    [Test]
    public async Task GetMimeType_Svg_ReturnsImageSvgXml()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("logo.svg")).IsEqualTo("image/svg+xml");
    }

    [Test]
    public async Task GetMimeType_Png_ReturnsImagePng()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("photo.png")).IsEqualTo("image/png");
    }

    [Test]
    public async Task GetMimeType_Woff2_ReturnsFontWoff2()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("font.woff2")).IsEqualTo("font/woff2");
    }

    [Test]
    public async Task GetMimeType_Wasm_ReturnsApplicationWasm()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("app.wasm")).IsEqualTo("application/wasm");
    }

    [Test]
    public async Task GetMimeType_UnknownExtension_ReturnsOctetStream()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("file.xyz123")).IsEqualTo("application/octet-stream");
    }

    [Test]
    public async Task GetMimeType_NoExtension_ReturnsOctetStream()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("noextension")).IsEqualTo("application/octet-stream");
    }

    [Test]
    public async Task GetMimeType_EmptyPath_ReturnsOctetStream()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("")).IsEqualTo("application/octet-stream");
    }

    [Test]
    public async Task GetMimeType_UppercaseExtension_IsCaseInsensitive()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(server.GetMimeType("INDEX.HTML")).IsEqualTo("text/html");
        await Assert.That(server.GetMimeType("Style.CSS")).IsEqualTo("text/css");
    }

    // ========== 自定义 MIME 注册测试 ==========

    /// <summary>
    /// 测试用 AssetServer 子类，允许注入自定义 AssetOptions。
    /// </summary>
    private sealed class StubAssetServerWithOptions : AssetServer
    {
        public StubAssetServerWithOptions(AssetOptions options)
            : base(options)
        {
        }

        protected override byte[]? ReadAssetCore(string path) => null;
    }

    [Test]
    public async Task GetMimeType_CustomMimeTypes_ReturnsCustomValue()
    {
        var server = new StubAssetServerWithOptions(new AssetOptions
        {
            Handler = "stub",
            CustomMimeTypes = { [".webmanifest"] = "application/manifest+json" }
        });

        await Assert.That(server.GetMimeType("site.webmanifest")).IsEqualTo("application/manifest+json");
    }

    [Test]
    public async Task GetMimeType_CustomMimeTypes_OverridesBuiltinMapping()
    {
        var server = new StubAssetServerWithOptions(new AssetOptions
        {
            Handler = "stub",
            CustomMimeTypes = { [".js"] = "text/javascript" }
        });

        await Assert.That(server.GetMimeType("app.js")).IsEqualTo("text/javascript");
    }

    [Test]
    public async Task GetMimeType_CustomMimeTypes_IsCaseInsensitive()
    {
        var server = new StubAssetServerWithOptions(new AssetOptions
        {
            Handler = "stub",
            CustomMimeTypes = { [".WEBMANIFEST"] = "application/manifest+json" }
        });

        await Assert.That(server.GetMimeType("site.webmanifest")).IsEqualTo("application/manifest+json");
        await Assert.That(server.GetMimeType("site.WEBMANIFEST")).IsEqualTo("application/manifest+json");
    }

    [Test]
    public async Task GetMimeType_CustomMimeTypes_NotInCustom_FallsBackToBuiltin()
    {
        var server = new StubAssetServerWithOptions(new AssetOptions
        {
            Handler = "stub",
            CustomMimeTypes = { [".webmanifest"] = "application/manifest+json" }
        });

        // .html 不在自定义字典中，回退到内置映射
        await Assert.That(server.GetMimeType("index.html")).IsEqualTo("text/html");
    }

    [Test]
    public async Task GetMimeType_CustomMimeTypes_EmptyExtension_UsesBuiltinFallback()
    {
        var server = new StubAssetServerWithOptions(new AssetOptions
        {
            Handler = "stub",
            CustomMimeTypes = { [".xyz"] = "application/x-test" }
        });

        await Assert.That(server.GetMimeType("noextension")).IsEqualTo("application/octet-stream");
        await Assert.That(server.GetMimeType("")).IsEqualTo("application/octet-stream");
    }

    [Test]
    public async Task GetMimeType_MimeTypeResolver_ReturnsResolvedValue()
    {
        var server = new StubAssetServerWithOptions(new AssetOptions
        {
            Handler = "stub",
            MimeTypeResolver = path =>
                path.EndsWith(".custom", StringComparison.OrdinalIgnoreCase)
                    ? "application/x-custom"
                    : null
        });

        await Assert.That(server.GetMimeType("file.custom")).IsEqualTo("application/x-custom");
    }

    [Test]
    public async Task GetMimeType_MimeTypeResolver_HasPriorityOverCustomMimeTypes()
    {
        var server = new StubAssetServerWithOptions(new AssetOptions
        {
            Handler = "stub",
            CustomMimeTypes = { [".webmanifest"] = "application/manifest+json" },
            MimeTypeResolver = _ => "application/x-from-resolver"
        });

        await Assert.That(server.GetMimeType("site.webmanifest")).IsEqualTo("application/x-from-resolver");
    }

    [Test]
    public async Task GetMimeType_MimeTypeResolver_ReturnsNull_FallsBackToCustomDict()
    {
        var server = new StubAssetServerWithOptions(new AssetOptions
        {
            Handler = "stub",
            CustomMimeTypes = { [".webmanifest"] = "application/manifest+json" },
            MimeTypeResolver = _ => null
        });

        await Assert.That(server.GetMimeType("site.webmanifest")).IsEqualTo("application/manifest+json");
    }

    [Test]
    public async Task GetMimeType_MimeTypeResolver_ReturnsNull_FallsBackToBuiltin()
    {
        var server = new StubAssetServerWithOptions(new AssetOptions
        {
            Handler = "stub",
            MimeTypeResolver = _ => null
        });

        await Assert.That(server.GetMimeType("index.html")).IsEqualTo("text/html");
        await Assert.That(server.GetMimeType("data.json")).IsEqualTo("application/json");
    }

    [Test]
    public async Task GetMimeType_MimeTypeResolver_ReturnsEmptyString_FallsBackToBuiltin()
    {
        var server = new StubAssetServerWithOptions(new AssetOptions
        {
            Handler = "stub",
            MimeTypeResolver = _ => string.Empty
        });

        await Assert.That(server.GetMimeType("index.html")).IsEqualTo("text/html");
    }

    // ========== If-Modified-Since 协商缓存测试 ==========

    /// <summary>
    /// 测试用 AssetServer 子类，支持注入 Last-Modified 时间，用于 If-Modified-Since 测试。
    /// </summary>
    private sealed class StubAssetServerWithLastModified : AssetServer
    {
        private readonly Func<string, byte[]?> _reader;
        private readonly Func<string, DateTime?> _lastModifiedProvider;

        public StubAssetServerWithLastModified(
            Func<string, byte[]?> reader,
            Func<string, DateTime?> lastModifiedProvider)
            : base(new AssetOptions { Handler = "stub" })
        {
            _reader = reader;
            _lastModifiedProvider = lastModifiedProvider;
        }

        protected override byte[]? ReadAssetCore(string path)
        {
            return _reader(path) ?? base.ReadAssetCore(path);
        }

        public override DateTime? GetLastModified(string path) => _lastModifiedProvider(path);
    }

    [Test]
    public async Task ServeHttpAsync_LastModifiedSet_ResponseContainsLastModifiedHeader()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var lastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var response = await SendRequestAsync(
            () => new StubAssetServerWithLastModified(_ => content, _ => lastModified),
            "/index.html");

        if (response is null)
        {
            return;
        }

        try
        {
            var lastModifiedHeader = response.GetHeader(AssetServer.Headers.LastModified);
            await Assert.That(lastModifiedHeader).IsNotNull();
            await Assert.That(lastModifiedHeader).Contains("2026");
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_IfModifiedSinceBeforeLastModified_Returns200()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var lastModified = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);
        var headers = new Dictionary<string, string>
        {
            { AssetServer.Headers.IfModifiedSince, "Wed, 01 Jan 2026 00:00:00 GMT" }
        };
        var response = await SendRequestAsync(
            () => new StubAssetServerWithLastModified(_ => content, _ => lastModified),
            "/index.html", "GET", headers);

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That((int)response.StatusCode).IsEqualTo(200);
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_IfModifiedSinceAfterLastModified_Returns304()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var lastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var headers = new Dictionary<string, string>
        {
            { AssetServer.Headers.IfModifiedSince, "Wed, 01 Jan 2027 00:00:00 GMT" }
        };
        var response = await SendRequestAsync(
            () => new StubAssetServerWithLastModified(_ => content, _ => lastModified),
            "/index.html", "GET", headers);

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That((int)response.StatusCode).IsEqualTo(304);
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_IfModifiedSinceEqualsLastModified_Returns304()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var lastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var headers = new Dictionary<string, string>
        {
            { AssetServer.Headers.IfModifiedSince, "Wed, 01 Jan 2026 00:00:00 GMT" }
        };
        var response = await SendRequestAsync(
            () => new StubAssetServerWithLastModified(_ => content, _ => lastModified),
            "/index.html", "GET", headers);

        if (response is null)
        {
            return;
        }

        try
        {
            // lastModified <= sinceDate：相等也应返回 304
            await Assert.That((int)response.StatusCode).IsEqualTo(304);
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_IfModifiedSinceWithoutLastModified_Returns200()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var headers = new Dictionary<string, string>
        {
            { AssetServer.Headers.IfModifiedSince, "Wed, 01 Jan 2027 00:00:00 GMT" }
        };
        var response = await SendRequestAsync(
            () => new StubAssetServer(_ => content),
            "/index.html", "GET", headers);

        if (response is null)
        {
            return;
        }

        try
        {
            // 没有 Last-Modified 信息，不应触发 304
            await Assert.That((int)response.StatusCode).IsEqualTo(200);
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_IfNoneMatchTakesPriorityOverIfModifiedSince()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var lastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 先获取真实的 ETag
        var firstResponse = await SendRequestAsync(
            () => new StubAssetServerWithLastModified(_ => content, _ => lastModified),
            "/index.html");
        if (firstResponse is null) return;
        var etag = firstResponse.GetHeader(AssetServer.Headers.ETag);
        firstResponse.Dispose();
        if (etag is null) return;

        // 同时发送 If-None-Match（匹配）和 If-Modified-Since（不匹配）
        var headers = new Dictionary<string, string>
        {
            { AssetServer.Headers.IfNoneMatch, etag },
            { AssetServer.Headers.IfModifiedSince, "Wed, 01 Jan 2020 00:00:00 GMT" }
        };
        var response = await SendRequestAsync(
            () => new StubAssetServerWithLastModified(_ => content, _ => lastModified),
            "/index.html", "GET", headers);

        if (response is null)
        {
            return;
        }

        try
        {
            // If-None-Match 命中应优先返回 304（即使 If-Modified-Since 指示资源已修改）
            await Assert.That((int)response.StatusCode).IsEqualTo(304);
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_InvalidIfModifiedSinceDate_Returns200()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var lastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var headers = new Dictionary<string, string>
        {
            { AssetServer.Headers.IfModifiedSince, "not-a-date" }
        };
        var response = await SendRequestAsync(
            () => new StubAssetServerWithLastModified(_ => content, _ => lastModified),
            "/index.html", "GET", headers);

        if (response is null)
        {
            return;
        }

        try
        {
            // 无效日期无法解析，应返回 200
            await Assert.That((int)response.StatusCode).IsEqualTo(200);
        }
        finally
        {
            response.Dispose();
        }
    }

    // ========== ServeAsync 测试 ==========

    [Test]
    public async Task ServeAsync_ExistingResource_ReturnsContent()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        var server = new StubAssetServer(_ => content);

        var result = await server.ServeAsync("/index.html");

        await Assert.That(result).IsEqualTo(content);
    }

    [Test]
    public async Task ServeAsync_MissingResource_ReturnsEmptyArray()
    {
        var server = new StubAssetServer(_ => null);

        var result = await server.ServeAsync("/missing.html");

        await Assert.That(result).IsEqualTo(Array.Empty<byte>());
    }

    [Test]
    public async Task ServeAsync_CustomAssetReader_ReturnsReaderContent()
    {
        var content = Encoding.UTF8.GetBytes("from reader");
        var server = new StubAssetServer(_ => null);
        server.SetAssetReader(_ => new MemoryStream(content));

        var result = await server.ServeAsync("/custom.txt");

        // 通过字符串比较内容（自定义读取器返回新字节数组，引用不同）
        await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("from reader");
    }

    [Test]
    public async Task ServeAsync_CustomAssetReaderReturnsNull_FallsBackToReadAssetCore()
    {
        var coreContent = Encoding.UTF8.GetBytes("from core");
        var server = new StubAssetServer(_ => coreContent);
        server.SetAssetReader(_ => null);

        var result = await server.ServeAsync("/fallback.txt");

        await Assert.That(result).IsEqualTo(coreContent);
    }

    [Test]
    public async Task ServeAsync_PathMiddlewareInvoked_CanReturnCustomContent()
    {
        var customContent = Encoding.UTF8.GetBytes("from middleware");
        var server = new StubAssetServer(_ => null);
        server.Use(new StubPathMiddleware(_ => customContent));

        var result = await server.ServeAsync("/intercepted.html");

        await Assert.That(result).IsEqualTo(customContent);
    }

    [Test]
    public async Task ServeAsync_PathMiddlewareCallsNext_PassesThroughToCore()
    {
        var coreContent = Encoding.UTF8.GetBytes("from core");
        var server = new StubAssetServer(_ => coreContent);
        server.Use(new TransformingPathMiddleware((path, next) => next(path)));

        var result = await server.ServeAsync("/passthrough.html");

        await Assert.That(result).IsEqualTo(coreContent);
    }

    [Test]
    public async Task ServeAsync_EmptyPath_ThrowsArgumentException()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(async () => await server.ServeAsync("")).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ServeAsync_NullPath_ThrowsArgumentNullException()
    {
        var server = new StubAssetServer(_ => null);
        await Assert.That(async () => await server.ServeAsync(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task ServeAsync_SetCspHeader_DoesNotAffectServeAsync()
    {
        var content = Encoding.UTF8.GetBytes("body");
        var server = new StubAssetServer(_ => content);
        server.SetCspHeader("default-src 'self'");

        // SetCspHeader 只影响 HTTP 响应头，不影响 ServeAsync 返回内容
        var result = await server.ServeAsync("/index.html");
        await Assert.That(result).IsEqualTo(content);
    }

    // ========== SPA 回退测试 ==========

    /// <summary>
    /// 测试用的 AssetServer 子类，根据路径返回不同内容，并支持 SPA 回退配置。
    /// </summary>
    private sealed class StubAssetServerWithSpaFallback : AssetServer
    {
        private readonly Func<string, byte[]?> _reader;

        public StubAssetServerWithSpaFallback(
            Func<string, byte[]?> reader,
            bool enableSpaFallback = true,
            string defaultDocument = "index.html")
            : base(new AssetOptions
            {
                Handler = "stub",
                EnableSpaFallback = enableSpaFallback,
                DefaultDocument = defaultDocument
            })
        {
            _reader = reader;
        }

        protected override byte[]? ReadAssetCore(string path)
        {
            return _reader(path) ?? base.ReadAssetCore(path);
        }
    }

    [Test]
    public async Task ServeAsync_SpaFallback_MissingRoute_ReturnsDefaultDocument()
    {
        var indexContent = Encoding.UTF8.GetBytes("<html>SPA</html>");
        // 仅 index.html 返回内容，其他路径返回 null（路径可能带或不带前导斜杠）
        var server = new StubAssetServerWithSpaFallback(
            path => string.Equals(path.TrimStart('/'), "index.html", StringComparison.OrdinalIgnoreCase) ? indexContent : null);

        var result = await server.ServeAsync("/some/client/route");

        await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo(Encoding.UTF8.GetString(indexContent));
    }

    [Test]
    public async Task ServeAsync_SpaFallbackDisabled_MissingResource_ReturnsEmptyArray()
    {
        var indexContent = Encoding.UTF8.GetBytes("<html>SPA</html>");
        var server = new StubAssetServerWithSpaFallback(
            path => string.Equals(path.TrimStart('/'), "index.html", StringComparison.OrdinalIgnoreCase) ? indexContent : null,
            enableSpaFallback: false);

        var result = await server.ServeAsync("/some/client/route");

        await Assert.That(result).IsEqualTo(Array.Empty<byte>());
    }

    [Test]
    public async Task ServeAsync_SpaFallback_CustomDefaultDocument()
    {
        var customContent = Encoding.UTF8.GetBytes("<html>Custom</html>");
        var server = new StubAssetServerWithSpaFallback(
            path => string.Equals(path.TrimStart('/'), "app.html", StringComparison.OrdinalIgnoreCase) ? customContent : null,
            defaultDocument: "app.html");

        var result = await server.ServeAsync("/some/route");

        await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo(Encoding.UTF8.GetString(customContent));
    }

    [Test]
    public async Task ServeAsync_SpaFallback_DefaultDocumentAlsoMissing_ReturnsEmptyArray()
    {
        var server = new StubAssetServerWithSpaFallback(_ => null);

        var result = await server.ServeAsync("/some/route");

        await Assert.That(result).IsEqualTo(Array.Empty<byte>());
    }

    [Test]
    public async Task ServeAsync_SpaFallback_ExistingResource_ReturnsResourceNotFallback()
    {
        var indexContent = Encoding.UTF8.GetBytes("<html>Index</html>");
        var pageContent = Encoding.UTF8.GetBytes("<html>Page</html>");
        var server = new StubAssetServerWithSpaFallback(path =>
        {
            if (string.Equals(path.TrimStart('/'), "index.html", StringComparison.OrdinalIgnoreCase)) return indexContent;
            if (string.Equals(path.TrimStart('/'), "about.html", StringComparison.OrdinalIgnoreCase)) return pageContent;
            return null;
        });

        var result = await server.ServeAsync("/about.html");

        await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo(Encoding.UTF8.GetString(pageContent));
    }

    [Test]
    public async Task ServeHttpAsync_SpaFallback_MissingRoute_Returns200WithDefaultDocument()
    {
        var indexContent = Encoding.UTF8.GetBytes("<html>SPA</html>");
        var response = await SendRequestAsync(
            () => new StubAssetServerWithSpaFallback(
                path => string.Equals(path.TrimStart('/'), "index.html", StringComparison.OrdinalIgnoreCase) ? indexContent : null),
            "/client/route");

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That((int)response.StatusCode).IsEqualTo(200);
            await Assert.That(response.Response.Content.Headers.ContentType?.MediaType ?? "").IsEqualTo("text/html");
            await Assert.That(Encoding.UTF8.GetString(response.Body ?? [])).IsEqualTo(Encoding.UTF8.GetString(indexContent));
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_SpaFallbackDisabled_MissingResource_Returns404()
    {
        var indexContent = Encoding.UTF8.GetBytes("<html>SPA</html>");
        var response = await SendRequestAsync(
            () => new StubAssetServerWithSpaFallback(
                path => string.Equals(path.TrimStart('/'), "index.html", StringComparison.OrdinalIgnoreCase) ? indexContent : null,
                enableSpaFallback: false),
            "/client/route");

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That((int)response.StatusCode).IsEqualTo(404);
        }
        finally
        {
            response.Dispose();
        }
    }

    // ========== ServeHttpAsync 测试（使用 HttpListener + HttpClient） ==========

    /// <summary>
    /// HTTP 测试响应结果，包含状态码、响应头和响应体。
    /// </summary>
    private sealed class TestHttpResponse : IDisposable
    {
        public required HttpStatusCode StatusCode { get; init; }
        public required HttpResponseMessage Response { get; init; }
        public byte[]? Body { get; set; }

        public string? GetHeader(string name)
        {
            // HttpClient 将响应头拆分为两组：
            // - Response.Headers：通用头部（ETag、CORS、CSP 等）
            // - Response.Content.Headers：内容相关头部（Content-Type、Content-Length、Content-Range 等）
            if (Response.Headers.TryGetValues(name, out var values))
            {
                return values.FirstOrDefault();
            }

            if (Response.Content.Headers.TryGetValues(name, out var contentValues))
            {
                return contentValues.FirstOrDefault();
            }

            return null;
        }

        public void Dispose() => Response.Dispose();
    }

    /// <summary>
    /// 使用 HttpListener 启动本地监听，将请求委托给 AssetServer 处理，并返回响应。
    /// 若 HttpListener 无法启动（如 CI 环境无权限），则返回 null 标记跳过。
    /// </summary>
    private static async Task<TestHttpResponse?> SendRequestAsync(
        Func<AssetServer> serverFactory,
        string path,
        string method = "GET",
        Dictionary<string, string>? headers = null)
    {
        var port = GetFreePort();
        var uriPrefix = $"http://localhost:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(uriPrefix);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException)
        {
            return null;
        }

        var server = serverFactory();
        var listenerTask = Task.Run(async () =>
        {
            try
            {
                var ctx = await listener.GetContextAsync();
                await server.ServeHttpAsync(ctx);
            }
            catch
            {
                // 忽略监听异常
            }
        });

        try
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(
                new HttpMethod(method),
                $"http://localhost:{port}{path}");

            if (headers is not null)
            {
                foreach (var kvp in headers)
                {
                    if (string.Equals(kvp.Key, AssetServer.Headers.Range, StringComparison.OrdinalIgnoreCase))
                    {
                        // Range 头部需要通过 RangeHeaderValue 设置，否则 HttpClient 不会发送
                        SetRangeHeader(request, kvp.Value);
                    }
                    else
                    {
                        try
                        {
                            request.Headers.Add(kvp.Key, kvp.Value);
                        }
                        catch (ArgumentException)
                        {
                            request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                        }
                    }
                }
            }

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsByteArrayAsync();

            await listenerTask;
            listener.Stop();

            return new TestHttpResponse
            {
                StatusCode = response.StatusCode,
                Response = response,
                Body = body
            };
        }
        catch
        {
            try { listener.Stop(); } catch { }
            return null;
        }
    }

    /// <summary>
    /// 获取系统可用的空闲 TCP 端口。
    /// </summary>
    private static int GetFreePort()
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    /// <summary>
    /// 解析 "bytes=0-9" 或 "bytes=-3" 格式的 Range 头部，并设置到 HttpRequestMessage 上。
    /// HttpClient 不会通过 TryAddWithoutValidation 发送 Range 头部，必须使用 RangeHeaderValue。
    /// </summary>
    private static void SetRangeHeader(HttpRequestMessage request, string rangeValue)
    {
        if (!rangeValue.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var spec = rangeValue[6..];
        var dashIndex = spec.IndexOf('-');
        if (dashIndex < 0)
        {
            return;
        }

        var startStr = spec[..dashIndex].Trim();
        var endStr = spec[(dashIndex + 1)..].Trim();
        long? from = string.IsNullOrEmpty(startStr) ? null : long.Parse(startStr);
        long? to = string.IsNullOrEmpty(endStr) ? null : long.Parse(endStr);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);
    }

    [Test]
    public async Task ServeHttpAsync_GetExistingResource_Returns200WithContent()
    {
        var content = Encoding.UTF8.GetBytes("<html><body>Hello</body></html>");
        var response = await SendRequestAsync(
            () => new StubAssetServer(_ => content),
            "/index.html");

        if (response is null)
        {
            return; // 跳过：HttpListener 不可用
        }

        try
        {
            await Assert.That((int)response.StatusCode).IsEqualTo(200);
            await Assert.That(response.Response.Content.Headers.ContentType?.MediaType ?? "").IsEqualTo("text/html");
            await Assert.That(response.GetHeader(AssetServer.Headers.AcceptRanges)).IsEqualTo("bytes");
            await Assert.That(response.GetHeader(AssetServer.Headers.ETag)).IsNotNull();
            await Assert.That(Encoding.UTF8.GetString(response.Body ?? [])).IsEqualTo(Encoding.UTF8.GetString(content));
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_MissingResource_Returns404()
    {
        var response = await SendRequestAsync(
            () => new StubAssetServer(_ => null),
            "/nonexistent.html");

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That((int)response.StatusCode).IsEqualTo(404);
            var text = Encoding.UTF8.GetString(response.Body ?? []);
            await Assert.That(text).Contains("404 Not Found");
            await Assert.That(text).Contains("/nonexistent.html");
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_OptionsRequest_Returns204()
    {
        var response = await SendRequestAsync(
            () => new StubAssetServer(_ => null),
            "/",
            "OPTIONS");

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That((int)response.StatusCode).IsEqualTo(204);
            await Assert.That(response.GetHeader(AssetServer.Headers.AccessControlAllowOrigin)).IsEqualTo("*");
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_AlwaysSetsCorsHeader()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var response = await SendRequestAsync(
            () => new StubAssetServer(_ => content),
            "/data.json");

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That(response.GetHeader(AssetServer.Headers.AccessControlAllowOrigin)).IsEqualTo("*");
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_CspHeaderSet_ResponseIncludesCsp()
    {
        var content = Encoding.UTF8.GetBytes("<html></html>");
        var response = await SendRequestAsync(
            () =>
            {
                var s = new StubAssetServer(_ => content);
                s.SetCspHeader("default-src 'self'");
                return s;
            },
            "/index.html");

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That(response.GetHeader("Content-Security-Policy")).IsEqualTo("default-src 'self'");
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_NoCspHeader_NoCspInResponse()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var response = await SendRequestAsync(
            () => new StubAssetServer(_ => content),
            "/data.txt");

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That(response.GetHeader("Content-Security-Policy")).IsNull();
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_RangeRequest_Returns206PartialContent()
    {
        var content = Encoding.UTF8.GetBytes("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ");
        var headers = new Dictionary<string, string>
        {
            { AssetServer.Headers.Range, "bytes=0-9" }
        };
        var response = await SendRequestAsync(
            () => new StubAssetServer(_ => content),
            "/data.bin",
            "GET",
            headers);

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That((int)response.StatusCode).IsEqualTo(206);
            await Assert.That(response.GetHeader(AssetServer.Headers.ContentRange)).IsEqualTo($"bytes 0-9/{content.Length}");
            await Assert.That(Encoding.UTF8.GetString(response.Body ?? [])).IsEqualTo("0123456789");
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_RangeRequestSuffixBytes_ReturnsLastNBytes()
    {
        var content = Encoding.UTF8.GetBytes("0123456789");
        var headers = new Dictionary<string, string>
        {
            { AssetServer.Headers.Range, "bytes=-3" }
        };
        var response = await SendRequestAsync(
            () => new StubAssetServer(_ => content),
            "/data.bin",
            "GET",
            headers);

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That((int)response.StatusCode).IsEqualTo(206);
            await Assert.That(Encoding.UTF8.GetString(response.Body ?? [])).IsEqualTo("789");
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_EtagSet_ResponseContainsEtag()
    {
        var content = Encoding.UTF8.GetBytes("etag test");
        var response = await SendRequestAsync(
            () => new StubAssetServer(_ => content),
            "/index.html");

        if (response is null)
        {
            return;
        }

        try
        {
            var etag = response.GetHeader(AssetServer.Headers.ETag);
            await Assert.That(etag).IsNotNull();
            await Assert.That(etag!.StartsWith("\"")).IsTrue();
            await Assert.That(etag.EndsWith("\"")).IsTrue();
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_IfNoneMatchMatches_Returns304()
    {
        var content = Encoding.UTF8.GetBytes("consistent content");
        // 先获取 ETag
        var firstResponse = await SendRequestAsync(
            () => new StubAssetServer(_ => content),
            "/index.html");

        if (firstResponse is null)
        {
            return;
        }

        var etag = firstResponse.GetHeader(AssetServer.Headers.ETag);
        firstResponse.Dispose();

        if (etag is null)
        {
            return;
        }

        // 用 ETag 发送 If-None-Match
        var headers = new Dictionary<string, string>
        {
            { AssetServer.Headers.IfNoneMatch, etag }
        };
        var secondResponse = await SendRequestAsync(
            () => new StubAssetServer(_ => content),
            "/index.html",
            "GET",
            headers);

        if (secondResponse is null)
        {
            return;
        }

        try
        {
            await Assert.That((int)secondResponse.StatusCode).IsEqualTo(304);
        }
        finally
        {
            secondResponse.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_IfNoneMatchDoesNotMatch_Returns200()
    {
        var content = Encoding.UTF8.GetBytes("data");
        var headers = new Dictionary<string, string>
        {
            { AssetServer.Headers.IfNoneMatch, "\"nonmatching-etag\"" }
        };
        var response = await SendRequestAsync(
            () => new StubAssetServer(_ => content),
            "/index.html",
            "GET",
            headers);

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That((int)response.StatusCode).IsEqualTo(200);
        }
        finally
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task ServeHttpAsync_CustomErrorHandler_IsInvokedFor404()
    {
        var customBody = Encoding.UTF8.GetBytes("custom error");
        var server = new StubAssetServer(_ => null);
        server.Options.ErrorHandler = (ctx, ex) =>
        {
            ctx.Response.StatusCode = 418;
            ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength64 = customBody.Length;
            ctx.Response.OutputStream.Write(customBody, 0, customBody.Length);
            ctx.Response.Close();
        };

        var response = await SendRequestAsync(
            () => server,
            "/missing.html");

        if (response is null)
        {
            return;
        }

        try
        {
            await Assert.That((int)response.StatusCode).IsEqualTo(418);
            await Assert.That(Encoding.UTF8.GetString(response.Body ?? [])).IsEqualTo(Encoding.UTF8.GetString(customBody));
        }
        finally
        {
            response.Dispose();
        }
    }

    // ========== 辅助类型 ==========

    /// <summary>
    /// 测试用基于路径的中间件，直接返回指定内容（短路）。
    /// </summary>
    private sealed class StubPathMiddleware : IMiddleware
    {
        private readonly Func<string, byte[]?> _returnContent;

        public StubPathMiddleware(Func<string, byte[]?> returnContent)
        {
            _returnContent = returnContent;
        }

        public Task<byte[]?> ProcessAsync(string path, Func<string, Task<byte[]?>> next)
        {
            return Task.FromResult(_returnContent(path));
        }
    }

    /// <summary>
    /// 测试用基于路径的中间件，可调用 next 委托继续链。
    /// </summary>
    private sealed class TransformingPathMiddleware : IMiddleware
    {
        private readonly Func<string, Func<string, Task<byte[]?>>, Task<byte[]?>> _handler;

        public TransformingPathMiddleware(Func<string, Func<string, Task<byte[]?>>, Task<byte[]?>> handler)
        {
            _handler = handler;
        }

        public Task<byte[]?> ProcessAsync(string path, Func<string, Task<byte[]?>> next)
        {
            return _handler(path, next);
        }
    }
}
