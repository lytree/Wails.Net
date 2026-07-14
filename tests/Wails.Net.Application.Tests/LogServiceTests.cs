using Microsoft.Extensions.Logging;
using TUnit.Core;
using Wails.Net.Application.Options;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Tests;

/// <summary>
/// LogService 的单元测试（TUnit）。
/// 测试日志写入、级别过滤、文件输出、生命周期方法。
/// </summary>
[NotInParallel]
public sealed class LogServiceTests
{
    [Test]
    public async Task Info_WritesLogEntry()
    {
        // 安排
        var service = new LogService();

        // 操作
        service.Info("test info message");

        // 断言
        var entries = service.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Information);
        await Assert.That(entries[0].Message).IsEqualTo("test info message");
    }

    [Test]
    public async Task Debug_WritesLogEntry()
    {
        // 安排
        var service = new LogService();

        // 操作
        service.Debug("debug msg");

        // 断言
        var entries = service.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Debug);
    }

    [Test]
    public async Task Warning_WritesLogEntry()
    {
        // 安排
        var service = new LogService();

        // 操作
        service.Warning("warning msg");

        // 断言
        var entries = service.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Warning);
    }

    [Test]
    public async Task Error_WritesLogEntry()
    {
        // 安排
        var service = new LogService();

        // 操作
        service.Error("error msg");

        // 断言
        var entries = service.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Error);
    }

    [Test]
    public async Task Fatal_WritesLogEntry()
    {
        // 安排
        var service = new LogService();

        // 操作
        service.Fatal("fatal msg");

        // 断言
        var entries = service.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Critical);
    }

    [Test]
    public async Task Log_StringLevel_WritesCorrectEntry()
    {
        // 安排
        var service = new LogService();

        // 操作
        service.Log("Warning", "via string level");

        // 断言
        var entries = service.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(entries[0].Message).IsEqualTo("via string level");
    }

    [Test]
    public async Task Log_InvalidLevel_DoesNotWriteEntry()
    {
        // 安排
        var service = new LogService();

        // 操作
        service.Log("InvalidLevel", "should be ignored");

        // 断言
        var entries = service.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Log_BelowMinimumLevel_IsFiltered()
    {
        // 安排
        var service = new LogService { MinimumLevel = LogLevel.Warning };

        // 操作
        service.Debug("filtered debug");
        service.Info("filtered info");
        service.Warning("kept warning");

        // 断言
        var entries = service.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(entries[0].Message).IsEqualTo("kept warning");
    }

    [Test]
    public async Task Log_AtMinimumLevel_IsNotFiltered()
    {
        // 安排
        var service = new LogService { MinimumLevel = LogLevel.Error };

        // 操作
        service.Error("at minimum level");

        // 断言
        var entries = service.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Error);
    }

    [Test]
    public async Task Log_MultipleEntries_AllRecorded()
    {
        // 安排
        var service = new LogService();

        // 操作
        service.Debug("first");
        service.Info("second");
        service.Error("third");

        // 断言
        var entries = service.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(3);
        await Assert.That(entries[0].Message).IsEqualTo("first");
        await Assert.That(entries[1].Message).IsEqualTo("second");
        await Assert.That(entries[2].Message).IsEqualTo("third");
    }

    [Test]
    public async Task ServiceStartup_OpensLogFile()
    {
        // 安排
        var logFile = Path.Combine(Path.GetTempPath(), $"log_test_{Guid.NewGuid():N}.log");
        try
        {
            var service = new LogService(logFile);

            // 操作
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);
            service.Info("file log test");
            await service.ServiceShutdown(CancellationToken.None);

            // 断言
            await Assert.That(File.Exists(logFile)).IsTrue();
            var content = await File.ReadAllTextAsync(logFile);
            await Assert.That(content).Contains("file log test");
            await Assert.That(content).Contains("Info");
        }
        finally
        {
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
        }
    }

    [Test]
    public async Task ServiceShutdown_ClosesLogFile()
    {
        // 安排
        var logFile = Path.Combine(Path.GetTempPath(), $"log_test_{Guid.NewGuid():N}.log");
        try
        {
            var service = new LogService(logFile);
            await service.ServiceStartup(new ApplicationOptions(), CancellationToken.None);

            // 操作
            await service.ServiceShutdown(CancellationToken.None);

            // 断言：关闭后再写入日志不应抛出异常（写入器已关闭但内部有 null 检查）
            await Assert.That(() => service.Info("after shutdown")).ThrowsNothing();
        }
        finally
        {
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
        }
    }

    [Test]
    public async Task ServiceStartup_NoLogFile_DoesNotThrow()
    {
        // 安排
        var service = new LogService();

        // 操作与断言
        await Assert.That(() => service.ServiceStartup(new ApplicationOptions(), CancellationToken.None))
            .ThrowsNothing();
    }
}
