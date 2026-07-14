using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Options;

namespace Wails.Net.Application.Services;

/// <summary>
/// 日志条目记录。
/// Level 字段使用 <see cref="Microsoft.Extensions.Logging.LogLevel"/>，
/// 对应 AGENTS.md §1.1.1 的技术选型要求：日志统一使用 <c>Microsoft.Extensions.Logging.ILogger&lt;T&gt;</c> 抽象。
/// </summary>
public sealed class LogEntry
{
    /// <summary>
    /// 获取日志级别（基于 <see cref="Microsoft.Extensions.Logging.LogLevel"/>）。
    /// </summary>
    public LogLevel Level { get; init; }

    /// <summary>
    /// 获取日志消息。
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// 获取日志时间戳。
    /// </summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// 自定义日志处理委托。
/// 日志写入时依次调用所有已注册的处理器，可据此接入外部日志管线（如 ILogger）。
/// </summary>
/// <param name="level">日志级别字符串（Debug/Information/Warning/Error/Critical 或兼容别名 Info/Fatal）。</param>
/// <param name="message">日志消息。</param>
/// <param name="exception">异常实例，可为 null。</param>
/// <param name="fields">结构化字段字典，可为 null。</param>
public delegate void LogHandler(string level, string message, Exception? exception, IReadOnlyDictionary<string, object?>? fields);

/// <summary>
/// 日志服务，允许前端写入日志并支持级别过滤。
/// 对应 Wails v3 Go 版本 pkg/services/log。
/// 日志写入 System.Diagnostics.Trace 并可选写入文件。
/// 统一使用 <see cref="Microsoft.Extensions.Logging.LogLevel"/> 枚举，与 ASP.NET Core 日志抽象保持一致。
/// </summary>
public class LogService : IServiceStartup, IServiceShutdown
{
    /// <summary>
    /// 日志条目列表，用于内部记录和测试验证。
    /// </summary>
    private readonly List<LogEntry> _entries = new();

    /// <summary>
    /// 线程安全锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 日志文件写入器，为 null 时不写入文件。
    /// </summary>
    private StreamWriter? _writer;

    /// <summary>
    /// 自定义日志处理器列表。
    /// 日志写入时依次调用所有已注册的处理器。
    /// </summary>
    private readonly List<LogHandler> _handlers = new();

    /// <summary>
    /// 获取或设置最低日志级别，低于此级别的日志将被忽略。
    /// 默认为 <see cref="LogLevel.Debug"/>（记录所有级别）。
    /// 对应 <c>Microsoft.Extensions.Logging.LogLevel</c> 枚举。
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// 获取或设置日志文件路径。
    /// 在服务启动前设置以启用文件日志。
    /// </summary>
    public string? LogFilePath { get; set; }

    /// <summary>
    /// 使用默认配置构造日志服务实例。
    /// </summary>
    public LogService()
    {
    }

    /// <summary>
    /// 使用指定日志文件路径构造日志服务实例。
    /// </summary>
    /// <param name="logFilePath">日志文件路径。</param>
    public LogService(string logFilePath)
    {
        LogFilePath = logFilePath;
    }

