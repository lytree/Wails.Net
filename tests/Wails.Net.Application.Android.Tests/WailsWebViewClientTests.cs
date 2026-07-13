using Android.Webkit;
using TUnit.Core;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Android.Tests;

/// <summary>
/// WailsWebViewClient 的单元测试（TUnit）。
/// 测试资源拦截器在无 AssetServer 时的降级行为。
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
}
