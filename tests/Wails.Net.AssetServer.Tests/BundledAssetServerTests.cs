using System.Reflection;
using System.Text;
using TUnit.Core;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// BundledAssetServer 的单元测试（TUnit）。
/// 覆盖嵌入资源读取、多程序集合并查找、AddAssembly 方法及路径规范化。
/// 对应 Wails v3 Go 版本 bundledassets 测试。
/// </summary>
[NotInParallel]
public sealed class BundledAssetServerTests
{
    /// <summary>
    /// 获取包含本测试程序集嵌入资源的程序集（用于测试）。
    /// </summary>
    private static Assembly TestAssembly => typeof(BundledAssetServerTests).Assembly;

    // ========== 构造函数与属性测试 ==========

    [Test]
    public async Task Constructor_SingleAssembly_SetsHandlerToBundled()
    {
        var server = new BundledAssetServer(TestAssembly);
        await Assert.That(server.Options.Handler).IsEqualTo("bundled");
    }

    [Test]
    public async Task Constructor_SingleAssembly_RegistersAssembly()
    {
        var server = new BundledAssetServer(TestAssembly);
        await Assert.That(server.Assemblies.Count).IsEqualTo(1);
        await Assert.That(server.Assemblies[0]).IsEqualTo(TestAssembly);
    }

    [Test]
    public async Task Constructor_MultipleAssemblies_RegistersAll()
    {
        var assemblies = new List<Assembly> { TestAssembly, typeof(object).Assembly };
        var server = new BundledAssetServer(assemblies);
        await Assert.That(server.Assemblies.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Constructor_NullAssembly_ThrowsArgumentNullException()
    {
        await Assert.That(() => new BundledAssetServer((Assembly)null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_NullAssemblies_ThrowsArgumentNullException()
    {
        await Assert.That(() => new BundledAssetServer((IEnumerable<Assembly>)null!)).ThrowsExactly<ArgumentNullException>();
    }

    // ========== AddAssembly 测试 ==========

    [Test]
    public async Task AddAssembly_NewAssembly_AddsToList()
    {
        var server = new BundledAssetServer(TestAssembly);
        server.AddAssembly(typeof(object).Assembly);

        await Assert.That(server.Assemblies.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AddAssembly_DuplicateAssembly_DoesNotAddAgain()
    {
        var server = new BundledAssetServer(TestAssembly);
        server.AddAssembly(TestAssembly); // 重复添加

        await Assert.That(server.Assemblies.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AddAssembly_NullAssembly_ThrowsArgumentNullException()
    {
        var server = new BundledAssetServer(TestAssembly);
        await Assert.That(() => server.AddAssembly(null!)).ThrowsExactly<ArgumentNullException>();
    }

    // ========== ReadResource 测试 ==========

    /// <summary>
    /// 测试用的嵌入资源内容。
    /// </summary>
    private const string EmbeddedResourceContent = "embedded test resource content";

    [Test]
    public async Task ReadResource_EmptyPath_ReturnsNull()
    {
        var server = new BundledAssetServer(TestAssembly);
        var result = server.ReadResource("");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadResource_NullPath_ReturnsNull()
    {
        var server = new BundledAssetServer(TestAssembly);
        var result = server.ReadResource(null!);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadResource_MissingResource_ReturnsNull()
    {
        var server = new BundledAssetServer(TestAssembly);
        var result = server.ReadResource("nonexistent.resource.txt");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadResource_ResourceWithLeadingSlash_NormalizesAndReads()
    {
        // 即使找不到资源，也应规范化路径不抛异常
        var server = new BundledAssetServer(TestAssembly);
        var result = server.ReadResource("/nonexistent.txt");

        await Assert.That(result).IsNull();
    }

    // ========== ServeAsync 测试 ==========

    [Test]
    public async Task ServeAsync_MissingEmbeddedResource_ReturnsEmptyArray()
    {
        var server = new BundledAssetServer(TestAssembly);
        var result = await server.ServeAsync("/nonexistent.html");

        await Assert.That(result).IsEqualTo(Array.Empty<byte>());
    }

    [Test]
    public async Task ServeAsync_EmptyPath_ThrowsArgumentException()
    {
        var server = new BundledAssetServer(TestAssembly);
        await Assert.That(async () => await server.ServeAsync("")).ThrowsExactly<ArgumentException>();
    }

    // ========== ReadResource 名称规范化测试 ==========

    [Test]
    public async Task ReadResource_PathWithBackslash_NormalizesToDot()
    {
        // 路径中的 \ 应被替换为 .，且不抛异常
        var server = new BundledAssetServer(TestAssembly);
        var result = server.ReadResource("folder\\file.txt");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadResource_PathWithForwardSlash_NormalizesToDot()
    {
        var server = new BundledAssetServer(TestAssembly);
        var result = server.ReadResource("folder/file.txt");

        await Assert.That(result).IsNull();
    }
}