    /// <summary>
    /// 服务启动，若配置了日志文件路径则打开文件写入器。
    /// </summary>
    /// <param name="options">应用选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动操作的异步任务。</returns>
    public Task ServiceStartup(ApplicationOptions options, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(LogFilePath))
        {
            var dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _writer = new StreamWriter(LogFilePath, append: true) { AutoFlush = true };
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 服务关闭，关闭日志文件写入器。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示关闭操作的异步任务。</returns>
    public Task ServiceShutdown(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 写入指定级别的日志。
    /// 若级别低于最低级别则忽略。
    /// 支持的 level 字符串：Trace/Debug/Information/Warning/Error/Critical/None，
    /// 以及向后兼容的别名 Info→Information、Fatal→Critical。
    /// </summary>
    /// <param name="level">日志级别字符串。</param>
    /// <param name="message">日志消息。</param>
    public void Log(string level, string message)
    {
        if (TryParseLevel(level, out var logLevel))
        {
            Log(logLevel, message);
        }
    }

    /// <summary>
    /// 写入调试级别日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public void Debug(string message) => Log(LogLevel.Debug, message);

    /// <summary>
    /// 写入信息级别日志（对应 <see cref="LogLevel.Information"/>）。
    /// 方法名保留 Info 以保持向后兼容。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public void Info(string message) => Log(LogLevel.Information, message);

    /// <summary>
    /// 写入警告级别日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public void Warning(string message) => Log(LogLevel.Warning, message);

    /// <summary>
    /// 写入错误级别日志。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public void Error(string message) => Log(LogLevel.Error, message);

    /// <summary>
    /// 写入致命级别日志（对应 <see cref="LogLevel.Critical"/>）。
    /// 方法名保留 Fatal 以保持向后兼容。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public void Fatal(string message) => Log(LogLevel.Critical, message);

    /// <summary>
    /// 注册自定义日志处理器。
    /// 日志写入时将依次调用所有已注册的处理器。
    /// </summary>
    /// <param name="handler">要注册的日志处理器。</param>
    public void AddHandler(LogHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
        {
            _handlers.Add(handler);
        }
    }

    /// <summary>
    /// 写入带结构化字段的日志。
    /// 对应结构化日志模型（类似 ILogger 的 LogScope + Log 模式），
    /// 字段字典会传递给所有已注册的 handler。
    /// </summary>
    /// <param name="level">日志级别字符串（Trace/Debug/Information/Warning/Error/Critical，或别名 Info/Fatal）。</param>
    /// <param name="message">日志消息。</param>
    /// <param name="fields">结构化字段字典。</param>
    public void LogStructured(string level, string message, Dictionary<string, object?> fields)
    {
        if (TryParseLevel(level, out var logLevel))
        {
            IReadOnlyDictionary<string, object?>? readOnlyFields =
                fields is null ? null : new Dictionary<string, object?>(fields);
            WriteLog(logLevel, message, readOnlyFields);
        }
    }

    /// <summary>
    /// 获取所有已记录的日志条目（主要用于测试验证）。
    /// </summary>
    /// <returns>日志条目的只读列表。</returns>
    internal IReadOnlyList<LogEntry> GetEntries()
    {
        lock (_lock)
        {
            return _entries.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// 写入指定级别的日志，应用级别过滤。
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <param name="message">日志消息。</param>
    private void Log(LogLevel level, string message)
    {
        WriteLog(level, message, null);
    }

    /// <summary>
    /// 写入结构化日志的核心实现，应用级别过滤并依次调用所有注册的 handler。
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <param name="message">日志消息。</param>
    /// <param name="fields">结构化字段字典，可为 null。</param>
    private void WriteLog(LogLevel level, string message, IReadOnlyDictionary<string, object?>? fields)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var entry = new LogEntry
        {
            Level = level,
            Message = message,
            Timestamp = DateTime.UtcNow
        };

        LogHandler[] handlersCopy;
        lock (_lock)
        {
            _entries.Add(entry);
            var formatted = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
            Trace.WriteLine(formatted);
            _writer?.WriteLine(formatted);
            handlersCopy = _handlers.ToArray();
        }

        // 在锁外调用 handler 以避免回调中再次写日志导致死锁
        var levelStr = level.ToString();
        foreach (var handler in handlersCopy)
        {
            try
            {
                handler(levelStr, message, null, fields);
            }
            catch
            {
                // handler 中的异常不应中断日志流程
            }
        }
    }

    /// <summary>
    /// 将字符串解析为 <see cref="LogLevel"/>，不区分大小写。
    /// 支持标准枚举名（Trace/Debug/Information/Warning/Error/Critical/None），
    /// 以及向后兼容的别名：Info → Information、Fatal → Critical、Warn → Warning。
    /// </summary>
    /// <param name="level">级别字符串。</param>
    /// <param name="result">解析结果。</param>
    /// <returns>解析成功返回 true，否则返回 false。</returns>
    private static bool TryParseLevel(string level, out LogLevel result)
    {
        if (Enum.TryParse(level, ignoreCase: true, out result))
        {
            return true;
        }

        // 向后兼容别名映射
        return level.ToLowerInvariant() switch
        {
            "info" => SetResult(LogLevel.Information, out result),
            "fatal" => SetResult(LogLevel.Critical, out result),
            "warn" => SetResult(LogLevel.Warning, out result),
            _ => SetResult(default, out result),
        };
    }

    private static bool SetResult(LogLevel value, out LogLevel result)
    {
        result = value;
        return true;
    }
}
