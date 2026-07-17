using System.Text;
using TUnit.Core;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// FileAssetProvider 的单元测试（TUnit）。
/// 覆盖文件读取、路径穿越防护、Last-Modified 查询及 IAssetProvider 接口契约。
/// 对应 M7 新增的组合模式 Provider。
/// </summary>
[NotInParallel]
public sealed class FileAssetProviderTests
{
    /// <summary>
    /// 创建临时根目录，并在其中放置指定文件。
    /// </summary>
    /// <param name="relativePath">文件相对路径。</param>
    /// <param name="content">文件内容。</param>
    /// <returns>临时根目录路径。</returns>
    private static string CreateTempRootWithFile(string relativePath, string content)
    {
        var root = Path.Combine(Path.GetTempPath(), $"wails_provider_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return root;
    }

    /// <summary>
    /// 删除临时目录（如果存在）。
    /// </summary>
    private static void Cleanup(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    // ========== 构造函数测试 ==========

    [Test]
    public async Task Constructor_NullRootPath_ThrowsArgumentNullException()
    {
        await Assert.That(() => new FileAssetProvider(null!))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_EmptyRootPath_ThrowsArgumentException()
    {
        await Assert.That(() => new FileAssetProvider(string.Empty))
            .ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task RootPath_ReturnsConstructorValue()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var provider = new FileAssetProvider(root);
            await Assert.That(provider.RootPath).IsEqualTo(root);
        }
        finally
        {
            Cleanup(root);
        }
    }

    // ========== ReadAsync 测试 ==========

    [Test]
    public async Task ReadAsync_ExistingFile_ReturnsContent()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var provider = new FileAssetProvider(root);
            var result = await provider.ReadAsync("index.html");

            await Assert.That(result).IsNotNull();
            await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("<html></html>");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ReadAsync_NestedFile_ReturnsContent()
    {
        var root = CreateTempRootWithFile("assets/app.js", "console.log('hello');");
        try
        {
            var provider = new FileAssetProvider(root);
            var result = await provider.ReadAsync("assets/app.js");

            await Assert.That(result).IsNotNull();
            await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("console.log('hello');");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ReadAsync_LeadingSlash_ReturnsContent()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var provider = new FileAssetProvider(root);
            var result = await provider.ReadAsync("/index.html");

            await Assert.That(result).IsNotNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ReadAsync_NonExistentFile_ReturnsNull()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var provider = new FileAssetProvider(root);
            var result = await provider.ReadAsync("nonexistent.html");

            await Assert.That(result).IsNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ReadAsync_EmptyPath_ReturnsNull()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var provider = new FileAssetProvider(root);
            var result = await provider.ReadAsync(string.Empty);

            await Assert.That(result).IsNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ReadAsync_NullPath_ReturnsNull()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var provider = new FileAssetProvider(root);
            var result = await provider.ReadAsync(null!);

            await Assert.That(result).IsNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    // ========== 路径穿越防护测试 ==========

    [Test]
    public async Task ReadAsync_PathTraversal_ReturnsNull()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var provider = new FileAssetProvider(root);
            var result = await provider.ReadAsync("../../../etc/passwd");

            await Assert.That(result).IsNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ReadAsync_PathTraversalWithBackslash_ReturnsNull()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var provider = new FileAssetProvider(root);
            var result = await provider.ReadAsync("..\\..\\..\\windows\\system32\\config\\sam");

            await Assert.That(result).IsNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    // ========== GetLastModified 测试 ==========

    [Test]
    public async Task GetLastModified_ExistingFile_ReturnsUtcTime()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var provider = new FileAssetProvider(root);
            var result = provider.GetLastModified("index.html");

            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task GetLastModified_NonExistentFile_ReturnsNull()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var provider = new FileAssetProvider(root);
            var result = provider.GetLastModified("nonexistent.html");

            await Assert.That(result).IsNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task GetLastModified_EmptyPath_ReturnsNull()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var provider = new FileAssetProvider(root);
            var result = provider.GetLastModified(string.Empty);

            await Assert.That(result).IsNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    // ========== IAssetProvider 接口契约测试 ==========

    [Test]
    public async Task FileAssetProvider_ImplementsIAssetProvider()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            IAssetProvider provider = new FileAssetProvider(root);
            await Assert.That(provider).IsAssignableTo<IAssetProvider>();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ReadAsync_WithCancellationToken_ReturnsContent()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var provider = new FileAssetProvider(root);
            using var cts = new CancellationTokenSource();
            var result = await provider.ReadAsync("index.html", cts.Token);

            await Assert.That(result).IsNotNull();
        }
        finally
        {
            Cleanup(root);
        }
    }
}
