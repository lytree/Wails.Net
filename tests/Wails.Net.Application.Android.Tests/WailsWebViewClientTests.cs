using System.Reflection;
using Android.Webkit;
using TUnit.Core;
using Wails.Net.Application.Windows;
using Wails.Net.AssetServer;

namespace Wails.Net.Application.Android.Tests;

/// <summary>
/// WailsWebViewClient 的单元测试（TUnit）。
/// 测试资源拦截器在无 AssetServer 时的降级行为，以及有 AssetServer 时的引用存储。
/// 完整的 AssetServer 集成测试需要真实 AssetServer 实例，由 Application 集成测试覆盖。
/// </summary>
[NotInParallel]
public sealed class WailsWebViewClientTests
{
    [Test]
    public async Task Constructor_WithNullAssetServer_DoesNotThrow()
    {
        // 操作与断言：null AssetServer 应被允许（降级到默认加载）
        var client = new WailsWebViewClient(null);
        await Assert.That(client).IsNotNull();
    }

    [Test]
    public async Task ShouldInterceptRequest_ReturnsNull_WhenNoAssetServer()
    {
        // 安排：无 AssetServer 配置
        var client = new WailsWebViewClient(null);

        // 操作：传入 null WebView 与 null IWebResourceRequest
        // 由于 _assetServer 为 null，方法会在第一个检查就返回 null，
        // 不会实际访问 request 参数。
        WebResourceResponse? result = client.ShouldInterceptRequest(null, (IWebResourceRequest?)null);

        // 断言：无 AssetServer 时返回 null，使用 WebView 默认加载行为
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Constructor_WithNonNullAssetServer_DoesNotThrow()
    {
        // 安排：使用临时目录创建 FileAssetServer 实例
        var tempDir = Path.Combine(Path.GetTempPath(), "WailsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var assetServer = new FileAssetServer(tempDir);

            // 操作与断言：非 null AssetServer 应被存储
            var client = new WailsWebViewClient(assetServer);
            await Assert.That(client).IsNotNull();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Test]
    public async Task Constructor_WithNonNullAssetServer_StoresReference()
    {
        // 安排：使用反射验证 _assetServer 字段被正确存储
        var tempDir = Path.Combine(Path.GetTempPath(), "WailsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var assetServer = new FileAssetServer(tempDir);
            var client = new WailsWebViewClient(assetServer);

            // 操作：通过反射读取私有 _assetServer 字段
            var field = typeof(WailsWebViewClient).GetField(
                "_assetServer",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var storedValue = field?.GetValue(client);

            // 断言：存储的引用应与传入的实例相同
            await Assert.That(storedValue).IsSameReferenceAs(assetServer);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Test]
    public async Task ShouldInterceptRequest_ReturnsNull_WhenRequestIsNull_ButAssetServerExists()
    {
        // 安排：配置了 AssetServer，但传入 null request
        var tempDir = Path.Combine(Path.GetTempPath(), "WailsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var assetServer = new FileAssetServer(tempDir);
            var client = new WailsWebViewClient(assetServer);

            // 操作：传入 null WebView 与 null IWebResourceRequest
            // _assetServer 非 null，但 request 为 null，应返回 null
            WebResourceResponse? result = client.ShouldInterceptRequest(null, (IWebResourceRequest?)null);

            // 断言：request 为 null 时返回 null
            await Assert.That(result).IsNull();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Test]
    public async Task Constructor_WithNullAssetServer_StoresNullReference()
    {
        // 安排：传入 null AssetServer
        var client = new WailsWebViewClient(null);

        // 操作：通过反射读取私有 _assetServer 字段
        var field = typeof(WailsWebViewClient).GetField(
            "_assetServer",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var storedValue = field?.GetValue(client);

        // 断言：存储的引用应为 null
        await Assert.That(storedValue).IsNull();
    }

    [Test]
    public async Task ShouldInterceptRequest_ReturnsNull_WhenViewIsNull_ButAssetServerExists()
    {
        // 安排：配置了 AssetServer，传入 null view（view 参数不参与 null 检查逻辑）
        var tempDir = Path.Combine(Path.GetTempPath(), "WailsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var assetServer = new FileAssetServer(tempDir);
            var client = new WailsWebViewClient(assetServer);

            // 操作：传入 null view 与 null request
            // 由于 request 为 null，方法会在第一个检查返回 null，不访问 view
            WebResourceResponse? result = client.ShouldInterceptRequest(null, (IWebResourceRequest?)null);

            // 断言
            await Assert.That(result).IsNull();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
