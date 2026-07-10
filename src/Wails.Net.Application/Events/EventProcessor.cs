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
    /// 用于事件广播到传输层的监听器接口（如 IPC、WebSocket）。
    /// </summary>
    private IWailsEventListener? _wailsEventListener;

    /// <summary>
    /// 用于生成唯一监听器 ID 的计数器。
    /// </summary>
    private int _nextListenerId = 1;

    /// <summary>
    /// 设置传输层事件监听器，用于将事件广播到前端。
    /// </summary>
    /// <param name="listener">传输层事件监听器。</param>
    public void SetWailsEventListener(IWailsEventListener listener)
    {
        _wailsEventListener = listener;
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

        // 通知传输层监听器（广播到前端）
        _wailsEventListener?.NotifyEvent(name, data);

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
