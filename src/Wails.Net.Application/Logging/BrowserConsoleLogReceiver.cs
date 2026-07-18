using Microsoft.Extensions.Logging;
using Wails.Net.Application.Managers;
using Wails.Net.Application.Services;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Logging;

/// <summary>
/// 浏览器控制台日志接收器（P1-3-4 Direction 3：前端 console → 后端日志）。
/// <para>
/// 订阅所有 <see cref="WebviewWindow"/> 的 <c>SetConsoleMessageHandler</c> 事件，
/// 将前端 JavaScript 的 <c>console.log/info/warn/error/debug</c> 调用转发到后端 <see cref="LogService"/>。
/// </para>
/// <para>
/// 与 <see cref="BrowserConsoleLogForwarder"/>（Direction 2：后端 → 前端）配合使用时，
/// 通过 <see cref="BrowserConsoleMessageSourceMarker"/> 字段在 LogService 条目中标记来源，
/// 避免形成"后端日志 → 前端 console → 后端日志"的无限循环。
/// </para>
/// <para>
/// 这是 Wails.Net 扩展能力，Wails v3 不提供此功能。
/// </para>
/// </summary>
public sealed class BrowserConsoleLogReceiver : IDisposable
{
    /// <summary>
    /// 日志服务实例。
    /// </summary>
    private readonly LogService _logService;

    /// <summary>
    /// 窗口管理器实例。
    /// </summary>
    private readonly WindowManager _windowManager;

    /// <summary>
    /// 诊断日志记录器。
    /// </summary>
    private readonly ILogger<BrowserConsoleLogReceiver> _diagnosticLogger;

    /// <summary>
    /// 已注册的窗口集合，用于跟踪已订阅的窗口并在 Dispose 时取消订阅。
    /// </summary>
    private readonly HashSet<uint> _registeredWindowIds = new();

    /// <summary>
    /// 注册的 console 消息回调，用于在 Dispose 时取消注册。
    /// </summary>
    private Action<BrowserConsoleMessageLevel, string>? _handler;

