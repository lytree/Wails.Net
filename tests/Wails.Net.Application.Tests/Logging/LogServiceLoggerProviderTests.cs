using Microsoft.Extensions.Logging;
using TUnit.Core;
using Wails.Net.Application.Logging;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Tests.Logging;

/// <summary>
/// <see cref="LogServiceLoggerProvider"/> 的单元测试（P1-3 Direction 1：.NET ILogger → LogService）。
/// </summary>
[NotInParallel]
public sealed class LogServiceLoggerProviderTests
{
    /// <summary>
    /// 构造测试组合：独立的 LogService 与基于其创建的 LoggerProvider/Logger。
    /// </summary>
    private static (LogService logService, LogServiceLoggerProvider provider, ILogger logger) Create(
        string categoryName = "TestCategory",
        LogLevel? minimumLevel = null)
    {
        var logService = new LogService();
        if (minimumLevel is not null)
        {
            logService.MinimumLevel = minimumLevel.Value;
        }
        var provider = new LogServiceLoggerProvider(logService);
        var logger = provider.CreateLogger(categoryName);
        return (logService, provider, logger);
    }

    [Test]
    public async Task Constructor_NullLogService_ThrowsArgumentNullException()
    {
        await Assert.That(() => new LogServiceLoggerProvider(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task CreateLogger_EmptyCategoryName_ThrowsArgumentException()
    {
        var logService = new LogService();
        var provider = new LogServiceLoggerProvider(logService);

        await Assert.That(() => provider.CreateLogger(string.Empty)).ThrowsExactly<ArgumentException>();
    }

    [Test]
    public async Task CreateLogger_NullCategoryName_ThrowsArgumentNullException()
    {
        var logService = new LogService();
        var provider = new LogServiceLoggerProvider(logService);

        await Assert.That(() => provider.CreateLogger(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task Log_WritesMessage_IntoLogService()
    {
        var (logService, _, logger) = Create("TestCategory");

        logger.LogInformation("hello from test");

        var entries = logService.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Information);
        await Assert.That(entries[0].Message.Contains("hello from test")).IsTrue();
        await Assert.That(entries[0].Message.Contains("[TestCategory]")).IsTrue();
    }

    [Test]
    public async Task Log_ErrorLevel_WritesErrorEntry()
    {
        var (logService, _, logger) = Create();

        logger.LogError("boom");

        var entries = logService.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Level).IsEqualTo(LogLevel.Error);
    }

    [Test]
    public async Task Log_WithException_IncludesExceptionInMessage()
    {
        var (logService, _, logger) = Create();
        var ex = new InvalidOperationException("inner failure");

        logger.LogError(ex, "outer failure");

        var entries = logService.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Message.Contains("outer failure")).IsTrue();
        await Assert.That(entries[0].Message.Contains("Exception:")).IsTrue();
        await Assert.That(entries[0].Message.Contains("inner failure")).IsTrue();
    }

    [Test]
    public async Task Log_BelowMinimumLevel_IsFiltered()
    {
        var (logService, _, logger) = Create(minimumLevel: LogLevel.Warning);

        logger.LogInformation("filtered out");

        var entries = logService.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Log_NoneLevel_IsAlwaysFiltered()
    {
        var (logService, _, logger) = Create();

        logger.Log(LogLevel.None, "should not appear");

        var entries = logService.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task IsEnabled_NoneLevel_ReturnsFalse()
    {
        var (_, _, logger) = Create();

        await Assert.That(logger.IsEnabled(LogLevel.None)).IsFalse();
    }

    [Test]
    public async Task IsEnabled_AboveMinimum_ReturnsTrue()
    {
        var (_, _, logger) = Create(minimumLevel: LogLevel.Warning);

        await Assert.That(logger.IsEnabled(LogLevel.Warning)).IsTrue();
        await Assert.That(logger.IsEnabled(LogLevel.Error)).IsTrue();
        await Assert.That(logger.IsEnabled(LogLevel.Critical)).IsTrue();
    }

    [Test]
    public async Task IsEnabled_BelowMinimum_ReturnsFalse()
    {
        var (_, _, logger) = Create(minimumLevel: LogLevel.Warning);

        await Assert.That(logger.IsEnabled(LogLevel.Trace)).IsFalse();
        await Assert.That(logger.IsEnabled(LogLevel.Debug)).IsFalse();
        await Assert.That(logger.IsEnabled(LogLevel.Information)).IsFalse();
    }

    [Test]
    public async Task Log_WithEventId_IncludesEventIdInMessage()
    {
        var (logService, _, logger) = Create();
        var eventId = new EventId(42, "TestEvent");

        logger.LogInformation(eventId, "with event id");

        var entries = logService.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Message.Contains("[42]")).IsTrue();
    }

    [Test]
    public async Task Log_EventIdZero_OmitsEventIdInMessage()
    {
        var (logService, _, logger) = Create();

        logger.LogInformation(new EventId(0), "no event id");

        var entries = logService.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Message.Contains("[0]")).IsFalse();
    }

    [Test]
    public async Task BeginScope_ReturnsNonNullableDisposable()
    {
        var (_, _, logger) = Create();

        var scope = logger.BeginScope("some state");

        await Assert.That(scope).IsNotNull();
        scope!.Dispose();
    }

    [Test]
    public async Task Dispose_DoesNotThrow()
    {
        var (_, provider, _) = Create();

        await Assert.That(() => provider.Dispose()).ThrowsNothing();
    }

    [Test]
    public async Task Log_RecursiveHandler_DoesNotStackOverflow()
    {
        // 验证 AsyncLocal 防回环：handler 内部再次调用 ILogger.Log，
        // 不应触发递归写入到 LogService（避免栈溢出）。
        var logService = new LogService();
        var provider = new LogServiceLoggerProvider(logService);
        var logger = provider.CreateLogger("Recursive");

        // 注册一个 handler，内部再次调用 logger.Log
        logService.AddHandler((level, message, ex, fields) =>
        {
            // 这条日志应被 AsyncLocal 标志跳过
            logger.LogWarning("recursive attempt");
        });

        logger.LogInformation("initial log");

        // 初始日志应被记录，递归日志应被跳过
        var entries = logService.GetEntries();
        await Assert.That(entries.Count).IsEqualTo(1);
        await Assert.That(entries[0].Message.Contains("initial log")).IsTrue();
        await Assert.That(entries[0].Message.Contains("recursive attempt")).IsFalse();
    }
}
