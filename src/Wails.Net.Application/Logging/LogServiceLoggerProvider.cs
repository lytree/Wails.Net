using Microsoft.Extensions.Logging;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Logging;

/// <summary>
/// 将 <see cref="Microsoft.Extensions.Logging.ILogger{T}"/> 写入桥接到 <see cref="LogService"/> 的日志提供程序。
/// <para>
/// 对应 P1-3：Logger ↔ 前端 console 桥接的 Direction 1（.NET ILogger → LogService）。
/// 此提供程序在 <see cref="Hosting.DesktopApplicationBuilder"/> 中默认注册，使所有
/// <c>ILogger&lt;T&gt;</c> 写入自动进入 <see cref="LogService"/> 的 handler 链，
/// 完成 <see cref="Application.Logger"/> 与 <see cref="LogService"/> 的连通。
/// </para>
/// <para>
/// 设计说明：
/// <list type="bullet">
/// <item>每个日志类别创建独立的 <see cref="LogServiceLogger"/> 实例，附加类别前缀。</item>
/// <item>使用 <see cref="AsyncLocal{T}"/> 防止 handler 链中的递归写入导致栈溢出。</item>
/// <item>不实现 <see cref="IDisposable.Dispose"/> 的真实逻辑——<see cref="LogService"/> 由
/// <see cref="Services.IServiceShutdown"/> 自行管理生命周期。</item>
/// </list>
/// </para>
/// </summary>
public sealed class LogServiceLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// 被包装的 <see cref="LogService"/> 实例。
    /// </summary>
    private readonly LogService _logService;

    /// <summary>
    /// 异步本地标志：标记当前执行上下文是否正在向 <see cref="LogService"/> 写入。
    /// 防止 <see cref="LogServiceLogger.Log{TState}"/> 在 handler 链中被递归触发时形成无限循环
    /// （例如某 handler 内部又使用 <c>ILogger&lt;T&gt;</c> 记录日志）。
    /// </summary>
    private static readonly AsyncLocal<bool> _isWriting = new();

    /// <summary>
    /// 构造日志服务桥接提供程序。
    /// </summary>
    /// <param name="logService">被包装的日志服务实例。</param>
    public LogServiceLoggerProvider(LogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    /// <summary>
    /// 创建指定类别的日志记录器。
    /// </summary>
    /// <param name="categoryName">日志类别名（通常是 <c>typeof(T).FullName</c>）。</param>
    /// <returns>包装 <see cref="LogService"/> 的 <see cref="ILogger"/> 实例。</returns>
    public ILogger CreateLogger(string categoryName)
    {
        ArgumentException.ThrowIfNullOrEmpty(categoryName);
        return new LogServiceLogger(_logService, categoryName);
    }

    /// <summary>
    /// 空实现。<see cref="LogService"/> 的生命周期由 <see cref="Services.IServiceShutdown"/> 管理。
    /// </summary>
    public void Dispose()
    {
    }

    /// <summary>
    /// 包装 <see cref="LogService"/> 的 <see cref="ILogger"/> 实现。
    /// </summary>
    private sealed class LogServiceLogger : ILogger
    {
        /// <summary>目标日志服务。</summary>
        private readonly LogService _logService;

        /// <summary>日志类别名，将作为消息前缀以区分来源。</summary>
        private readonly string _categoryName;

        /// <summary>构造日志记录器。</summary>
        /// <param name="logService">目标日志服务。</param>
        /// <param name="categoryName">日志类别名。</param>
        public LogServiceLogger(LogService logService, string categoryName)
        {
            _logService = logService;
            _categoryName = categoryName;
        }

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None && logLevel >= _logService.MinimumLevel;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            // 防止 handler 链内的递归写入（如某 LogHandler 内部调用 ILogger<T>.Log）。
            if (_isWriting.Value)
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message = $"{message}{Environment.NewLine}Exception: {exception}";
            }

            var formatted = eventId.Id != 0
                ? $"[{_categoryName}] [{eventId.Id}] {message}"
                : $"[{_categoryName}] {message}";

            _isWriting.Value = true;
            try
            {
                _logService.Log(logLevel, formatted);
            }
            finally
            {
                _isWriting.Value = false;
            }
        }
    }

    /// <summary>
    /// 空作用域，用于 <see cref="BeginScope{TState}"/> 实现。
    /// </summary>
    private sealed class NullScope : IDisposable
    {
        /// <summary>共享的空作用域实例。</summary>
        public static readonly NullScope Instance = new();

        private NullScope() { }

        /// <inheritdoc />
        public void Dispose() { }
    }
}