    /// <summary>
    /// 用于保护 _registeredWindowIds 的锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 是否已释放。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 构造 <see cref="BrowserConsoleLogReceiver"/> 实例。
    /// </summary>
    /// <param name="logService">日志服务实例。</param>
    /// <param name="windowManager">窗口管理器实例。</param>
    /// <param name="diagnosticLogger">诊断日志记录器，为 null 时使用 NullLogger。</param>
    public BrowserConsoleLogReceiver(
        LogService logService,
        WindowManager windowManager,
        ILogger<BrowserConsoleLogReceiver>? diagnosticLogger = null)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _diagnosticLogger = diagnosticLogger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BrowserConsoleLogReceiver>.Instance;
    }

    /// <summary>
    /// 启动接收器：订阅所有现有窗口的 console 事件，并监听新窗口创建事件。
    /// 应在应用启动后调用（或通过 IHostedService 自动调用）。
    /// <para>
    /// 此方法是幂等的：可多次调用以重新扫描未注册的窗口。
    /// 当窗口创建时 Impl 通常尚未设置（由平台层异步绑定），
    /// 外部组件可在 Impl 就绪后再次调用 Start 触发重新扫描。
    /// </para>
    /// </summary>
    public void Start()
    {
        if (_handler is null)
        {
            _handler = OnConsoleMessage;
            _windowManager.WindowCreated += OnWindowCreated;
        }

        // 扫描所有窗口，注册未注册的（幂等）
        foreach (var window in _windowManager.GetAllWindows())
        {
            RegisterWindow(window);
        }
    }

    /// <summary>
    /// 注册指定窗口的 console 事件处理器。
    /// </summary>
    /// <param name="window">要注册的窗口。</param>
    private void RegisterWindow(WebviewWindow window)
    {
        if (window.Impl is null)
        {
            // 窗口尚未完成平台绑定，跳过；新窗口创建时 Impl 通常尚未设置，
            // 后续会在 Impl 设置后由平台层调用 SetConsoleMessageHandler。
            // 此处仍记录窗口 ID，以便平台层就绪后再次尝试注册。
            _diagnosticLogger.LogDebug("窗口 {WindowId} 的平台实现尚未就绪，跳过 console 事件订阅", window.ID);
            return;
        }

        lock (_lock)
        {
            if (!_registeredWindowIds.Add(window.ID))
            {
                // 已注册
                return;
            }
        }

        try
        {
            window.Impl.SetConsoleMessageHandler(_handler);
            _diagnosticLogger.LogDebug("已订阅窗口 {WindowId} 的 console 消息", window.ID);
        }
        catch (Exception ex)
        {
            _diagnosticLogger.LogWarning(ex, "订阅窗口 {WindowId} 的 console 消息失败", window.ID);
            lock (_lock)
            {
                _registeredWindowIds.Remove(window.ID);
            }
        }
    }

    /// <summary>
    /// 窗口创建事件处理器：订阅新窗口的 console 事件。
    /// </summary>
    /// <param name="window">新创建的窗口。</param>
    private void OnWindowCreated(WebviewWindow window)
    {
        RegisterWindow(window);
    }

    /// <summary>
    /// console 消息回调：将前端 console 调用转发到 LogService。
    /// 使用 <see cref="BrowserConsoleMessageSourceMarker"/> 标记来源，避免与 Forwarder 形成循环。
    /// </summary>
    /// <param name="level">消息级别。</param>
    /// <param name="message">消息文本。</param>
    private void OnConsoleMessage(BrowserConsoleMessageLevel level, string message)
    {
        var logLevel = MapToLogLevel(level);
        var fields = new Dictionary<string, object?>
        {
            [BrowserConsoleMessageSourceMarker.SourceField] = BrowserConsoleMessageSourceMarker.Browser
        };
        _logService.LogStructured(logLevel.ToString(), message, fields);
    }

    /// <summary>
    /// 将 <see cref="BrowserConsoleMessageLevel"/> 映射到 <see cref="LogLevel"/>。
    /// </summary>
    /// <param name="level">浏览器控制台消息级别。</param>
    /// <returns>对应的 .NET 日志级别。</returns>
    private static LogLevel MapToLogLevel(BrowserConsoleMessageLevel level)
    {
        return level switch
        {
            BrowserConsoleMessageLevel.Debug => LogLevel.Debug,
            BrowserConsoleMessageLevel.Info => LogLevel.Information,
            BrowserConsoleMessageLevel.Warning => LogLevel.Warning,
            BrowserConsoleMessageLevel.Error => LogLevel.Error,
            _ => LogLevel.Information,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _windowManager.WindowCreated -= OnWindowCreated;

        // 取消所有窗口的 console 处理器注册
        List<uint> ids;
        lock (_lock)
        {
            ids = _registeredWindowIds.ToList();
            _registeredWindowIds.Clear();
        }

        foreach (var id in ids)
        {
            var window = _windowManager.GetWindow(id);
            try
            {
                window?.Impl?.SetConsoleMessageHandler(null);
            }
            catch (Exception ex)
            {
                _diagnosticLogger.LogDebug(ex, "取消窗口 {WindowId} 的 console 订阅失败", id);
            }
        }
    }
}

/// <summary>
/// 控制台消息来源标记常量（P1-3-4）。
/// <para>
/// 用于在 <see cref="LogService"/> 的 fields 字典中标记消息来源，
/// 使 <see cref="BrowserConsoleLogForwarder"/> 能识别并跳过来自前端 console 的回环消息。
/// </para>
/// </summary>
internal static class BrowserConsoleMessageSourceMarker
{
    /// <summary>
    /// fields 字典中表示消息来源的字段名。
    /// </summary>
    public const string SourceField = "source";

    /// <summary>
    /// 表示消息来自前端浏览器 console。
    /// </summary>
    public const string Browser = "browser";
}
