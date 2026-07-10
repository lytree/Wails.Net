using Wails.Net.Application.Bindings;

namespace Wails.Net.Application.Events;

/// <summary>
/// 泛型类型安全事件，对应 Wails v3 中的 typed event。
/// 提供编译时类型检查的事件注册和发布。
/// </summary>
/// <typeparam name="TData">事件数据类型。</typeparam>
public class TypedEvent<TData>
{
    /// <summary>
    /// 事件名称。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 关联的事件处理器。
    /// </summary>
    private readonly EventProcessor _processor;

    /// <summary>
    /// 使用指定名称和事件处理器构造 TypedEvent 实例。
    /// </summary>
    /// <param name="name">事件名称。</param>
    /// <param name="processor">关联的事件处理器。</param>
    public TypedEvent(string name, EventProcessor processor)
    {
        Name = name;
        _processor = processor;
    }

    /// <summary>
    /// 订阅事件，回调将收到类型安全的数据。
    /// </summary>
    /// <param name="callback">事件回调。</param>
    /// <returns>监听器 ID。</returns>
    public int On(Action<TData?> callback)
    {
        return _processor.On(Name, evt =>
        {
            var data = ConvertData(evt.Data);
            callback(data);
        });
    }

    /// <summary>
    /// 订阅事件一次。
    /// </summary>
    /// <param name="callback">事件回调。</param>
    /// <returns>监听器 ID。</returns>
    public int Once(Action<TData?> callback)
    {
        return _processor.Once(Name, evt =>
        {
            var data = ConvertData(evt.Data);
            callback(data);
        });
    }

    /// <summary>
    /// 发布类型安全的事件。
    /// </summary>
    /// <param name="data">事件数据。</param>
    /// <param name="senderWindowID">发送窗口 ID。</param>
    public void Emit(TData? data, uint? senderWindowID = null)
    {
        _processor.Emit(Name, data, senderWindowID);
    }

    /// <summary>
    /// 取消所有订阅。
    /// </summary>
    public void Off()
    {
        _processor.Off(Name);
    }

    /// <summary>
    /// 将事件数据转换为目标类型。
    /// </summary>
    /// <param name="data">原始数据。</param>
    /// <returns>转换后的数据。</returns>
    private static TData? ConvertData(object? data)
    {
        if (data is null)
        {
            return default;
        }

        if (data is TData typed)
        {
            return typed;
        }

        // 尝试通过 JSON 进行类型转换
        var json = System.Text.Json.JsonSerializer.Serialize(data, JsonOptions.DefaultSerializerOptions);
        return System.Text.Json.JsonSerializer.Deserialize<TData>(json, JsonOptions.DefaultSerializerOptions);
    }
}

/// <summary>
/// 事件钩子，用于在事件发布前进行拦截。
/// 对应 Wails v3 中的 pre-emit hooks。
/// </summary>
public class EventHook
{
    /// <summary>
    /// 关联的事件处理器。
    /// </summary>
    private readonly EventProcessor _processor;

    /// <summary>
    /// 钩子函数。
    /// </summary>
    private readonly Func<CustomEvent, bool> _hook;

    /// <summary>
    /// 构造 EventHook 实例并注册到事件处理器。
    /// </summary>
    /// <param name="processor">事件处理器。</param>
    /// <param name="hook">钩子函数，返回 false 取消事件。</param>
    public EventHook(EventProcessor processor, Func<CustomEvent, bool> hook)
    {
        _processor = processor;
        _hook = hook;
        processor.RegisterHook(hook);
    }

    /// <summary>
    /// 创建并注册一个钩子，当指定事件名匹配时执行条件判断。
    /// </summary>
    /// <param name="processor">事件处理器。</param>
    /// <param name="eventName">要拦截的事件名。</param>
    /// <param name="condition">条件函数，返回 false 取消事件。</param>
    /// <returns>EventHook 实例。</returns>
    public static EventHook ForEvent(EventProcessor processor, string eventName, Func<CustomEvent, bool> condition)
    {
        return new EventHook(processor, evt =>
        {
            if (evt.Name != eventName)
            {
                return true; // 不匹配的事件放行
            }
            return condition(evt);
        });
    }
}
