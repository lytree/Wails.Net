using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Transport;

/// <summary>
/// 事件 IPC 兜底传输层。
/// 对应 Wails v3 Go 版本 <c>EventIPCTransport</c>（<c>transport_event_ipc.go</c>）。
/// <para>
/// 当主 Transport（如 <see cref="HttpTransport"/>）未实现 <see cref="IWailsEventListener"/>，
/// 或虽然实现但需要确保桌面端 webview 一定能收到事件时，由 <see cref="Application"/>
/// 自动追加此监听器作为兜底，通过 <see cref="WebviewWindow.ExecJS"/> 直接把事件 JSON
/// 注入到每个窗口的 webview 中，绕过 HTTP/WebSocket 通道。
/// </para>
/// <para>
/// <b>设计目的</b>：
/// <list type="bullet">
/// <item>HTTP 单独使用时（无 WebSocket 客户端连接）事件仍能推送到前端 webview。</item>
/// <item>事件推送不依赖传输层连接状态，确保桌面端关键事件（如窗口关闭、应用退出）必达。</item>
/// <item>对应 Wails v3 中 <c>EventIPCTransport.DispatchWailsEvent</c> 调用
/// <c>window.DispatchWailsEvent(event)</c> 的 ExecJS 注入机制。</item>
/// </list>
/// </para>
/// <para>
/// <b>互操作说明</b>：本实现通过调用前端 <c>window._wailsEmitEvent(name, data)</c>
/// 触发已注册的本地事件回调，与 <c>transport.template.js</c> 中定义的回调注册表对齐。
/// </para>
/// <para>
/// <b>与 NativeIpcTransport 的协作（P0-A1 完善）</b>：
/// 当 <see cref="Application.NativeIpcTransport"/> 已注册时，事件已通过原生 postMessage
/// 副通道推送到所有窗口。此时 EventIPCTransport 不再重复注入 ExecJS，避免前端收到
/// 重复事件回调。仅当 NativeIpcTransport 未注册或无窗口时，回退到 ExecJS 路径。
/// </para>
/// </summary>
public sealed class EventIPCTransport : IWailsEventListener
{
    /// <summary>
    /// JSON 序列化选项，使用驼峰命名以匹配前端 JS 习惯。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 可选的日志记录器。未注入时静默处理所有异常。
    /// </summary>
    private readonly ILogger<EventIPCTransport>? _logger;

    /// <summary>
    /// 构造默认 EventIPCTransport 实例（无日志）。
    /// </summary>
    public EventIPCTransport()
    {
    }

    /// <summary>
    /// 构造带日志记录器的 EventIPCTransport 实例。
    /// </summary>
    /// <param name="logger">日志记录器实例。</param>
    public EventIPCTransport(ILogger<EventIPCTransport> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 通知事件：将事件通过 ExecJS 注入到所有窗口的 webview。
    /// <para>
    /// 若 <see cref="Application.NativeIpcTransport"/> 已注册且至少有一个窗口，
    /// 则事件已通过原生 postMessage 推送，此方法跳过 ExecJS 注入以避免重复派发。
    /// </para>
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据，可为 null。</param>
    /// <param name="senderWindowId">事件来源窗口 ID，可为 null（应用级事件）。
    /// 将作为第三个参数传递给前端 <c>window._wailsEmitEvent(name, data, senderWindowId)</c>，
    /// 使前端可识别事件发起方并据此过滤或显示。</param>
    public void NotifyEvent(string eventName, object? data, uint? senderWindowId = null)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return;
        }

        var app = Application.Get();

        // P0-A1 完善：若 NativeIpcTransport 已注册并拥有窗口，事件已通过原生 postMessage
        // 推送。跳过 ExecJS 路径以避免前端收到重复事件回调。
        // 仅当 NativeIpcTransport 未注册或无窗口时，回退到 ExecJS 兜底路径。
        var nativeIpc = app?.NativeIpcTransport;
        if (nativeIpc is not null && nativeIpc.RegisteredWindowCount > 0)
        {
            return;
        }

        var windowManager = app?.WindowManager;
        if (windowManager is null)
        {
            return;
        }

        // 对应 Wails v3 transport_event_ipc.go 中先复制窗口列表再释放锁，
        // 避免 ExecJS 时持有锁导致死锁（ExecJS 可能同步等待 UI 线程）。
        // IWindowManager.GetAllWindows() 返回 IReadOnlyList<WebviewWindow>，
        // 内部已做拷贝，可安全在锁外枚举。
        IReadOnlyList<WebviewWindow> windows;
        try
        {
            windows = windowManager.GetAllWindows();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "获取窗口列表失败，跳过事件派发: {EventName}", eventName);
            return;
        }

        if (windows.Count == 0)
        {
            return;
        }

        // 构造注入脚本：window._wailsEmitEvent(name, data, senderWindowId);
        // 使用 JSON 序列化数据，避免拼接字符串时的转义问题。
        // P1-2：senderWindowId 作为第三个参数传递，前端可据此识别事件来源窗口。
        var nameJson = JsonSerializer.Serialize(eventName, JsonOptions);
        var dataJson = data is null
            ? "null"
            : JsonSerializer.Serialize(data, data.GetType(), JsonOptions);
        var senderJson = senderWindowId is null ? "null" : senderWindowId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var js = $"window._wailsEmitEvent && window._wailsEmitEvent({nameJson}, {dataJson}, {senderJson});";

        // 对应 Wails v3 EventIPCTransport.DispatchWailsEvent：
        //   for _, window := range windows {
        //       if event.IsCancelled() { return }
        //       window.DispatchWailsEvent(event)
        //   }
        foreach (var window in windows)
        {
            try
            {
                // WebviewWindow.ExecJS 内部会调用平台实现的 ExecuteScriptAsync。
                // 失败的窗口（已关闭、已释放）不应影响其他窗口的事件推送，
                // 异常会被 catch 捕获并记录日志。
                window.ExecJS(js);
            }
            catch (Exception ex)
            {
                // 单个窗口注入失败仅记录日志，不影响其他窗口。
                _logger?.LogDebug(ex, "向窗口 {WindowId} 派发事件 {EventName} 失败",
                    window.ID, eventName);
            }
        }
    }
}
