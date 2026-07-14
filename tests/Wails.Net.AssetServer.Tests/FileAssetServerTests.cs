using System.Net;
using System.Text;
using TUnit.Core;

namespace Wails.Net.AssetServer.Tests;

/// <summary>
/// FileAssetServer 的单元测试（TUnit）。
/// 覆盖文件读取、路径穿越防护、Last-Modified 头及 ServeHttpAsync 集成。
/// 对应 Wails v3 Go 版本 fileassets 测试。
/// </summary>
[NotInParallel]
public sealed class FileAssetServerTests
{
    /// <summary>
    /// 创建临时根目录，并在其中放置指定文件。
    /// </summary>
    /// <param name="relativePath">文件相对路径。</param>
    /// <param name="content">文件内容。</param>
    /// <returns>临时根目录路径。</returns>
    private static string CreateTempRootWithFile(string relativePath, string content)
    {
        var root = Path.Combine(Path.GetTempPath(), $"wails_asset_test_{Guid.NewGuid():N}");
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

    // ========== ReadFile 测试 ==========

    [Test]
    public async Task ReadFile_ExistingFile_ReturnsContent()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var server = new FileAssetServer(root);
            var result = server.ReadFile("index.html");

            await Assert.That(result).IsNotNull();
            await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("<html></html>");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ReadFile_MissingFile_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wails_asset_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var server = new FileAssetServer(root);
            var result = server.ReadFile("nonexistent.html");

            await Assert.That(result).IsNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ReadFile_EmptyPath_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wails_asset_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var server = new FileAssetServer(root);
            var result = server.ReadFile("");

            await Assert.That(result).IsNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ReadFile_NestedPath_ReturnsContent()
    {
        var root = CreateTempRootWithFile("assets/js/app.js", "console.log('app');");
        try
        {
            var server = new FileAssetServer(root);
            var result = server.ReadFile("assets/js/app.js");

            await Assert.That(result).IsNotNull();
            await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("console.log('app');");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ReadFile_PathWithLeadingSlash_ResolvesCorrectly()
    {
        var root = CreateTempRootWithFile("style.css", "body { }");
        try
        {
            var server = new FileAssetServer(root);
            var result = server.ReadFile("/style.css");

            await Assert.That(result).IsNotNull();
            await Assert.That(Encoding.UTF8.GetString(result!)).IsEqualTo("body { }");
        }
        finally
        {
            Cleanup(root);
        }
    }

    // ========== 路径穿越防护测试 ==========

    [Test]
    public async Task ReadFile_PathTraversalWithDoubleDots_ReturnsNull()
    {
        // 在 root 之下创建一个文件，但尝试通过 ../ 逃出
        var root = CreateTempRootWithFile("inside.txt", "inside");
        try
        {
            var server = new FileAssetServer(root);
            var result = server.ReadFile("../../../outside.txt");

            await Assert.That(result).IsNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ReadFile_PathTraversalAbsolute_ReturnsNull()
    {
        var root = CreateTempRootWithFile("inside.txt", "inside");
        try
        {
            var server = new FileAssetServer(root);
            // 绝对路径在 Windows 上可能被 Path.Combine 解释为根路径，应被拒绝
            var absolutePath = Path.Combine(Path.GetTempPath(), "absolute_outside.txt");
            File.WriteAllText(absolutePath, "outside");
            try
            {
                var result = server.ReadFile(absolutePath);
                await Assert.That(result).IsNull();
            }
            finally
            {
                File.Delete(absolutePath);
            }
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
        var root = CreateTempRootWithFile("file.txt", "content");
        try
        {
            var server = new FileAssetServer(root);
            var result = server.GetLastModified("file.txt");

            await Assert.That(result).IsNotNull();
            // 验证返回的是 UTC 时间
            await Assert.That(result!.Value.Kind).IsEqualTo(DateTimeKind.Utc);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task GetLastModified_MissingFile_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wails_asset_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var server = new FileAssetServer(root);
            var result = server.GetLastModified("nonexistent.txt");

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
        var root = Path.Combine(Path.GetTempPath(), $"wails_asset_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var server = new FileAssetServer(root);
            var result = server.GetLastModified("");

            await Assert.That(result).IsNull();
        }
        finally
        {
            Cleanup(root);
        }
    }

    // ========== RootPath 属性测试 ==========

    [Test]
    public async Task RootPath_ReturnsConfiguredRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wails_asset_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var server = new FileAssetServer(root);
            await Assert.That(server.RootPath).IsEqualTo(root);
        }
        finally
        {
            Cleanup(root);
        }
    }

    // ========== Options 属性测试 ==========

    [Test]
    public async Task Constructor_SetsHandlerToFile()
    {
        var server = new FileAssetServer(Path.GetTempPath());
        await Assert.That(server.Options.Handler).IsEqualTo("file");
    }

    [Test]
    public async Task Constructor_SetsRootPath()
    {
        var root = Path.GetTempPath();
        var server = new FileAssetServer(root);
        await Assert.That(server.Options.RootPath).IsEqualTo(root);
    }

    // ========== ServeAsync 集成测试 ==========

    [Test]
    public async Task ServeAsync_ExistingFile_ReturnsFileContent()
    {
        var content = "console.log('hello');";
        var root = CreateTempRootWithFile("app.js", content);
        try
        {
            var server = new FileAssetServer(root);
            var result = await server.ServeAsync("/app.js");

            await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo(content);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task ServeAsync_MissingFile_ReturnsEmptyArray()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wails_asset_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var server = new FileAssetServer(root);
            var result = await server.ServeAsync("/missing.html");

            await Assert.That(result).IsEqualTo(Array.Empty<byte>());
        }
        finally
        {
            Cleanup(root);
        }
    }

    // ========== 构造函数参数校验 ==========

    [Test]
    public async Task Constructor_NullRootPath_ThrowsArgumentNullException()
    {
        await Assert.That(() => new FileAssetServer(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_EmptyRootPath_ThrowsArgumentException()
    {
        await Assert.That(() => new FileAssetServer("")).ThrowsExactly<ArgumentException>();
    }

    // ========== SPA 回退构造函数测试 ==========

    [Test]
    public async Task Constructor_WithSpaFallback_SetsEnableSpaFallbackTrue()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var server = new FileAssetServer(root, enableSpaFallback: true, defaultDocument: "index.html");
            await Assert.That(server.Options.EnableSpaFallback).IsTrue();
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task Constructor_WithSpaFallback_SetsDefaultDocument()
    {
        var root = CreateTempRootWithFile("app.html", "<html></html>");
        try
        {
            var server = new FileAssetServer(root, enableSpaFallback: true, defaultDocument: "app.html");
            await Assert.That(server.Options.DefaultDocument).IsEqualTo("app.html");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task Constructor_WithNullDefaultDocument_FallsBackToIndexHtml()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var server = new FileAssetServer(root, enableSpaFallback: true, defaultDocument: null!);
            await Assert.That(server.Options.DefaultDocument).IsEqualTo("index.html");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task Constructor_WithoutSpaFallback_EnableSpaFallbackIsFalse()
    {
        var root = CreateTempRootWithFile("index.html", "<html></html>");
        try
        {
            var server = new FileAssetServer(root);
            await Assert.That(server.Options.EnableSpaFallback).IsFalse();
        }
        finally
        {
            Cleanup(root);
        }
    }
}
