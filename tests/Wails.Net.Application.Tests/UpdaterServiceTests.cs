using System.Net;
using System.Text;
using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Tests;

/// <summary>
/// UpdaterService 的单元测试（TUnit）。
/// 测试更新检查、版本比较、下载、安装验证、生命周期方法。
/// </summary>
[NotInParallel]
public sealed class UpdaterServiceTests
{
    /// <summary>
    /// 模拟 HTTP 消息处理器，返回预设的响应内容。
    /// </summary>
    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly byte[] _responseBytes;
        private readonly HttpStatusCode _statusCode;

        public MockHttpHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBytes = Encoding.UTF8.GetBytes(responseContent);
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new ByteArrayContent(_responseBytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }

    [Test]
    public async Task CheckForUpdates_NewerVersion_ReturnsUpdateAvailable()
    {
        // 安排
        var json = """{"version":"2.0.0","downloadUrl":"http://example.com/update","releaseNotes":"New features"}""";
        var handler = new MockHttpHandler(json);
        var service = new UpdaterService(new HttpClient(handler))
        {
            CurrentVersion = "1.0.0"
        };

        // 操作
        var info = await service.CheckForUpdates("http://example.com/check");

        // 断言
        await Assert.That(info.Version).IsEqualTo("2.0.0");
        await Assert.That(info.UpdateAvailable).IsTrue();
        await Assert.That(info.DownloadUrl).IsEqualTo("http://example.com/update");
        await Assert.That(info.ReleaseNotes).IsEqualTo("New features");
    }

    [Test]
    public async Task CheckForUpdates_OlderVersion_ReturnsNoUpdate()
    {
        // 安排
        var json = """{"version":"0.9.0"}""";
        var handler = new MockHttpHandler(json);
        var service = new UpdaterService(new HttpClient(handler))
        {
            CurrentVersion = "1.0.0"
        };

        // 操作
        var info = await service.CheckForUpdates("http://example.com/check");

        // 断言
        await Assert.That(info.Version).IsEqualTo("0.9.0");
        await Assert.That(info.UpdateAvailable).IsFalse();
    }

    [Test]
    public async Task CheckForUpdates_SameVersion_ReturnsNoUpdate()
    {
        // 安排
        var json = """{"version":"1.0.0"}""";
        var handler = new MockHttpHandler(json);
        var service = new UpdaterService(new HttpClient(handler))
        {
            CurrentVersion = "1.0.0"
        };

        // 操作
        var info = await service.CheckForUpdates("http://example.com/check");

        // 断言
        await Assert.That(info.UpdateAvailable).IsFalse();
    }

    [Test]
    public async Task CheckForUpdates_MissingVersionField_DefaultsToZero()
    {
        // 安排
        var json = """{"downloadUrl":"http://example.com"}""";
        var handler = new MockHttpHandler(json);
        var service = new UpdaterService(new HttpClient(handler))
        {
            CurrentVersion = "1.0.0"
        };

        // 操作
        var info = await service.CheckForUpdates("http://example.com/check");

        // 断言
        await Assert.That(info.Version).IsEqualTo("0.0.0");
        await Assert.That(info.UpdateAvailable).IsFalse();
    }

    [Test]
    public async Task DownloadUpdate_SavesFileToDisk()
    {
        // 安排
        var content = "update-package-content";
        var handler = new MockHttpHandler(content);
        var downloadDir = Path.Combine(Path.GetTempPath(), $"updater_test_{Guid.NewGuid():N}");
        var service = new UpdaterService(new HttpClient(handler))
        {
            DownloadDirectory = downloadDir
        };

        try
        {
            // 操作
            var filePath = await service.DownloadUpdate("http://example.com/update.zip");

            // 断言
            await Assert.That(File.Exists(filePath)).IsTrue();
            var fileContent = await File.ReadAllTextAsync(filePath);
            await Assert.That(fileContent).IsEqualTo(content);
        }
        finally
        {
            if (Directory.Exists(downloadDir))
            {
                Directory.Delete(downloadDir, recursive: true);
            }
        }
    }

    [Test]
    public async Task InstallUpdate_NonExistentFile_ThrowsFileNotFoundException()
    {
        // 安排
        var service = new UpdaterService();

        // 操作与断言
        await Assert.That(() => service.InstallUpdate("nonexistent_update.zip"))
            .ThrowsExactly<FileNotFoundException>();
    }

    [Test]
    public async Task InstallUpdate_ExistingFile_DoesNotThrow()
    {
        // 安排：创建一个有效的（空）ZIP 文件，使解压不抛出异常
        var filePath = Path.Combine(Path.GetTempPath(), $"update_test_{Guid.NewGuid():N}.zip");
        try
        {
            // ZIP 空文件的中央目录结尾记录（22 字节）
            var emptyZip = new byte[]
            {
                0x50, 0x4B, 0x05, 0x06, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };
            await File.WriteAllBytesAsync(filePath, emptyZip);
            var service = new UpdaterService();

            // 操作与断言
            await Assert.That(() => service.InstallUpdate(filePath)).ThrowsNothing();
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Test]
    public async Task ServiceStartup_SetsCurrentVersionFromOptions()
    {
        // 安排
        var service = new UpdaterService();
        var options = new ApplicationOptions { Version = "3.5.2" };

        // 操作
        await service.ServiceStartup(options, CancellationToken.None);

        // 断言
        await Assert.That(service.CurrentVersion).IsEqualTo("3.5.2");
    }

    [Test]
    public async Task ServiceShutdown_DoesNotThrow()
    {
        // 安排
        var service = new UpdaterService();

        // 操作与断言
        await Assert.That(() => service.ServiceShutdown(CancellationToken.None)).ThrowsNothing();
    }
}
