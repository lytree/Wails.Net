using System.Net;
using TUnit.Core;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// AssetOptions 的单元测试（TUnit）。
/// 验证默认值与自定义设置。
/// </summary>
[NotInParallel]
public sealed class AssetOptionsTests
{
    [Test]
    public async Task DefaultConstructor_HandlerIsEmptyString()
    {
        var options = new AssetOptions();
        await Assert.That(options.Handler).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task DefaultConstructor_RootPathIsEmptyString()
    {
        var options = new AssetOptions();
        await Assert.That(options.RootPath).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task DefaultConstructor_MiddlewareIsEmptyDictionary()
    {
        var options = new AssetOptions();
        await Assert.That(options.Middleware.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DefaultConstructor_ErrorHandlerIsNull()
    {
        var options = new AssetOptions();
        await Assert.That(options.ErrorHandler).IsNull();
    }

    [Test]
    public async Task DefaultConstructor_HandlerTimeoutIs30Seconds()
    {
        var options = new AssetOptions();
        await Assert.That(options.HandlerTimeout).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task SetHandler_UpdatesValue()
    {
        var options = new AssetOptions { Handler = "file" };
        await Assert.That(options.Handler).IsEqualTo("file");
    }

    [Test]
    public async Task SetRootPath_UpdatesValue()
    {
        var options = new AssetOptions { RootPath = "/assets" };
        await Assert.That(options.RootPath).IsEqualTo("/assets");
    }

    [Test]
    public async Task SetErrorHandler_UpdatesValue()
    {
        Action<HttpListenerContext, Exception> handler = (_, _) => { };
        var options = new AssetOptions { ErrorHandler = handler };
        await Assert.That(options.ErrorHandler).IsEqualTo(handler);
    }

    [Test]
    public async Task SetHandlerTimeout_UpdatesValue()
    {
        var timeout = TimeSpan.FromMinutes(1);
        var options = new AssetOptions { HandlerTimeout = timeout };
        await Assert.That(options.HandlerTimeout).IsEqualTo(timeout);
    }

    [Test]
    public async Task AddMiddlewareEntry_AddsToDictionary()
    {
        var options = new AssetOptions();
        options.Middleware["cors"] = "enabled";
        await Assert.That(options.Middleware.Count).IsEqualTo(1);
        await Assert.That(options.Middleware["cors"]).IsEqualTo("enabled");
    }

    [Test]
    public async Task DefaultConstructor_EnableSpaFallbackIsFalse()
    {
        var options = new AssetOptions();
        await Assert.That(options.EnableSpaFallback).IsFalse();
    }

    [Test]
    public async Task DefaultConstructor_DefaultDocumentIsIndexHtml()
    {
        var options = new AssetOptions();
        await Assert.That(options.DefaultDocument).IsEqualTo("index.html");
    }

    [Test]
    public async Task SetEnableSpaFallback_UpdatesValue()
    {
        var options = new AssetOptions { EnableSpaFallback = true };
        await Assert.That(options.EnableSpaFallback).IsTrue();
    }

    [Test]
    public async Task SetDefaultDocument_UpdatesValue()
    {
        var options = new AssetOptions { DefaultDocument = "app.html" };
        await Assert.That(options.DefaultDocument).IsEqualTo("app.html");
    }

    [Test]
    public async Task DefaultConstructor_CustomMimeTypesIsEmptyDictionary()
    {
        var options = new AssetOptions();
        await Assert.That(options.CustomMimeTypes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DefaultConstructor_MimeTypeResolverIsNull()
    {
        var options = new AssetOptions();
        await Assert.That(options.MimeTypeResolver).IsNull();
    }

    [Test]
    public async Task CustomMimeTypes_IsCaseInsensitiveComparer()
    {
        var options = new AssetOptions();
        options.CustomMimeTypes[".HTML"] = "text/html";

        // 不区分大小写查找
        await Assert.That(options.CustomMimeTypes.ContainsKey(".html")).IsTrue();
        await Assert.That(options.CustomMimeTypes.TryGetValue(".html", out _)).IsTrue();
    }

    [Test]
    public async Task SetMimeTypeResolver_UpdatesValue()
    {
        Func<string, string?> resolver = _ => "application/x-custom";
        var options = new AssetOptions { MimeTypeResolver = resolver };
        await Assert.That(options.MimeTypeResolver).IsEqualTo(resolver);
    }
}
