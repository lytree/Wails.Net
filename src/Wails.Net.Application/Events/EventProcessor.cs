using System.Collections.Concurrent;
using Wails.Net.Application.Transport;

namespace Wails.Net.Application.Events;

/// <summary>
/// 事件处理器，管理事件订阅、发布和取消。
/// 对应 Wails v3 Go 版本 events.go 中的 EventProcessor。
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
    /// 用于事件广播到传输层的监听器列表（如 IPC、WebSocket、EventIPC 兜底）。
    /// 对应 Wails v3 Go 版本 application.go 中的 <c>wailsEventListeners []WailsEventListener</c> 字段。
    /// <para>
    /// 列表设计支持多种传输层并行广播事件：例如 HttpTransport 推送给 WebSocket 客户端的同时，
    /// EventIPCTransport 通过 ExecJS 注入到桌面端 webview。
    /// </para>
    /// </summary>
    private readonly List<IWailsEventListener> _wailsEventListeners = new();

    /// <summary>
    /// 保护 <see cref="_wailsEventListeners"/> 的锁。
    /// </summary>
    private readonly object _listenersLock = new();

    /// <summary>
    /// 用于生成唯一监听器 ID 的计数器。
    /// </summary>
    private int _nextListenerId = 1;

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
        _hooks.Add(hook);
    }

    /// <summary>
    /// 订阅事件，每次发布都会触发回调。
    /// </summary>
    /// <param name="name">事件名称。</param>
    /// <param name="callback">事件回调。</param>
    /// <returns>监听器 ID，可用于取消订阅。</returns>
    public int On(string name, Action<CustomEvent> callback)
    {
        return OnMultiple(name, callback, -1);
    }

    /// <summary>
    /// 订阅事件，最多触发指定次数后自动取消。
    /// </summary>
    /// <param name="name">事件名称。</param>
    /// <param name="callback">事件回调。</param>
    /// <param name="maxCalls">最大触发次数，-1 表示无限制。</param>
    /// <returns>监听器 ID，可用于取消订阅。</returns>
    public int OnMultiple(string name, Action<CustomEvent> callback, int maxCalls)
    {
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
    /// </summary>
    /// <param name="name">事件名称。</param>
    /// <param name="callback">事件回调。</param>
    /// <returns>监听器 ID，可用于取消订阅。</returns>
    public int Once(string name, Action<CustomEvent> callback)
    {
        return OnMultiple(name, callback, 1);
    }

    /// <summary>
    /// 发布事件到所有订阅者。
    /// </summary>
    /// <param name="name">事件名称。</param>
    /// <param name="data">事件数据。</param>
    /// <param name="senderWindowID">发送窗口 ID，可为 null。</param>
    public void Emit(string name, object? data = null, uint? senderWindowID = null)
    {
        var evt = new CustomEvent(name, data, senderWindowID);

        // 执行 pre-emit 钩子
        foreach (var hook in _hooks)
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

        // 通知传输层监听器（广播到前端）。
        // 对应 Wails v3 Go 版本 application.go 中的并行 goroutine 派发：
        //   go dispatchEventToListeners(event)
        //   go dispatchEventToWindows(event)
        // 此处采用同步遍历保持简单，监听器实现应自行处理线程安全与异步派发。
        // 拷贝一份列表避免在锁外枚举时被并发修改。
        List<IWailsEventListener> snapshot;
        lock (_listenersLock)
        {
            if (_wailsEventListeners.Count == 0)
            {
                snapshot = null!;
            }
            else
            {
                snapshot = new List<IWailsEventListener>(_wailsEventListeners);
            }
        }

        if (snapshot is not null)
        {
            foreach (var listener in snapshot)
            {
                try
                {
                    listener.NotifyEvent(name, data);
                }
                catch
                {
                    // 单个监听器异常不应影响其他监听器或本地订阅者。
                    // 对应 Wails v3 Go 版本 dispatchEventToWindows 中每个窗口独立 dispatch 的容错语义。
                }
            }
        }

        // 通知本地订阅者
        if (_listeners.TryGetValue(name, out var listeners))
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
    /// </summary>
    /// <param name="name">事件名称。</param>
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
    /// 获取指定事件的订阅者数量。
    /// </summary>
    /// <param name="name">事件名称。</param>
    /// <returns>订阅者数量。</returns>
    public int ListenerCount(string name)
    {
        return _listeners.TryGetValue(name, out var list) ? list.Count : 0;
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
