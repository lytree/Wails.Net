using System.Collections.Concurrent;
using Wails.Net.Application.Transport;
using Wails.Net.Events;

namespace Wails.Net.Application.Events;

/// <summary>
/// 事件处理器，管理事件订阅、发布和取消。
/// 对应 Wails v3 Go 版本 events.go 中的 EventProcessor 和 event_manager.go 中的 EventManager。
/// <para>
/// 同时承载两类事件：
/// <list type="bullet">
/// <item>自定义事件（CustomEvent）：用户代码通过 <see cref="Emit"/> 发布，前端通过 wails.Events.On 订阅。</item>
/// <item>应用事件（ApplicationEvent）：平台触发（窗口焦点/系统主题/电池网络等），通过
/// <see cref="OnApplicationEvent"/> 订阅、<see cref="EmitApplicationEvent"/> 分发。</item>
/// </list>
/// </para>
/// </summary>
public class EventProcessor
{
    /// <summary>
    /// 事件监听器记录：事件名 → 监听器列表。
    /// </summary>
    private readonly ConcurrentDictionary<string, List<EventListener>> _listeners = new();

    /// <summary>
    /// Pre-emit 钩子列表，可在事件发布前拦截或取消事件。
    /// </summary>
    private readonly List<Func<CustomEvent, bool>> _hooks = new();

    /// <summary>
    /// 保护 <see cref="_hooks"/> 的锁。
    /// 对应 Wails v3.1.0 #6276：发布事件时必须在锁内快照钩子列表，
    /// 否则并发 <see cref="RegisterHook"/> 会与 <see cref="EmitCore"/> 的遍历冲突。
    /// </summary>
    private readonly object _hooksLock = new();

    /// <summary>
    /// 用于事件广播到传输层的监听器列表（如 IPC、WebSocket、EventIPC 兜底）。
    /// 对应 Wails v3 Go 版本 application.go 中的 <c>wailsEventListeners []WailsEventListener</c> 字段。
    /// <para>
    /// 列表设计支持多种传输层并行广播事件：例如 HttpTransport 推送给 WebSocket 客户端的同时，
    /// EventIPCTransport 通过 ExecJS 注入到桌面端 webview。
    /// </para>
    /// </summary>
    private readonly List<IWailsEventListener> _wailsEventListeners = new();

    /// <summary>
    /// 应用事件监听器：事件 ID → 监听器回调列表。
    /// 对应 Wails v3 Go 版本 application.go 中的 <c>applicationEventListeners map[uint][]*EventListener</c>。
    /// </summary>
    private readonly ConcurrentDictionary<uint, List<Action<ApplicationEvent>>> _applicationEventListeners = new();

    /// <summary>
    /// 应用事件钩子：事件 ID → 钩子回调列表。
    /// 对应 Wails v3 Go 版本 application.go 中的 <c>applicationEventHooks map[uint][]*eventHook</c>。
    /// 钩子在监听器执行前调用，可通过 <see cref="ApplicationEvent.Cancel"/> 取消事件。
    /// </summary>
    private readonly ConcurrentDictionary<uint, List<Action<ApplicationEvent>>> _applicationEventHooks = new();

    /// <summary>
    /// 保护 <see cref="_wailsEventListeners"/> 的锁。
    /// </summary>
    private readonly object _listenersLock = new();

    /// <summary>
    /// 用于生成唯一监听器 ID 的计数器。
    /// </summary>
    private int _nextListenerId = 1;

    /// <summary>
    /// 前端事件投递邮箱（事件投递硬化 P0-1，对应 Wails v3 的
    /// <c>EventProcessor.frontendEvents</c>，见 PR #5851）。
    /// <para>
    /// 前端派发统一经由本邮箱串行执行，使到达前端的顺序等于 <see cref="Emit"/> 的调用顺序；
    /// 同时保证下游（<see cref="Transport.PerWindowEventQueue"/>）的阻塞式背压只落在
    /// 邮箱排空任务上，应用侧调用 <see cref="Emit"/> 永不阻塞（含 UI 线程）。
    /// </para>
    /// </summary>
    private readonly EventMailbox<FrontendEventDelivery> _frontendMailbox;

    /// <summary>
    /// 构造事件处理器。
    /// </summary>
    public EventProcessor()
    {
        _frontendMailbox = new EventMailbox<FrontendEventDelivery>(DeliverFrontendEventAsync);
    }

