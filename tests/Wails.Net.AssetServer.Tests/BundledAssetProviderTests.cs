using System.Reflection;
using System.Text;
using TUnit.Core;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// BundledAssetProvider 的单元测试（TUnit）。
/// 覆盖嵌入资源读取、多程序集合并查找、AddAssembly 方法及 IAssetProvider 接口契约。
/// 对应 M7 新增的组合模式 Provider。
/// </summary>
[NotInParallel]
public sealed class BundledAssetProviderTests
{
    /// <summary>
    /// 获取包含本测试程序集嵌入资源的程序集（用于测试）。
    /// </summary>
    private static Assembly TestAssembly => typeof(BundledAssetProviderTests).Assembly;

    // ========== 构造函数与属性测试 ==========

    [Test]
    public async Task Constructor_SingleAssembly_RegistersAssembly()
    {
        var provider = new BundledAssetProvider(TestAssembly);
        await Assert.That(provider.Assemblies.Count).IsEqualTo(1);
        await Assert.That(provider.Assemblies[0]).IsEqualTo(TestAssembly);
    }

    [Test]
    public async Task Constructor_MultipleAssemblies_RegistersAll()
    {
        var assemblies = new List<Assembly> { TestAssembly, typeof(object).Assembly };
        var provider = new BundledAssetProvider(assemblies);
        await Assert.That(provider.Assemblies.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Constructor_NullAssembly_ThrowsArgumentNullException()
    {
        await Assert.That(() => new BundledAssetProvider((Assembly)null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_NullAssemblies_ThrowsArgumentNullException()
    {
        await Assert.That(() => new BundledAssetProvider((IEnumerable<Assembly>)null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_EmptyAssemblies_CreatesEmptyList()
    {
        var provider = new BundledAssetProvider(Array.Empty<Assembly>());
        await Assert.That(provider.Assemblies.Count).IsEqualTo(0);
    }

    // ========== AddAssembly 测试 ==========

    [Test]
    public async Task AddAssembly_NewAssembly_AddsToList()
    {
        var provider = new BundledAssetProvider(TestAssembly);
        provider.AddAssembly(typeof(object).Assembly);

        await Assert.That(provider.Assemblies.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AddAssembly_DuplicateAssembly_DoesNotAddAgain()
    {
        var provider = new BundledAssetProvider(TestAssembly);
        provider.AddAssembly(TestAssembly); // 重复添加

        await Assert.That(provider.Assemblies.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AddAssembly_NullAssembly_ThrowsArgumentNullException()
    {
        var provider = new BundledAssetProvider(TestAssembly);
        await Assert.That(() => provider.AddAssembly(null!)).ThrowsExactly<ArgumentNullException>();
    }

    // ========== ReadAsync 测试 ==========

    [Test]
    public async Task ReadAsync_EmptyPath_ReturnsNull()
    {
        var provider = new BundledAssetProvider(TestAssembly);
        var result = await provider.ReadAsync(string.Empty);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAsync_NullPath_ReturnsNull()
    {
        var provider = new BundledAssetProvider(TestAssembly);
        var result = await provider.ReadAsync(null!);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAsync_MissingResource_ReturnsNull()
    {
        var provider = new BundledAssetProvider(TestAssembly);
        var result = await provider.ReadAsync("nonexistent.resource.txt");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAsync_ResourceWithLeadingSlash_DoesNotThrow()
    {
        // 即使找不到资源，也应规范化路径不抛异常
        var provider = new BundledAssetProvider(TestAssembly);
        var result = await provider.ReadAsync("/nonexistent.txt");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAsync_PathWithBackslash_NormalizesToDot()
    {
        // 路径中的 \ 应被替换为 .，且不抛异常
        var provider = new BundledAssetProvider(TestAssembly);
        var result = await provider.ReadAsync("folder\\file.txt");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAsync_PathWithForwardSlash_NormalizesToDot()
    {
        // 路径中的 / 应被替换为 .，且不抛异常
        var provider = new BundledAssetProvider(TestAssembly);
        var result = await provider.ReadAsync("folder/file.txt");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAsync_WithCancellationToken_DoesNotThrow()
    {
        var provider = new BundledAssetProvider(TestAssembly);
        using var cts = new CancellationTokenSource();
        var result = await provider.ReadAsync("nonexistent.txt", cts.Token);

        await Assert.That(result).IsNull();
    }

    // ========== GetLastModified 测试 ==========

    [Test]
    public async Task GetLastModified_AlwaysReturnsNull()
    {
        // 嵌入式资源无修改时间，始终返回 null
        var provider = new BundledAssetProvider(TestAssembly);
        var result = provider.GetLastModified("any/path.txt");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetLastModified_EmptyPath_ReturnsNull()
    {
        var provider = new BundledAssetProvider(TestAssembly);
        var result = provider.GetLastModified(string.Empty);

        await Assert.That(result).IsNull();
    }

    // ========== IAssetProvider 接口契约测试 ==========

    [Test]
    public async Task BundledAssetProvider_ImplementsIAssetProvider()
    {
        IAssetProvider provider = new BundledAssetProvider(TestAssembly);
        await Assert.That(provider).IsAssignableTo<IAssetProvider>();
    }

    // ========== 多程序集查找测试 ==========

    [Test]
    public async Task ReadAsync_MultipleAssemblies_SearchesAll()
    {
        var assemblies = new List<Assembly> { TestAssembly, typeof(object).Assembly };
        var provider = new BundledAssetProvider(assemblies);

        // 在两个程序集中都找不到的资源应返回 null
        var result = await provider.ReadAsync("definitely.nonexistent.resource.xyz");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task AddAssembly_AfterConstruction_SearchesNewAssembly()
    {
        var provider = new BundledAssetProvider(TestAssembly);
        provider.AddAssembly(typeof(object).Assembly);

        // 添加新程序集后，Assemblies 列表应包含两个
        await Assert.That(provider.Assemblies.Count).IsEqualTo(2);
    }
}
