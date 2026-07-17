using System.Text;
using TUnit.Core;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// AssetServer 与 IAssetProvider 组合模式的集成测试（TUnit）。
/// 验证 M7 新增的 AssetServer(AssetOptions, IAssetProvider) 构造函数、
/// ReadAssetCore 委托逻辑及 GetLastModified 委托逻辑。
/// </summary>
[NotInParallel]
public sealed class AssetProviderIntegrationTests
{
    /// <summary>
    /// 用于测试的内存资源提供者，从字典读取资源。
    /// </summary>
    private sealed class MemoryAssetProvider : IAssetProvider
    {
        private readonly Dictionary<string, byte[]> _resources;
        private readonly Dictionary<string, DateTime> _lastModified;

        public MemoryAssetProvider(Dictionary<string, byte[]> resources, Dictionary<string, DateTime>? lastModified = null)
        {
            _resources = resources;
            _lastModified = lastModified ?? new Dictionary<string, DateTime>();
        }

        public Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_resources.TryGetValue(path, out var content) ? content : null);
        }

        public DateTime? GetLastModified(string path)
        {
            return _lastModified.TryGetValue(path, out var time) ? time : null;
        }
    }

    // ========== 组合模式构造函数测试 ==========

    [Test]
    public async Task Constructor_WithProvider_StoresProvider()
    {
        var options = new AssetOptions { Handler = "test" };
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>());
        var server = new AssetServer(options, provider);

        await Assert.That(server.Options.Handler).IsEqualTo("test");
    }

    [Test]
    public async Task Constructor_NullProvider_ThrowsArgumentNullException()
    {
        var options = new AssetOptions { Handler = "test" };
        await Assert.That(() => new AssetServer(options, (IAssetProvider)null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    // ========== ReadAssetCore 委托测试 ==========

    [Test]
    public async Task ServeAsync_WithProvider_ReturnsProviderContent()
    {
        var options = new AssetOptions { Handler = "test" };
        var resources = new Dictionary<string, byte[]>
        {
            ["index.html"] = Encoding.UTF8.GetBytes("<html><body>Provider</body></html>")
        };
        var provider = new MemoryAssetProvider(resources);
        var server = new AssetServer(options, provider);

        var result = await server.ServeAsync("index.html");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("<html><body>Provider</body></html>");
    }

    [Test]
    public async Task ServeAsync_WithProvider_ResourceNotFound_ReturnsEmptyArray()
    {
        var options = new AssetOptions { Handler = "test" };
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>());
        var server = new AssetServer(options, provider);

        var result = await server.ServeAsync("nonexistent.html");

        await Assert.That(result).IsEqualTo(Array.Empty<byte>());
    }

    // ========== GetLastModified 委托测试 ==========

    [Test]
    public async Task GetLastModified_WithProvider_ReturnsProviderTime()
    {
        var options = new AssetOptions { Handler = "test" };
        var expectedTime = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var resources = new Dictionary<string, byte[]>
        {
            ["index.html"] = Encoding.UTF8.GetBytes("<html></html>")
        };
        var lastModified = new Dictionary<string, DateTime>
        {
            ["index.html"] = expectedTime
        };
        var provider = new MemoryAssetProvider(resources, lastModified);
        var server = new AssetServer(options, provider);

        var result = server.GetLastModified("index.html");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value).IsEqualTo(expectedTime);
    }

    [Test]
    public async Task GetLastModified_WithProvider_NoTime_ReturnsNull()
    {
        var options = new AssetOptions { Handler = "test" };
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>());
        var server = new AssetServer(options, provider);

        var result = server.GetLastModified("any/path.html");

        await Assert.That(result).IsNull();
    }

    // ========== 优先级测试：Provider 优先于 CustomAssetReader ==========

    [Test]
    public async Task ReadAssetCore_ProviderTakesPrecedenceOverCustomReader()
    {
        var options = new AssetOptions { Handler = "test" };
        var providerContent = Encoding.UTF8.GetBytes("from provider");
        var customContent = Encoding.UTF8.GetBytes("from custom reader");

        var resources = new Dictionary<string, byte[]>
        {
            ["index.html"] = providerContent
        };
        var provider = new MemoryAssetProvider(resources);
        var server = new AssetServer(options, provider);

        // 设置自定义读取器，但 Provider 应优先
        server.SetAssetReader(_ => new MemoryStream(customContent));

        var result = await server.ServeAsync("index.html");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("from provider");
    }

    [Test]
    public async Task ReadAssetCore_CustomReaderUsedWhenProviderReturnsNull()
    {
        var options = new AssetOptions { Handler = "test" };
        var customContent = Encoding.UTF8.GetBytes("from custom reader");

        // Provider 中没有该资源
        var provider = new MemoryAssetProvider(new Dictionary<string, byte[]>());
        var server = new AssetServer(options, provider);

        // 自定义读取器返回内容
        server.SetAssetReader(path =>
        {
            if (path == "custom.html")
            {
                return new MemoryStream(customContent);
            }
            return null;
        });

        var result = await server.ServeAsync("custom.html");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("from custom reader");
    }

    // ========== 向后兼容测试：无 Provider 时回退到 CustomAssetReader ==========

    [Test]
    public async Task ReadAssetCore_NoProvider_UsesCustomReader()
    {
        var options = new AssetOptions { Handler = "test" };
        var customContent = Encoding.UTF8.GetBytes("from custom reader");
        var server = new AssetServer(options);

        server.SetAssetReader(_ => new MemoryStream(customContent));

        var result = await server.ServeAsync("any.html");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("from custom reader");
    }

    [Test]
    public async Task ReadAssetCore_NoProviderNoCustomReader_ReturnsNull()
    {
        var options = new AssetOptions { Handler = "test" };
        var server = new AssetServer(options);

        var result = await server.ServeAsync("nonexistent.html");

        await Assert.That(result).IsEqualTo(Array.Empty<byte>());
    }
}
