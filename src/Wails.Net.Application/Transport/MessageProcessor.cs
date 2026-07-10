using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Errors;

namespace Wails.Net.Application.Transport;

/// <summary>
/// 消息处理器，负责解析前端传入的 JSON 消息并分发到对应的处理逻辑。
/// 对应 Wails v3 Go 版本 internal/runtime/messageprocessor.go。
/// </summary>
public class MessageProcessor
{
    /// <summary>
    /// 消息类型常量，与 Wails v3 前端协议一致。
    /// </summary>
    public static class MessageTypes
    {
        /// <summary>绑定方法调用。</summary>
        public const string Call = "call";

        /// <summary>事件发布。</summary>
        public const string Event = "event";

        /// <summary>窗口操作。</summary>
        public const string Window = "window";

        /// <summary>查询请求。</summary>
        public const string Query = "query";

        /// <summary>响应消息。</summary>
        public const string Response = "response";

        /// <summary>错误消息。</summary>
        public const string Error = "error";
    }

    /// <summary>
    /// 绑定管理器实例。
    /// </summary>
    private readonly BindingManager _bindings;

    /// <summary>
    /// 事件处理器实例。
    /// </summary>
    private readonly EventProcessor _events;

    /// <summary>
    /// 异步消息处理队列。
    /// </summary>
    private readonly BlockingCollection<Message> _queue = new(new ConcurrentQueue<Message>());

    /// <summary>
    /// 后台消费任务。
    /// </summary>
    private Task? _consumerTask;

    /// <summary>
    /// 取消令牌源。
    /// </summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 使用指定的绑定管理器和事件处理器构造 MessageProcessor 实例。
    /// </summary>
    /// <param name="bindings">绑定管理器。</param>
    /// <param name="events">事件处理器。</param>
    public MessageProcessor(BindingManager bindings, EventProcessor events)
    {
        _bindings = bindings;
        _events = events;
    }

    /// <summary>
    /// 启动消息处理器的后台消费任务。
    /// </summary>
    public void Start()
    {
        if (_consumerTask is not null)
        {
            return; // 已启动
        }

        _consumerTask = Task.Run(ProcessQueueAsync);
    }

    /// <summary>
    /// 停止消息处理器并等待后台任务完成。
    /// </summary>
    /// <returns>表示停止操作的异步任务。</returns>
    public async Task StopAsync()
    {
        _cts.Cancel();
        _queue.CompleteAdding();
        if (_consumerTask is not null)
        {
            await _consumerTask;
            _consumerTask = null;
        }
    }