    /// <summary>
    /// 前端事件投递载荷：一次 <see cref="Emit"/> 对应的前端派发请求。
    /// </summary>
    /// <param name="Name">事件名称。</param>
    /// <param name="Data">事件数据。</param>
    /// <param name="SenderWindowId">事件来源窗口 ID。</param>
    /// <param name="TargetWindowId">定向目标窗口 ID。</param>
    private readonly record struct FrontendEventDelivery(
        string Name,
        object? Data,
        uint? SenderWindowId,
        uint? TargetWindowId);

    /// <summary>
    /// 邮箱单消费者：按 FIFO 顺序把事件串行派发给传输层监听器。
    /// 监听器列表在派发时快照，避免长时间持锁与并发修改冲突。
    /// </summary>
    /// <param name="delivery">投递载荷。</param>
    private Task DeliverFrontendEventAsync(FrontendEventDelivery delivery)
    {
        List<IWailsEventListener>? snapshot;
        lock (_listenersLock)
        {
            snapshot = _wailsEventListeners.Count == 0
                ? null
                : new List<IWailsEventListener>(_wailsEventListeners);
        }

        if (snapshot is null)
        {
            return Task.CompletedTask;
        }

        foreach (var listener in snapshot)
        {
            try
            {
                // P1-2：将 senderWindowID 传递给传输层监听器，使前端可识别事件来源窗口。
                // OPT-EVENT-002：定向事件透传 targetWindowID，传输层按目标过滤。
                listener.NotifyEvent(delivery.Name, delivery.Data, delivery.SenderWindowId, delivery.TargetWindowId);
            }
            catch
            {
                // 单个监听器异常不应影响其他监听器。
                // 对应 Wails v3 Go 版本 dispatchEventToWindows 中每个窗口独立 dispatch 的容错语义。
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 设置传输层事件监听器，用于将事件广播到前端。
    /// <para>
    /// <b>兼容性说明</b>：此方法清除已有监听器列表后只保留单个监听器。
    /// 推荐使用 <see cref="AddWailsEventListener"/> 追加监听器以支持多传输层并行广播。
    /// </para>
    /// </summary>
    /// <param name="listener">传输层事件监听器。</param>
    public void SetWailsEventListener(IWailsEventListener listener)
    {
        lock (_listenersLock)
        {
            _wailsEventListeners.Clear();
            _wailsEventListeners.Add(listener);
        }
    }

    /// <summary>
    /// 追加传输层事件监听器，支持多传输层并行广播事件。
    /// 对应 Wails v3 Go 版本中 <c>App.wailsEventListeners = append(..., listener)</c> 的追加语义。
    /// <para>
    /// 典型用法：主 Transport（HttpTransport/WebSocketTransport）+ EventIPCTransport 兜底同时追加，
    /// 确保事件可通过 WebSocket 推送到远程客户端的同时，也通过 ExecJS 注入到桌面端 webview。
    /// </para>
    /// </summary>
    /// <param name="listener">要追加的传输层事件监听器。</param>
    public void AddWailsEventListener(IWailsEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_listenersLock)
        {
            _wailsEventListeners.Add(listener);
        }
    }

    /// <summary>
    /// 移除指定的传输层事件监听器。
    /// </summary>
    /// <param name="listener">要移除的传输层事件监听器。</param>
    /// <returns>成功移除返回 true，否则返回 false。</returns>
    public bool RemoveWailsEventListener(IWailsEventListener listener)
    {
        lock (_listenersLock)
        {
            return _wailsEventListeners.Remove(listener);
        }
    }

    /// <summary>
    /// 清除所有传输层事件监听器。
    /// </summary>
    public void ClearWailsEventListeners()
    {
        lock (_listenersLock)
        {
            _wailsEventListeners.Clear();
        }
    }

    /// <summary>
    /// 注册 pre-emit 钩子，可在事件发布前拦截或取消事件。
    /// 钩子返回 false 则取消事件。
    /// </summary>
    /// <param name="hook">钩子函数，接收 CustomEvent，返回 false 取消事件。</param>
    public void RegisterHook(Func<CustomEvent, bool> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        lock (_hooksLock)
        {
            _hooks.Add(hook);
        }
    }

    /// <summary>
    /// 订阅事件，每次发布都会触发回调。
    /// <para>
    /// OPT-EVENT-001：支持通配符模式订阅（<c>*</c> 后缀前缀匹配，与 Wails v3 语义一致），
    /// 例如 <c>On("wails:window:*", cb)</c> 可收到所有窗口相关事件。
    /// </para>
    /// </summary>
    /// <param name="name">事件名称或通配符模式（如 <c>wails:window:*</c>）。</param>
    /// <param name="callback">事件回调。</param>
    /// <returns>监听器 ID，可用于取消订阅。</returns>
    public int On(string name, Action<CustomEvent> callback)
    {
        return OnMultiple(name, callback, -1);
    }

    /// <summary>
    /// 订阅事件，最多触发指定次数后自动取消。
    /// <para>
    /// OPT-EVENT-001：支持通配符模式订阅（<c>*</c> 后缀前缀匹配）。
    /// </para>
    /// </summary>
    /// <param name="name">事件名称或通配符模式（如 <c>wails:window:*</c>）。</param>
    /// <param name="callback">事件回调。</param>
    /// <param name="maxCalls">最大触发次数，-1 表示无限制。</param>
    /// <returns>监听器 ID，可用于取消订阅。</returns>
    public int OnMultiple(string name, Action<CustomEvent> callback, int maxCalls)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var id = Interlocked.Increment(ref _nextListenerId);
        var listener = new EventListener(id, name, callback, maxCalls);

        _listeners.AddOrUpdate(name, [listener], (_, list) =>
        {
            lock (list)
            {
                list.Add(listener);
            }
            return list;
        });

        return id;
    }

    /// <summary>
    /// 订阅事件一次，触发后自动取消。
    /// <para>
    /// OPT-EVENT-001：支持通配符模式订阅（<c>*</c> 后缀前缀匹配）。
    /// </para>
    /// </summary>
    /// <param name="name">事件名称或通配符模式（如 <c>wails:window:*</c>）。</param>
    /// <param name="callback">事件回调。</param>
    /// <returns>监听器 ID，可用于取消订阅。</returns>
    public int Once(string name, Action<CustomEvent> callback)
    {
        return OnMultiple(name, callback, 1);
    }

    /// <summary>
    /// 判断事件名是否为通配符模式（以 <c>*</c> 结尾，OPT-EVENT-001）。
    /// 匹配规则：事件名以模式去除尾部 <c>*</c> 后的前缀开头。
    /// </summary>
    /// <param name="name">订阅时使用的名称。</param>
    /// <returns>是通配符模式返回 true。</returns>
    public static bool IsWildcardPattern(string name)
        => !string.IsNullOrEmpty(name) && name[^1] == '*';

    /// <summary>
    /// 判断事件名是否匹配指定订阅模式（精确匹配或通配符前缀匹配）。
    /// </summary>
    /// <param name="eventName">实际发布的事件名。</param>
    /// <param name="pattern">订阅时使用的名称（可为通配符模式）。</param>
    /// <returns>匹配返回 true。</returns>
    public static bool MatchesPattern(string eventName, string pattern)
    {
        if (string.Equals(eventName, pattern, StringComparison.Ordinal))
        {
            return true;
        }

        return IsWildcardPattern(pattern) &&
               eventName.StartsWith(pattern[..^1], StringComparison.Ordinal);
    }

    /// <summary>
    /// 发布事件到所有订阅者（广播）。
    /// </summary>
    /// <param name="name">事件名称。</param>
    /// <param name="data">事件数据。</param>
    /// <param name="senderWindowID">发送窗口 ID，可为 null。</param>
    public void Emit(string name, object? data = null, uint? senderWindowID = null)
    {
        EmitCore(new CustomEvent(name, data, senderWindowID));
    }

    /// <summary>
    /// 发布定向事件到指定窗口（OPT-EVENT-002，2026-08-15 新增）。
    /// <para>
    /// 与 <see cref="Emit(string, object?, uint?)"/> 的区别：事件仅派发到
    /// <paramref name="targetWindowID"/> 对应的窗口前端，其他窗口不收到；
    /// 本地订阅者（后端 <see cref="On(string, Action{CustomEvent})"/>）不受定向限制，
    /// 仍会收到事件（可在回调中通过 <see cref="CustomEvent.TargetWindowID"/> 判断）。
    /// </para>
    /// </summary>
    /// <param name="targetWindowID">目标窗口 ID。</param>
    /// <param name="name">事件名称。</param>
    /// <param name="data">事件数据。</param>
    /// <param name="senderWindowID">发送窗口 ID，可为 null。</param>
    public void EmitTo(uint targetWindowID, string name, object? data = null, uint? senderWindowID = null)
    {
        EmitCore(new CustomEvent(name, data, senderWindowID, targetWindowID));
    }

    /// <summary>
    /// 核心事件派发逻辑：pre-emit 钩子 → 传输层广播（含定向过滤）→ 本地订阅者（精确 + 通配符）。
    /// </summary>
    /// <param name="evt">事件实例。</param>
    private void EmitCore(CustomEvent evt)
    {
        // 执行 pre-emit 钩子：先在锁内快照，避免并发 RegisterHook 修改列表导致
        // 遍历抛出 InvalidOperationException（Wails v3.1.0 #6276）。
        Func<CustomEvent, bool>[] hooks;
        lock (_hooksLock)
        {
            hooks = _hooks.ToArray();
        }

        foreach (var hook in hooks)
        {
            if (!hook(evt))
            {
                return; // 钩子取消了事件
            }
        }

        // 若事件已被取消则直接返回
        if (evt.IsCancelled)
        {
            return;
        }

        // 通知传输层监听器（广播到前端）：入队到前端邮箱，由单消费者按 FIFO 顺序串行派发。
        // 事件投递硬化 P0-1（对应 Wails v3 PR #5851）：
        //   - 并发 Emit 之间不再交错，到达前端的顺序等于 Emit 调用顺序；
        //   - Send 永不阻塞，UI 线程调用 Emit 不会被下游背压拖住。
        // 监听器列表快照在邮箱消费侧完成（见 DeliverFrontendEventAsync）。
        _frontendMailbox.Send(new FrontendEventDelivery(
            evt.Name, evt.Data, evt.SenderWindowID, evt.TargetWindowID));

        // 通知本地订阅者：先精确匹配，再匹配通配符模式（OPT-EVENT-001）。
        // 定向事件（TargetWindowID 非空）不影响本地订阅者——本地订阅者始终收到事件，
        // 可在回调中通过 evt.TargetWindowID 判断事件是否为定向派发。
        if (_listeners.TryGetValue(evt.Name, out var listeners))
        {
            InvokeLocalListeners(evt, listeners);
        }

        // 通配符模式匹配：遍历所有以 * 结尾的模式键，前缀匹配当前事件名。
        // 事件名数量通常较小（数十级），O(n) 遍历可接受；且 ConcurrentDictionary
        // 的 Keys 枚举是快照安全的（读取操作并发安全）。
        foreach (var pair in _listeners)
        {
            if (pair.Key == evt.Name || !IsWildcardPattern(pair.Key))
            {
                continue;
            }

            if (evt.Name.StartsWith(pair.Key[..^1], StringComparison.Ordinal))
            {
                InvokeLocalListeners(evt, pair.Value);
            }
        }
    }

    /// <summary>
    /// 调用一组本地监听器，处理过期监听器清理。
    /// </summary>
    /// <param name="evt">事件实例。</param>
    /// <param name="listeners">监听器列表。</param>
    private static void InvokeLocalListeners(CustomEvent evt, List<EventListener> listeners)
    {
        List<EventListener>? toRemove = null;

        lock (listeners)
        {
            foreach (var listener in listeners)
            {
                if (evt.IsCancelled)
                {
                    break;
                }

                listener.Invoke(evt);

                // 检查是否达到最大调用次数
                if (listener.IsExpired)
                {
                    (toRemove ??= []).Add(listener);
                }
            }

            // 移除已过期的监听器
            if (toRemove is not null)
            {
                foreach (var item in toRemove)
                {
                    listeners.Remove(item);
                }
            }
        }
    }

    /// <summary>
    /// 按 ID 取消订阅。
    /// </summary>
    /// <param name="listenerId">监听器 ID。</param>
    public void Off(int listenerId)
    {
        foreach (var kvp in _listeners)
        {
            lock (kvp.Value)
            {
                var index = kvp.Value.FindIndex(l => l.ID == listenerId);
                if (index >= 0)
                {
                    kvp.Value.RemoveAt(index);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 按事件名取消所有订阅。
    /// <para>
    /// 传入精确事件名时移除该事件的订阅；传入通配符模式名（如 <c>wails:window:*</c>）时
    /// 移除该模式的订阅（OPT-EVENT-001）。不会移除其他不相关的订阅。
    /// </para>
    /// </summary>
    /// <param name="name">事件名称或通配符模式。</param>
    public void Off(string name)
    {
        _listeners.TryRemove(name, out _);
    }

    /// <summary>
    /// 清除所有事件订阅。
    /// </summary>
    public void Clear()
    {
        _listeners.Clear();
    }

    /// <summary>
    /// 清除所有自定义事件订阅。
    /// 对应 Wails v3 Go 版本 event_manager.go 中的 <c>EventManager.Reset</c>，
    /// 内部调用 <c>EventProcessor.OffAll</c>。
    /// <para>
    /// 此方法是 <see cref="Clear"/> 的语义别名，仅为对齐 Wails v3 公共 API 命名而提供。
    /// 应用事件监听器（<see cref="OnApplicationEvent"/>）不受影响。
    /// </para>
    /// </summary>
    public void Reset()
    {
        _listeners.Clear();
    }

    /// <summary>
    /// 订阅应用程序级别事件，返回取消订阅的回调。
    /// 对应 Wails v3 Go 版本 event_manager.go 中的 <c>EventManager.OnApplicationEvent</c>。
    /// <para>
    /// 与自定义事件的 <see cref="On"/> 不同，应用事件由平台触发（窗口焦点/系统主题/电池网络等），
    /// 监听器在调用前会先执行同事件的钩子（<see cref="RegisterApplicationEventHook"/>），
    /// 钩子可通过 <see cref="ApplicationEvent.Cancel"/> 取消事件以阻止后续监听器执行。
    /// </para>
    /// </summary>
    /// <param name="eventType">应用事件类型。</param>
    /// <param name="callback">事件回调。</param>
    /// <returns>取消订阅回调，调用后移除该监听器。</returns>
    public Action OnApplicationEvent(ApplicationEventType eventType, Action<ApplicationEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var id = (uint)eventType;

        var list = _applicationEventListeners.GetOrAdd(id, _ => new List<Action<ApplicationEvent>>());
        lock (list)
        {
            list.Add(callback);
        }

        return () =>
        {
            lock (list)
            {
                list.Remove(callback);
            }
        };
    }

    /// <summary>
    /// 注册应用程序级别事件钩子，返回取消注册的回调。
    /// 对应 Wails v3 Go 版本 event_manager.go 中的 <c>EventManager.RegisterApplicationEventHook</c>。
    /// <para>
    /// 钩子在监听器执行前按注册顺序调用，可通过 <see cref="ApplicationEvent.Cancel"/> 取消事件
    /// 以阻止后续钩子和监听器执行。
    /// </para>
    /// </summary>
    /// <param name="eventType">应用事件类型。</param>
    /// <param name="callback">钩子回调。</param>
    /// <returns>取消注册回调，调用后移除该钩子。</returns>
    public Action RegisterApplicationEventHook(ApplicationEventType eventType, Action<ApplicationEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var id = (uint)eventType;

        var list = _applicationEventHooks.GetOrAdd(id, _ => new List<Action<ApplicationEvent>>());
        lock (list)
        {
            list.Add(callback);
        }

        return () =>
        {
            lock (list)
            {
                list.Remove(callback);
            }
        };
    }

    /// <summary>
    /// 分发应用程序级别事件到所有订阅者和钩子。
    /// 对应 Wails v3 Go 版本 event_manager.go 中的 <c>handleApplicationEvent</c>。
    /// <para>
    /// 执行顺序：先按注册顺序调用所有钩子，任一钩子取消事件则立即停止；
    /// 否则继续按注册顺序调用所有监听器。单个监听器抛出的异常被吞下，不影响其他监听器。
    /// </para>
    /// </summary>
    /// <param name="eventType">应用事件类型。</param>
    /// <param name="data">事件数据，可为 null。</param>
    /// <param name="windowId">触发事件的窗口 ID，可为 null。</param>
    /// <returns>事件是否被钩子取消。</returns>
    public bool EmitApplicationEvent(ApplicationEventType eventType, object? data = null, uint? windowId = null)
    {
        var evt = new ApplicationEvent((uint)eventType, data, windowId);

        // 先执行钩子；任一钩子取消事件则停止。
        if (_applicationEventHooks.TryGetValue(evt.Id, out var hooks))
        {
            List<Action<ApplicationEvent>> hooksSnapshot;
            lock (hooks)
            {
                hooksSnapshot = new List<Action<ApplicationEvent>>(hooks);
            }

            foreach (var hook in hooksSnapshot)
            {
                try
                {
                    hook(evt);
                }
                catch
                {
                    // 单个钩子异常不影响其他钩子，对应 Wails v3 handleApplicationEvent 的 handlePanic 容错。
                }

                if (evt.IsCancelled)
                {
                    return true;
                }
            }
        }

        // 再执行监听器。
        if (_applicationEventListeners.TryGetValue(evt.Id, out var listeners))
        {
            List<Action<ApplicationEvent>> listenersSnapshot;
            lock (listeners)
            {
                listenersSnapshot = new List<Action<ApplicationEvent>>(listeners);
            }

            foreach (var listener in listenersSnapshot)
            {
                if (evt.IsCancelled)
                {
                    break;
                }

                try
                {
                    listener(evt);
                }
                catch
                {
                    // 单个监听器异常不影响其他监听器，对应 Wails v3 handleApplicationEvent 的 handlePanic 容错。
                }
            }
        }

        return evt.IsCancelled;
    }

    /// <summary>
    /// 获取指定应用事件类型的订阅者数量。
    /// </summary>
    /// <param name="eventType">应用事件类型。</param>
    /// <returns>订阅者数量。</returns>
    public int ApplicationEventListenerCount(ApplicationEventType eventType)
    {
        return _applicationEventListeners.TryGetValue((uint)eventType, out var list) ? list.Count : 0;
    }

    /// <summary>
    /// 获取指定应用事件类型的钩子数量。
    /// </summary>
    /// <param name="eventType">应用事件类型。</param>
    /// <returns>钩子数量。</returns>
    public int ApplicationEventHookCount(ApplicationEventType eventType)
    {
        return _applicationEventHooks.TryGetValue((uint)eventType, out var list) ? list.Count : 0;
    }

    /// <summary>
    /// 清除所有应用事件订阅和钩子。
    /// </summary>
    public void ClearApplicationEvents()
    {
        _applicationEventListeners.Clear();
        _applicationEventHooks.Clear();
    }

    /// <summary>
    /// 获取指定事件的订阅者数量。
    /// <para>
    /// OPT-EVENT-001：包含精确订阅与通配符模式（前缀匹配）的订阅者总数。
    /// </para>
    /// </summary>
    /// <param name="name">事件名称。</param>
    /// <returns>订阅者数量。</returns>
    public int ListenerCount(string name)
    {
        var count = _listeners.TryGetValue(name, out var list) ? list.Count : 0;

        foreach (var pair in _listeners)
        {
            if (pair.Key == name || !IsWildcardPattern(pair.Key))
            {
                continue;
            }

            if (name.StartsWith(pair.Key[..^1], StringComparison.Ordinal))
            {
                count += pair.Value.Count;
            }
        }

        return count;
    }

    /// <summary>
    /// 内部事件监听器记录。
    /// </summary>
    private sealed class EventListener
    {
        /// <summary>
        /// 监听器唯一 ID。
        /// </summary>
        public int ID { get; }

        /// <summary>
        /// 事件名称。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 事件回调。
        /// </summary>
        private readonly Action<CustomEvent> _callback;

        /// <summary>
        /// 最大调用次数，-1 表示无限制。
        /// </summary>
        private readonly int _maxCalls;

        /// <summary>
        /// 已调用次数。
        /// </summary>
        private int _callCount;

        /// <summary>
        /// 是否已过期（达到最大调用次数）。
        /// </summary>
        public bool IsExpired => _maxCalls > 0 && _callCount >= _maxCalls;

        /// <summary>
        /// 构造 EventListener 实例。
        /// </summary>
        /// <param name="id">监听器 ID。</param>
        /// <param name="name">事件名称。</param>
        /// <param name="callback">事件回调。</param>
        /// <param name="maxCalls">最大调用次数。</param>
        public EventListener(int id, string name, Action<CustomEvent> callback, int maxCalls)
        {
            ID = id;
            Name = name;
            _callback = callback;
            _maxCalls = maxCalls;
        }

        /// <summary>
        /// 触发事件回调。
        /// </summary>
        /// <param name="evt">事件实例。</param>
        public void Invoke(CustomEvent evt)
        {
            _callback(evt);
            Interlocked.Increment(ref _callCount);
        }
    }
}
