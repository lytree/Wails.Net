using System.Text;
using TUnit.Core;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// BundledAssetServer 的单元测试（TUnit）。
/// 覆盖委托式资源读取、路径规范化及与 AssetServer 基类的组合行为。
/// 对应 Wails v3 Go 版本 bundledassets 测试。
/// <para>
/// 遵循 AGENTS.md §3.4：不使用 System.Reflection.Assembly，资源由测试用字典 + 委托提供。
/// </para>
/// </summary>
[NotInParallel]
public sealed class BundledAssetServerTests
{
    /// <summary>
    /// 默认测试根路径。
    /// </summary>
    private const string DefaultRootPath = "Wails.Net.AssetServer.Tests";

    /// <summary>
    /// 创建测试用资源字典。
    /// </summary>
    private static Dictionary<string, byte[]> CreateTestResources() => new(StringComparer.Ordinal)
    {
        [$"{DefaultRootPath}.index.html"] = Encoding.UTF8.GetBytes("<html><body>Index</body></html>"),
        [$"{DefaultRootPath}.styles.css"] = Encoding.UTF8.GetBytes("body { color: red; }"),
        [$"{DefaultRootPath}.scripts.app.js"] = Encoding.UTF8.GetBytes("console.log('app');")
    };

    /// <summary>
    /// 从字典创建资源读取委托。
    /// </summary>
    private static Func<string, byte[]?> CreateReader(Dictionary<string, byte[]> resources) =>
        name => resources.TryGetValue(name, out var bytes) ? bytes : null;

    /// <summary>
    /// 使用默认资源字典创建 BundledAssetServer 实例。
    /// </summary>
    private static BundledAssetServer CreateServer(
        Dictionary<string, byte[]>? resources = null,
        string rootPath = DefaultRootPath)
    {
        resources ??= CreateTestResources();
        return new BundledAssetServer(rootPath, CreateReader(resources), resources.Keys);
    }

    // ========== 构造函数与属性测试 ==========

    [Test]
    public async Task Constructor_ValidParameters_SetsHandlerToBundled()
    {
        var server = CreateServer();
        await Assert.That(server.Options.Handler).IsEqualTo("bundled");
    }

    [Test]
    public async Task Constructor_ValidParameters_SetsRootPath()
    {
        var server = CreateServer();
        await Assert.That(server.RootPath).IsEqualTo(DefaultRootPath);
    }

    [Test]
    public async Task Constructor_ValidParameters_RegistersResourceNames()
    {
        var server = CreateServer();
        await Assert.That(server.ResourceNames.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Constructor_NullRootPath_ThrowsArgumentNullException()
    {
        var resources = CreateTestResources();
        await Assert.That(() => new BundledAssetServer(null!, CreateReader(resources), resources.Keys))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_EmptyRootPath_ThrowsArgumentException()
    {
        var resources = CreateTestResources();
        await Assert.That(() => new BundledAssetServer(string.Empty, CreateReader(resources), resources.Keys))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task Constructor_NullReader_ThrowsArgumentNullException()
    {
        await Assert.That(() => new BundledAssetServer(DefaultRootPath, null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_NullResourceNames_AllowsWildcardDisabled()
    {
        var resources = CreateTestResources();
        var server = new BundledAssetServer(DefaultRootPath, CreateReader(resources), null);
        await Assert.That(server.ResourceNames.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Constructor_WithSpaFallback_SetsOptions()
    {
        var server = CreateServer();
        var spaServer = new BundledAssetServer(
            DefaultRootPath,
            CreateReader(CreateTestResources()),
            CreateTestResources().Keys,
            enableSpaFallback: true,
            defaultDocument: "index.html");

        await Assert.That(spaServer.Options.EnableSpaFallback).IsTrue();
        await Assert.That(spaServer.Options.DefaultDocument).IsEqualTo("index.html");
    }

    // ========== ReadResource 测试 ==========

    [Test]
    public async Task ReadResource_EmptyPath_ReturnsNull()
    {
        var server = CreateServer();
        var result = server.ReadResource("");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadResource_NullPath_ReturnsNull()
    {
        var server = CreateServer();
        var result = server.ReadResource(null!);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadResource_MissingResource_ReturnsNull()
    {
        var server = CreateServer();
        var result = server.ReadResource("nonexistent.resource.txt");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadResource_ExistingResource_ReturnsContent()
    {
        var server = CreateServer();
        var result = server.ReadResource("index.html");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("<html><body>Index</body></html>");
    }

    [Test]
    public async Task ReadResource_ResourceWithLeadingSlash_NormalizesAndReads()
    {
        var server = CreateServer();
        var result = server.ReadResource("/index.html");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("<html><body>Index</body></html>");
    }

    // ========== ServeAsync 测试 ==========

    [Test]
    public async Task ServeAsync_MissingEmbeddedResource_ReturnsEmptyArray()
    {
        var server = CreateServer();
        var result = await server.ServeAsync("/nonexistent.html");

        await Assert.That(result).IsEqualTo(Array.Empty<byte>());
    }

    [Test]
    public async Task ServeAsync_EmptyPath_ThrowsArgumentException()
    {
        var server = CreateServer();
        await Assert.That(async () => await server.ServeAsync("")).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task ServeAsync_ExistingResource_ReturnsContent()
    {
        var server = CreateServer();
        var result = await server.ServeAsync("index.html");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Length).IsGreaterThan(0);
    }

    // ========== ReadResource 名称规范化测试 ==========

    [Test]
    public async Task ReadResource_PathWithBackslash_NormalizesToDot()
    {
        // 路径中的 \ 应被替换为 .，并能找到对应资源
        var server = CreateServer();
        var result = server.ReadResource("scripts\\app.js");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("console.log('app');");
    }

    [Test]
    public async Task ReadResource_PathWithForwardSlash_NormalizesToDot()
    {
        var server = CreateServer();
        var result = server.ReadResource("scripts/app.js");

        await Assert.That(result).IsNotNull();
        await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("console.log('app');");
    }
}
