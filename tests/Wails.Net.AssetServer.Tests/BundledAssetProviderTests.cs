using System.Text;
using TUnit.Core;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// BundledAssetProvider 的单元测试（TUnit）。
/// 覆盖委托式资源读取、通配符匹配、根路径前缀匹配及 IAssetProvider 接口契约。
/// 对应 M7 新增的组合模式 Provider。
/// <para>
/// 遵循 AGENTS.md §3.4：不使用 System.Reflection.Assembly，资源由测试用字典 + 委托提供。
/// </para>
/// </summary>
[NotInParallel]
public sealed class BundledAssetProviderTests
{
    /// <summary>
    /// 创建测试用资源字典，包含若干已知资源。
    /// </summary>
    /// <param name="extra">可选的额外资源键值对。</param>
    /// <returns>资源字典。</returns>
    private static Dictionary<string, byte[]> CreateTestResources(
        IEnumerable<KeyValuePair<string, byte[]>>? extra = null)
    {
        var resources = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["Wails.Net.AssetServer.Tests.index.html"] = Encoding.UTF8.GetBytes("<html><body>Index</body></html>"),
            ["Wails.Net.AssetServer.Tests.styles.css"] = Encoding.UTF8.GetBytes("body { color: red; }"),
            ["Wails.Net.AssetServer.Tests.scripts.app.js"] = Encoding.UTF8.GetBytes("console.log('app');"),
        };

        if (extra is not null)
        {
            foreach (var kv in extra)
            {
                resources[kv.Key] = kv.Value;
            }
        }

        return resources;
    }

    /// <summary>
    /// 从字典创建资源读取委托。
    /// </summary>
    /// <param name="resources">资源字典。</param>
    /// <returns>资源读取委托。</returns>
    private static Func<string, byte[]?> CreateReader(Dictionary<string, byte[]> resources)
    {
        return name => resources.TryGetValue(name, out var bytes) ? bytes : null;
    }

    /// <summary>
    /// 使用根路径和字典创建 BundledAssetProvider 实例。
    /// </summary>
    private static BundledAssetProvider CreateProvider(
        Dictionary<string, byte[]>? resources = null,
        string rootPath = "Wails.Net.AssetServer.Tests")
    {
        resources ??= CreateTestResources();
        return new BundledAssetProvider(rootPath, CreateReader(resources), resources.Keys);
    }

    // ========== 构造函数与属性测试 ==========

    [Test]
    public async Task Constructor_ValidParameters_SetsRootPath()
    {
        var provider = CreateProvider();
        await Assert.That(provider.RootPath).IsEqualTo("Wails.Net.AssetServer.Tests");
    }

    [Test]
    public async Task Constructor_ValidParameters_RegistersResourceNames()
    {
        var provider = CreateProvider();
        await Assert.That(provider.ResourceNames.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Constructor_NullRootPath_ThrowsArgumentNullException()
    {
        var resources = CreateTestResources();
        await Assert.That(() => new BundledAssetProvider(null!, CreateReader(resources)))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_EmptyRootPath_ThrowsArgumentException()
    {
        var resources = CreateTestResources();
        await Assert.That(() => new BundledAssetProvider(string.Empty, CreateReader(resources)))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Constructor_NullReader_ThrowsArgumentNullException()
    {
        await Assert.That(() => new BundledAssetProvider("root", null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_NullResourceNames_CreatesEmptyList()
    {
        var resources = CreateTestResources();
        var provider = new BundledAssetProvider("root", CreateReader(resources), null);
        await Assert.That(provider.ResourceNames.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Constructor_EmptyResourceNames_CreatesEmptyList()
    {
        var resources = CreateTestResources();
        var provider = new BundledAssetProvider("root", CreateReader(resources), Array.Empty<string>());
        await Assert.That(provider.ResourceNames.Count).IsEqualTo(0);
    }

    // ========== ReadAsync 测试 ==========

    [Test]
    public async Task ReadAsync_EmptyPath_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = await provider.ReadAsync(string.Empty);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAsync_NullPath_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = await provider.ReadAsync(null!);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAsync_MissingResource_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = await provider.ReadAsync("nonexistent.resource.txt");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAsync_ExactMatch_ReturnsContent()
    {
        var provider = CreateProvider();
        var result = await provider.ReadAsync("index.html");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("<html><body>Index</body></html>");
    }

    [Test]
    public async Task ReadAsync_ResourceWithLeadingSlash_NormalizesAndReads()
    {
        var provider = CreateProvider();
        var result = await provider.ReadAsync("/index.html");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("<html><body>Index</body></html>");
    }

    [Test]
    public async Task ReadAsync_PathWithBackslash_NormalizesToDot()
    {
        // 路径中的 \ 应被替换为 .，并尝试读取对应资源
        var provider = CreateProvider();
        var result = await provider.ReadAsync("scripts\\app.js");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("console.log('app');");
    }

    [Test]
    public async Task ReadAsync_PathWithForwardSlash_NormalizesToDot()
    {
        var provider = CreateProvider();
        var result = await provider.ReadAsync("scripts/app.js");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("console.log('app');");
    }

    [Test]
    public async Task ReadAsync_WithCancellationToken_DoesNotThrow()
    {
        var provider = CreateProvider();
        using var cts = new CancellationTokenSource();
        var result = await provider.ReadAsync("nonexistent.txt", cts.Token);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAsync_PrefixMatch_ReturnsContent()
    {
        // 不带根路径前缀的资源名应通过前缀匹配找到
        var resources = new Dictionary<string, byte[]>
        {
            ["MyApp.assets.config.json"] = Encoding.UTF8.GetBytes("{\"name\":\"app\"}")
        };
        var provider = new BundledAssetProvider(
            "MyApp",
            name => resources.TryGetValue(name, out var b) ? b : null,
            resources.Keys);

        var result = await provider.ReadAsync("assets/config.json");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("{\"name\":\"app\"}");
    }

    [Test]
    public async Task ReadAsync_WildcardMatch_ReturnsFirstMatch()
    {
        // 通配符匹配：* 转为 .*
        var resources = new Dictionary<string, byte[]>
        {
            ["assets.images.logo.png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            ["assets.images.icon.png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x01 }
        };
        var provider = new BundledAssetProvider(
            "MyApp",
            name => resources.TryGetValue(name, out var b) ? b : null,
            resources.Keys);

        var result = await provider.ReadAsync("assets/images/*.png");

        await Assert.That(result).IsNotNull();
    }

    // ========== GetLastModified 测试 ==========

    [Test]
    public async Task GetLastModified_AlwaysReturnsNull()
    {
        // 嵌入式资源无修改时间，始终返回 null
        var provider = CreateProvider();
        var result = provider.GetLastModified("any/path.txt");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetLastModified_EmptyPath_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.GetLastModified(string.Empty);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetLastModified_ExistingResource_ReturnsNull()
    {
        var provider = CreateProvider();
        var result = provider.GetLastModified("index.html");

        await Assert.That(result).IsNull();
    }

    // ========== IAssetProvider 接口契约测试 ==========

    [Test]
    public async Task BundledAssetProvider_ImplementsIAssetProvider()
    {
        IAssetProvider provider = CreateProvider();
        await Assert.That(provider).IsAssignableTo<IAssetProvider>();
    }
}
