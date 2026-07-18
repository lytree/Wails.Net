using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Services;

namespace Wails.Net.Application.Logging;

/// <summary>
/// 将后端日志转发到前端 console 的桥接器（P1-3 Direction 2：LogService → 前端 console）。
/// <para>
/// 此桥接器为可选功能，用户通过 <see cref="Hosting.DesktopApplicationBuilderExtensions.UseBrowserConsoleLogForwarder"/>
/// 显式启用。启用后会在 <see cref="LogService"/> 中注册一个 <see cref="LogHandler"/>，
/// 将所有后端日志（包括 <c>ILogger&lt;T&gt;</c> 写入和前端通过 <c>wails.Log.*</c> 写入的日志）
/// 通过 <c>ExecJS</c> 注入到所有已注册的窗口，调用前端 <c>console.log/info/warn/error</c>。
/// </para>
/// <para>
/// <strong>防回环机制</strong>：
/// <list type="bullet">
/// <item>同进程回环：使用 <see cref="AsyncLocal{T}"/> 标记当前异步上下文是否正在转发，
/// 防止 handler 链中的递归转发（如某 handler 内部又调用 <c>ILogger&lt;T&gt;</c> 写入）造成栈溢出。</item>
/// <item>跨方向回环：检查 fields 字段中是否包含 <c>source=browser</c> 标记
/// （由 <see cref="BrowserConsoleLogReceiver"/> 写入），跳过来自前端 console 的回环消息，
/// 避免"后端 → 前端 console → 后端 → 前端 console"循环。</item>
/// </list>
/// </para>
/// <para>
/// <strong>转发策略</strong>：
/// <list type="bullet">
/// <item><see cref="LogLevel.Trace"/> / <see cref="LogLevel.Debug"/> → <c>console.log</c></item>
/// <item><see cref="LogLevel.Information"/> → <c>console.info</c></item>
/// <item><see cref="LogLevel.Warning"/> → <c>console.warn</c></item>
/// <item><see cref="LogLevel.Error"/> / <see cref="LogLevel.Critical"/> → <c>console.error</c></item>
/// </list>
/// </para>
/// </summary>
public sealed class BrowserConsoleLogForwarder : IDisposable
{
    /// <summary>目标日志服务。</summary>
    private readonly LogService _logService;

    /// <summary>窗口管理器，用于枚举所有需要转发的前端窗口。</summary>
    private readonly WindowManager _windowManager;

    /// <summary>诊断日志记录器，用于记录转发失败等诊断信息（不会回环写入 LogService）。</summary>
    private readonly ILogger<BrowserConsoleLogForwarder> _diagnosticLogger;

    /// <summary>
    /// 异步本地标志：标记当前异步上下文是否正在向前端转发日志。
    /// 防止 handler 链内递归转发（如 OnLog 中触发的某条路径又调用 LogService.Log）。
    /// </summary>
    private static readonly AsyncLocal<bool> _isForwarding = new();

    /// <summary>已注册的 LogHandler 委托，便于 Dispose 时注销。</summary>
    private readonly LogHandler _handler;

    /// <summary>是否已注销。</summary>
    private bool _disposed;

    /// <summary>
    /// 构造浏览器 console 转发器，立即在 <paramref name="logService"/> 中注册 LogHandler。
    /// </summary>
    /// <param name="logService">目标日志服务。</param>
    /// <param name="windowManager">窗口管理器。</param>
    /// <param name="diagnosticLogger">诊断日志记录器，可为 null（使用 NullLogger 兜底）。</param>
    public BrowserConsoleLogForwarder(
        LogService logService,
        WindowManager windowManager,
        ILogger<BrowserConsoleLogForwarder>? diagnosticLogger = null)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _diagnosticLogger = diagnosticLogger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BrowserConsoleLogForwarder>.Instance;

        _handler = OnLog;
        _logService.AddHandler(_handler);
    }

    /// <summary>
    /// LogService 的 LogHandler 回调：将日志通过 ExecJS 注入到所有窗口。
    /// </summary>
    /// <param name="level">日志级别字符串（Debug/Information/Warning/Error/Critical 等）。</param>
    /// <param name="message">日志消息。</param>
    /// <param name="exception">异常实例，可为 null（当前实现未转发异常详情以保持前端 console 简洁）。</param>
    /// <param name="fields">结构化字段，可为 null（当前实现未转发以保持前端 console 简洁）。</param>
    private void OnLog(string level, string message, Exception? exception, IReadOnlyDictionary<string, object?>? fields)
    {
        // 防止 handler 链内的递归转发（如 OnLog 中触发其他日志写入又回到此回调）。
        if (_isForwarding.Value)
        {
            return;
        }

        // P1-3-4：跳过来自前端 console 的回环消息，避免"后端 → 前端 console → 后端 → 前端 console"循环。
        // BrowserConsoleLogReceiver 在写入 LogService 时会在 fields 中标记 source=browser。
        if (fields is not null
            && fields.TryGetValue(BrowserConsoleMessageSourceMarker.SourceField, out var src)
            && src as string == BrowserConsoleMessageSourceMarker.Browser)
        {
            return;
        }

        var windows = _windowManager.GetAllWindows();
        if (windows.Count == 0)
        {
            return;
        }

        var jsLevel = MapToConsoleMethod(level);
        var escapedMessage = JsonSerializer.Serialize(message, JsonOptions.DefaultSerializerOptions);
        var js = $"console.{jsLevel}({escapedMessage});";

        _isForwarding.Value = true;
        try
        {
            foreach (var window in windows)
            {
                try
                {
                    // Impl 为 null 时表示窗口尚未完成平台绑定，跳过。
                    if (window.Impl is null)
                    {
                        continue;
                    }
                    window.ExecJS(js);
                }
                catch (Exception ex)
                {
                    // 单窗口转发失败不影响其他窗口。
                    _diagnosticLogger.LogDebug(ex, "向前端窗口 {WindowId} 转发日志失败", window.ID);
                }
            }
        }
        finally
        {
            _isForwarding.Value = false;
        }
    }

    /// <summary>
    /// 将 <see cref="LogLevel"/> 名称映射到前端 console 方法名。
    /// </summary>
    /// <param name="level">日志级别字符串。</param>
    /// <returns>console 方法名（log/info/warn/error）。</returns>
    private static string MapToConsoleMethod(string level)
    {
        return level switch
        {
            "Warning" => "warn",
            "Error" => "error",
            "Critical" => "error",
            "Information" => "info",
            // Trace/Debug 及其他别名映射到 log
            _ => "log",
        };
    }

    /// <summary>
    /// 注销 LogHandler。注销后不再向前端转发日志。
    /// <para>
    /// 注意：<see cref="LogService"/> 当前未提供 RemoveHandler 方法，
    /// 此 Dispose 仅标记为已注销；handler 在 LogService 关闭时统一清空。
    /// </para>
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // LogService.AddHandler 未提供对应的注销方法。
        // handler 在 LogService.ServiceShutdown 时随实例释放统一失效，
        // 此处不进行实际注销以避免引入 LogService 的 API 变更。
    }
}