    /// <summary>
    /// 将原始 JSON 消息字符串解析为 Message 对象并加入处理队列。
    /// </summary>
    /// <param name="json">原始 JSON 消息字符串。</param>
    /// <returns>解析后的 Message 对象，若解析失败则返回 null。</returns>
    public Message? ParseMessage(string json)
    {
        try
        {
            var message = JsonSerializer.Deserialize<Message>(json, JsonOptions.DefaultSerializerOptions);
            return message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 将消息加入处理队列。
    /// </summary>
    /// <param name="message">要处理的消息。</param>
    public void Enqueue(Message message)
    {
        if (!_cts.IsCancellationRequested)
        {
            _queue.Add(message);
        }
    }

    /// <summary>
    /// 同步处理单条消息并返回响应。
    /// 此方法主要用于测试和需要立即响应的场景。
    /// </summary>
    /// <param name="message">要处理的消息。</param>
    /// <returns>响应消息，若无需响应则返回 null。</returns>
    public async Task<ResponseMessage?> ProcessAsync(Message message)
    {
        return message.Type switch
        {
            MessageTypes.Call => await ProcessCallAsync(message),
            MessageTypes.Event => ProcessEvent(message),
            MessageTypes.Query => ProcessQuery(message),
            _ => null
        };
    }

    /// <summary>
    /// 处理绑定调用消息。
    /// </summary>
    /// <param name="message">绑定调用消息。</param>
    /// <returns>包含调用结果的响应消息。</returns>
    private async Task<ResponseMessage?> ProcessCallAsync(Message message)
    {
        CallPayload? callPayload;
        try
        {
            callPayload = message.Payload.Deserialize<CallPayload>(JsonOptions.DefaultSerializerOptions);
        }
        catch (JsonException)
        {
            return CreateErrorResponse(message.Id, "无效的调用载荷", CallErrorKind.TypeError);
        }

        if (callPayload is null)
        {
            return CreateErrorResponse(message.Id, "无效的调用载荷", CallErrorKind.TypeError);
        }

        var args = callPayload.Args ?? Array.Empty<JsonElement>();
        Dictionary<string, object?> result;

        // 优先按 ID 调用，其次按名称调用
        if (callPayload.Id is not null)
        {
            result = await _bindings.Call(callPayload.Id.Value, args, _cts.Token);
        }
        else if (!string.IsNullOrEmpty(callPayload.Name))
        {
            result = await _bindings.Call(callPayload.Name!, args, _cts.Token);
        }
        else
        {
            return CreateErrorResponse(message.Id, "调用必须指定 id 或 name", CallErrorKind.TypeError);
        }

        return new ResponseMessage
        {
            Id = message.Id,
            Type = MessageTypes.Response,
            Result = result
        };
    }

    /// <summary>
    /// 处理事件发布消息。
    /// </summary>
    /// <param name="message">事件消息。</param>
    /// <returns>始终返回 null（事件无需响应）。</returns>
    private ResponseMessage? ProcessEvent(Message message)
    {
        var eventPayload = message.Payload.Deserialize<EventPayload>(JsonOptions.DefaultSerializerOptions);
        if (eventPayload is null)
        {
            return null;
        }

        _events.Emit(eventPayload.Name, eventPayload.Data, eventPayload.SenderWindowID);
        return null;
    }

    /// <summary>
    /// 处理查询消息。
    /// </summary>
    /// <param name="message">查询消息。</param>
    /// <returns>查询响应消息。</returns>
    private ResponseMessage? ProcessQuery(Message message)
    {
        var queryPayload = message.Payload.Deserialize<QueryPayload>(JsonOptions.DefaultSerializerOptions);
        if (queryPayload is null)
        {
            return null;
        }

        object? result = queryPayload.Query switch
        {
            "bindings" => _bindings.BoundMethods.Keys,
            "events" => _events.ListenerCount(""), // 占位，实际可扩展
            _ => null
        };

        return new ResponseMessage
        {
            Id = message.Id,
            Type = MessageTypes.Response,
            Result = new Dictionary<string, object?>
            {
                ["result"] = result,
                ["error"] = null
            }
        };
    }

    /// <summary>
    /// 创建错误响应消息。
    /// </summary>
    /// <param name="id">原始消息 ID。</param>
    /// <param name="errorMessage">错误消息。</param>
    /// <param name="kind">错误类型。</param>
    /// <returns>错误响应消息。</returns>
    private static ResponseMessage CreateErrorResponse(string id, string errorMessage, CallErrorKind kind)
    {
        var error = new CallError(errorMessage, null, kind);
        return new ResponseMessage
        {
            Id = id,
            Type = MessageTypes.Error,
            Result = new Dictionary<string, object?>
            {
                ["result"] = null,
                ["error"] = error.ToJson()
            }
        };
    }

    /// <summary>
    /// 后台队列消费循环。
    /// </summary>
    private async Task ProcessQueueAsync()
    {
        foreach (var message in _queue.GetConsumingEnumerable(_cts.Token))
        {
            try
            {
                await ProcessAsync(message);
            }
            catch
            {
                // 队列消费中的异常不应中断循环
            }
        }
    }
}

/// <summary>
/// 表示从前端传入的消息。
/// 对应 Wails v3 前端 JSON 消息格式。
/// </summary>
public class Message
{
    /// <summary>
    /// 消息唯一标识符，用于匹配请求和响应。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 消息类型（call、event、window、query）。
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 消息载荷，包含具体数据。
    /// </summary>
    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }

    /// <summary>
    /// 发送窗口 ID，可为 null。
    /// </summary>
    [JsonPropertyName("windowId")]
    public uint? WindowId { get; set; }
}

/// <summary>
/// 绑定调用消息的载荷。
/// </summary>
public class CallPayload
{
    /// <summary>
    /// 绑定方法的哈希 ID（优先使用）。
    /// </summary>
    [JsonPropertyName("id")]
    public uint? Id { get; set; }

    /// <summary>
    /// 绑定方法的全限定名（备用）。
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// 调用参数 JSON 数组。
    /// </summary>
    [JsonPropertyName("args")]
    public JsonElement[]? Args { get; set; }
}

/// <summary>
/// 事件发布消息的载荷。
/// </summary>
public class EventPayload
{
    /// <summary>
    /// 事件名称。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 事件数据。
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; set; }

    /// <summary>
    /// 发送窗口 ID。
    /// </summary>
    [JsonPropertyName("senderWindowId")]
    public uint? SenderWindowID { get; set; }
}

/// <summary>
/// 查询消息的载荷。
/// </summary>
public class QueryPayload
{
    /// <summary>
    /// 查询类型（bindings、events）。
    /// </summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;
}

/// <summary>
/// 表示返回给前端的响应消息。
/// </summary>
public class ResponseMessage
{
    /// <summary>
    /// 对应请求的消息 ID。
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 响应类型（response 或 error）。
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = MessageProcessor.MessageTypes.Response;

    /// <summary>
    /// 响应结果。
    /// </summary>
    [JsonPropertyName("result")]
    public Dictionary<string, object?> Result { get; set; } = new();
}
