using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Events;
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
    /// 每窗口有序事件投递队列（事件投递硬化 P0-1，对应 Wails v3 PR #5934）。
    /// <para>
    /// ExecJS 注入此前在 <see cref="NotifyEvent"/> 中同步逐窗口执行，且并发 <c>Emit</c>
    /// 之间会交错。改由本队列按窗口串行：既保证「每窗口 FIFO」，又避免慢窗口阻塞
    /// <see cref="Events.EventMailbox{T}"/> 之外的其他窗口投递。
    /// </para>
    /// </summary>
    private readonly PerWindowEventQueue _eventQueue;

    /// <summary>
    /// 构造默认 EventIPCTransport 实例（无日志）。
    /// </summary>
    public EventIPCTransport()
        : this(null, PerWindowEventQueue.DefaultCapacity)
    {
    }

    /// <summary>
    /// 构造带日志记录器的 EventIPCTransport 实例。
    /// </summary>
    /// <param name="logger">日志记录器实例。</param>
    public EventIPCTransport(ILogger<EventIPCTransport> logger)
        : this(logger, PerWindowEventQueue.DefaultCapacity)
    {
    }

    /// <summary>
    /// 构造 EventIPCTransport 实例（可指定每窗口事件队列容量，供测试使用）。
    /// </summary>
    /// <param name="logger">日志记录器，可为 null。</param>
    /// <param name="eventQueueCapacity">每窗口事件队列容量。</param>
    internal EventIPCTransport(ILogger<EventIPCTransport>? logger, int eventQueueCapacity)
    {
        _logger = logger;
        _eventQueue = new PerWindowEventQueue(eventQueueCapacity, logger);
    }

    /// <summary>
    /// 通知事件：将事件通过 ExecJS 注入到窗口的 webview。
    /// <para>
    /// OPT-EVENT-002：<paramref name="targetWindowId"/> 非 null 时仅注入到目标窗口，
    /// 其他窗口不收到（跨窗口定向事件）；null 时广播到所有窗口。
    /// </para>
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
    /// <param name="targetWindowId">定向目标窗口 ID（OPT-EVENT-002）；null 表示广播。</param>
    public void NotifyEvent(string eventName, object? data, uint? senderWindowId = null, uint? targetWindowId = null)
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

        // 构造注入脚本：window._wailsEmitEvent(name, data, senderWindowId, targetWindowId)。
        // 使用 JSON 序列化数据，避免拼接字符串时的转义问题。
        // P1-2：senderWindowId 作为第三个参数传递，前端可据此识别事件来源窗口。
        // OPT-EVENT-002：targetWindowId 作为第四个参数传递（可空），前端可据此识别定向事件。
        var nameJson = JsonSerializer.Serialize(eventName, JsonOptions);
        var dataJson = data is null
            ? "null"
            : JsonSerializer.Serialize(data, data.GetType(), JsonOptions);
        var senderJson = senderWindowId is null ? "null" : senderWindowId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var targetJson = targetWindowId is null ? "null" : targetWindowId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // 待派发的事件载荷（与内联/引用两种投递形式共用同一份字节）。
        var payloadJson = $"{{\"name\":{nameJson},\"data\":{dataJson},\"senderWindowId\":{senderJson},\"targetWindowId\":{targetJson}}}";
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        var oversized = payloadBytes.Length > EventPayloadStore.MaxInlinePayloadBytes;

        // 对应 Wails v3 EventIPCTransport.DispatchWailsEvent：
        //   for _, window := range windows {
        //       if event.IsCancelled() { return }
        //       window.DispatchWailsEvent(event)
        //   }
        // OPT-EVENT-002：定向事件仅注入目标窗口，广播则遍历所有窗口。
        // 事件投递硬化 P0-1（对应 Wails v3 PR #5934）：注入统一进入目标窗口的有序队列，
        // 由每窗口单消费者按 FIFO 串行执行 ExecJS。
        // 事件投递硬化 P0-1 / Layer 3（对应 PR #5930）：超过 8192 字节的载荷停放宿主侧，
        // 注入一段只做 fetch 的脚本，避免大载荷经 eval 触发 WebKit 共享内存滞留。
        // 特大载荷需要宿主侧存储；一次性解析，避免每个窗口重复取全局应用。
        var payloadStore = oversized ? Application.Get()?.EventPayloads : null;

        foreach (var window in windows)
        {
            // 定向过滤：目标窗口 ID 非空时仅派发到该窗口
            if (targetWindowId is not null && window.ID != targetWindowId.Value)
            {
                continue;
            }

            var target = window;
            string js;
            if (payloadStore is not null && payloadStore.TryPut(target.ID, payloadBytes, out var payloadId))
            {
                js = BuildRefEventScript(payloadId);
            }
            else
            {
                // 载荷不大，或存储已满/已关闭：回退内联投递（代价是恢复共享内存滞留，但优于丢弃）。
                js = BuildInlineEventScript(payloadJson);
            }

            // WebviewWindow.ExecJS 内部会调用平台实现的 ExecuteScriptAsync。
            // 投递失败（窗口已关闭、已释放）由队列消费者捕获并记录日志，不影响其他窗口。
            _eventQueue.Enqueue(target.ID, () =>
            {
                target.ExecJS(js);
                return Task.CompletedTask;
            });
        }
    }

    /// <summary>
    /// 构造内联投递脚本：直接调用 <c>window._wailsEmitEvent</c> 派发载荷。
    /// <para>
    /// 若该窗口已有一次引用投递（<c>fetch</c>）在进行中，则本次派发排队其后再执行，
    /// 避免内联事件越过尚未取回的引用事件（保持每窗口到达顺序）。
    /// 派发异常被 <c>catch</c> 吞掉，避免单个抛异常的监听器让队列 Promise 永久 rejected。
    /// </para>
    /// </summary>
    /// <param name="payloadJson">事件载荷 JSON（<c>{name,data,senderWindowId,targetWindowId}</c>）。</param>
    private static string BuildInlineEventScript(string payloadJson)
        => "if(window._wailsEmitEvent){var g=window,e=" + payloadJson + ";" +
           "var c=function(){g._wailsEmitEvent(e.name,e.data,e.senderWindowId,e.targetWindowId);};" +
           "if(g.__wailsEventQueue){g.__wailsEventQueue=g.__wailsEventQueue.then(c).catch(function(){});}else{c();}}";

    /// <summary>
    /// 构造引用投递脚本：从 <c>GET /wails/eventpayload/&lt;id&gt;</c> 取回停放载荷后再派发。
    /// <para>
    /// 建立 <c>window.__wailsEventQueue</c> 是让该窗口后续事件排队其后的关键。
    /// </para>
    /// </summary>
    /// <param name="payloadId">停放载荷的 id。</param>
    private static string BuildRefEventScript(string payloadId)
        => "if(window._wailsEmitEvent){var g=window;" +
           "g.__wailsEventQueue=(g.__wailsEventQueue||Promise.resolve()).then(function(){" +
           "return fetch('" + EventPayloadStore.PathPrefix + payloadId + "',{cache:'no-store'})" +
           ".then(function(r){return r.json();})" +
           ".then(function(e){g._wailsEmitEvent(e.name,e.data,e.senderWindowId,e.targetWindowId);});" +
           "}).catch(function(){});}";
}
