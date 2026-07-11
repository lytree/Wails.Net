using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Tests;

/// <summary>
/// FileServerService 的单元测试（TUnit）。
/// 测试文件读写、路径穿越防护、生命周期方法。
/// </summary>
[NotInParallel]
public sealed class FileServerServiceTests
{
    /// <summary>
    /// 创建临时根目录并启动服务。
    /// </summary>
    /// <returns>临时根目录路径。</returns>
    private static async Task<string> SetupRootAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wails_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var service = new FileServerService(root);
        await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
        return root;
    }

    [Test]
    public async Task WriteFile_CreatesFileAndWritesContent()
    {
        // 安排
        var root = await SetupRootAsync();
        try
        {
            var service = new FileServerService(root);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

            // 操作
            service.WriteFile("test.txt", "hello world");

            // 断言
            var fullPath = Path.Combine(root, "test.txt");
            await Assert.That(File.Exists(fullPath)).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(fullPath)).IsEqualTo("hello world");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ReadFile_ReturnsFileContent()
    {
        // 安排
        var root = await SetupRootAsync();
        try
        {
            var service = new FileServerService(root);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
            service.WriteFile("read.txt", "content to read");

            // 操作
            var content = service.ReadFile("read.txt");

            // 断言
            await Assert.That(content).IsEqualTo("content to read");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ReadFile_NotExists_ThrowsFileNotFoundException()
    {
        // 安排
        var root = await SetupRootAsync();
        try
        {
            var service = new FileServerService(root);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

            // 操作与断言
            await Assert.That(() => service.ReadFile("nonexistent.txt"))
                .ThrowsExactly<FileNotFoundException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FileExists_ReturnsTrueForExistingFile()
    {
        // 安排
        var root = await SetupRootAsync();
        try
        {
            var service = new FileServerService(root);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
            service.WriteFile("exists.txt", "data");

            // 操作
            var exists = service.FileExists("exists.txt");

            // 断言
            await Assert.That(exists).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task FileExists_ReturnsFalseForMissingFile()
    {
        // 安排
        var root = await SetupRootAsync();
        try
        {
            var service = new FileServerService(root);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

            // 操作
            var exists = service.FileExists("missing.txt");

            // 断言
            await Assert.That(exists).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DeleteFile_RemovesExistingFile()
    {
        // 安排
        var root = await SetupRootAsync();
        try
        {
            var service = new FileServerService(root);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
            service.WriteFile("delete.txt", "to delete");

            // 操作
            service.DeleteFile("delete.txt");

            // 断言
            await Assert.That(service.FileExists("delete.txt")).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DeleteFile_NonExisting_DoesNotThrow()
    {
        // 安排
        var root = await SetupRootAsync();
        try
        {
            var service = new FileServerService(root);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

            // 操作与断言
            await Assert.That(() => service.DeleteFile("nonexistent.txt")).ThrowsNothing();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task WriteFile_PathTraversal_ThrowsUnauthorizedAccess()
    {
        // 安排
        var root = await SetupRootAsync();
        try
        {
            var service = new FileServerService(root);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

            // 操作与断言
            await Assert.That(() => service.WriteFile("../../../outside.txt", "hacked"))
                .ThrowsExactly<UnauthorizedAccessException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ReadFile_PathTraversal_ThrowsUnauthorizedAccess()
    {
        // 安排
        var root = await SetupRootAsync();
        try
        {
            var service = new FileServerService(root);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

            // 操作与断言
            await Assert.That(() => service.ReadFile("../../../outside.txt"))
                .ThrowsExactly<UnauthorizedAccessException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task WriteFile_CreatesSubdirectories()
    {
        // 安排
        var root = await SetupRootAsync();
        try
        {
            var service = new FileServerService(root);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

            // 操作
            service.WriteFile("sub/dir/file.txt", "nested");

            // 断言
            await Assert.That(service.FileExists("sub/dir/file.txt")).IsTrue();
            await Assert.That(service.ReadFile("sub/dir/file.txt")).IsEqualTo("nested");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ServiceStartup_CreatesRootDirectoryIfNotExists()
    {
        // 安排
        var root = Path.Combine(Path.GetTempPath(), $"wails_test_{Guid.NewGuid():N}", "deep");
        try
        {
            var service = new FileServerService(root);

            // 操作
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

            // 断言
            await Assert.That(Directory.Exists(root)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(Path.GetDirectoryName(root)!, recursive: true);
            }
        }
    }

    [Test]
    public async Task ServiceShutdown_DoesNotThrow()
    {
        // 安排
        var root = await SetupRootAsync();
        try
        {
            var service = new FileServerService(root);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

            // 操作与断言
            await Assert.That(() => service.ServiceShutdown(CancellationToken.None)).ThrowsNothing();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
