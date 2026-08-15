namespace Wails.Net.Application.Events;

/// <summary>
/// 表示一个自定义事件，包含事件名、数据和来源窗口。
/// 对应 Wails v3 Go 版本 events.go 中的 CustomEvent 结构。
/// </summary>
public class CustomEvent
{
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 事件名称。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 事件数据，可为 null。
    /// </summary>
    public object? Data { get; }

    /// <summary>
    /// 发送事件的窗口 ID，若为应用级事件则为 null。
    /// </summary>
    public uint? SenderWindowID { get; }

    /// <summary>
    /// 定向目标窗口 ID（OPT-EVENT-002，2026-08-15 新增）。
    /// <para>
    /// 非 null 表示跨窗口定向事件：仅派发到指定窗口的前端，其他窗口不收到。
    /// null 表示广播到所有窗口（默认行为，与 Wails v3 Emit 语义一致）。
    /// </para>
    /// </summary>
    public uint? TargetWindowID { get; }

    /// <summary>
    /// 用于事件取消的 CancellationToken。
    /// </summary>
    public CancellationToken CancellationToken => _cts.Token;

    /// <summary>
    /// 事件是否已被取消。
    /// </summary>
    public bool IsCancelled => _cts.IsCancellationRequested;

    /// <summary>
    /// 使用指定名称构造 CustomEvent 实例。
    /// </summary>
    /// <param name="name">事件名称。</param>
    /// <param name="data">事件数据。</param>
    /// <param name="senderWindowID">发送窗口 ID，可为 null。</param>
    /// <param name="targetWindowID">定向目标窗口 ID（OPT-EVENT-002），可为 null（广播）。</param>
    public CustomEvent(string name, object? data = null, uint? senderWindowID = null, uint? targetWindowID = null)
    {
        Name = name;
        Data = data;
        SenderWindowID = senderWindowID;
        TargetWindowID = targetWindowID;
    }

    /// <summary>
    /// 取消事件，阻止后续监听器接收此事件。
    /// </summary>
    public void Cancel()
    {
        _cts.Cancel();
    }

    /// <summary>
    /// 将事件转换为可用于 JSON 序列化的字典。
    /// </summary>
    /// <returns>包含事件信息的字典。</returns>
    public Dictionary<string, object?> ToJson()
    {
        return new Dictionary<string, object?>
        {
            ["name"] = Name,
            ["data"] = Data,
            ["senderWindowId"] = SenderWindowID,
            ["targetWindowId"] = TargetWindowID
        };
    }
}
