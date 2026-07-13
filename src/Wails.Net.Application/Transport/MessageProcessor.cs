using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Events;
using Wails.Net.Application.Windows;
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
    /// 对应 Wails v3 Go 版本 messageprocessor.go 中的消息类型定义。
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

        /// <summary>拖放操作（文件拖入窗口）。</summary>
        public const string Drag = "drag";

        /// <summary>上下文菜单（右键菜单）。</summary>
        public const string ContextMenu = "contextmenu";

        /// <summary>系统命令。</summary>
        public const string System = "system";
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
    /// 窗口查找器，根据窗口 ID 获取 <see cref="WebviewWindow"/> 实例。
    /// 用于窗口操作消息的分发。可为 null（在测试或无窗口环境中）。
    /// </summary>
    private readonly Func<uint, WebviewWindow?>? _windowLookup;

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
    /// 使用指定的绑定管理器、事件处理器和窗口查找器构造 MessageProcessor 实例。
    /// 窗口查找器用于将窗口操作消息（type 为 "window.*"）分发到对应的 <see cref="WebviewWindow"/> 方法。
    /// </summary>
    /// <param name="bindings">绑定管理器。</param>
    /// <param name="events">事件处理器。</param>
    /// <param name="windowLookup">窗口查找器，根据窗口 ID 返回对应的窗口实例。</param>
    public MessageProcessor(BindingManager bindings, EventProcessor events, Func<uint, WebviewWindow?> windowLookup)
    {
        _bindings = bindings;
        _events = events;
        _windowLookup = windowLookup;
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
    /// 支持 call、event、query、drag、contextmenu、window 等消息类型。
    /// 对于带命名空间的消息（如 "window.setTitle"），自动提取基础类型进行路由。
    /// </summary>
    /// <param name="message">要处理的消息。</param>
    /// <returns>响应消息，若无需响应则返回 null。</returns>
    public async Task<ResponseMessage?> ProcessAsync(Message message)
    {
        // 支持 "namespace.action" 格式的消息类型（如 "window.setTitle"、"event.emit"）。
        // 提取基础命名空间进行路由，具体动作由对应的处理方法解析。
        var baseType = message.Type;
        var dotIndex = message.Type.IndexOf('.');
        if (dotIndex > 0)
        {
            baseType = message.Type[..dotIndex];
        }

        return baseType switch
        {
            MessageTypes.Call => await ProcessCallAsync(message),
            MessageTypes.Event => ProcessEvent(message),
            MessageTypes.Query => ProcessQuery(message),
            MessageTypes.Drag => ProcessDrag(message),
            MessageTypes.ContextMenu => ProcessContextMenu(message),
            MessageTypes.Window => ProcessWindow(message),
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
    /// 处理拖放消息（文件拖入窗口）。
    /// 将拖放事件转发为标准的 Wails 事件进行广播。
    /// </summary>
    /// <param name="message">拖放消息。</param>
    /// <returns>始终返回 null（拖放事件无需响应）。</returns>
    private ResponseMessage? ProcessDrag(Message message)
    {
        var dragPayload = message.Payload.Deserialize<DragPayload>(JsonOptions.DefaultSerializerOptions);
        if (dragPayload is null)
        {
            return null;
        }

        // 将拖放事件转发为标准事件广播
        _events.Emit("wails:drag", dragPayload, message.WindowId);
        return null;
    }

    /// <summary>
    /// 处理上下文菜单消息（右键菜单）。
    /// 将上下文菜单事件转发为标准的 Wails 事件进行广播。
    /// </summary>
    /// <param name="message">上下文菜单消息。</param>
    /// <returns>始终返回 null（上下文菜单事件无需响应）。</returns>
    private ResponseMessage? ProcessContextMenu(Message message)
    {
        var menuPayload = message.Payload.Deserialize<ContextMenuPayload>(JsonOptions.DefaultSerializerOptions);
        if (menuPayload is null)
        {
            return null;
        }

        // 将上下文菜单事件转发为标准事件广播
        _events.Emit("wails:contextmenu", menuPayload, message.WindowId);
        return null;
    }

    /// <summary>
    /// 处理窗口操作消息。
    /// 从消息类型中提取操作名（如 "window.setTitle" 中的 "setTitle"），
    /// 查找目标窗口实例并调用对应的窗口方法。
    /// 同时将窗口操作事件转发为标准的 Wails 事件进行广播。
    /// </summary>
    /// <param name="message">窗口操作消息，类型为 "window" 或 "window.&lt;action&gt;"。</param>
    /// <returns>包含操作结果的响应消息，若无法处理则返回错误响应。</returns>
    private ResponseMessage? ProcessWindow(Message message)
    {
        // 从消息类型中提取操作名：支持 "window"（使用 WindowPayload.Action）和 "window.<action>" 两种格式
        var action = ExtractActionFromType(message.Type, MessageTypes.Window);
        var payload = message.Payload;

        // 若类型中未包含 action，则尝试从 WindowPayload.Action 读取（向后兼容）
        if (string.IsNullOrEmpty(action))
        {
            var windowPayload = payload.Deserialize<WindowPayload>(JsonOptions.DefaultSerializerOptions);
            action = windowPayload?.Action ?? string.Empty;
        }

        if (string.IsNullOrEmpty(action))
        {
            return CreateErrorResponse(message.Id, "窗口消息未指定操作类型", CallErrorKind.TypeError);
        }

        // 窗口查找器未配置时，仅广播事件并返回 null（保持向后兼容）
        if (_windowLookup is null)
        {
            _events.Emit($"wails:window:{action}", payload, message.WindowId);
            return null;
        }

        // 确定目标窗口 ID：优先使用 message.WindowId
        var windowId = message.WindowId;
        if (windowId is null)
        {
            // 尝试从载荷中读取 windowId
            if (payload.TryGetProperty("windowId", out var widElement) &&
                widElement.TryGetUInt32(out var wid))
            {
                windowId = wid;
            }
        }

        if (windowId is null)
        {
            return CreateErrorResponse(message.Id, "窗口消息未指定目标窗口 ID", CallErrorKind.TypeError);
        }

        var window = _windowLookup(windowId.Value);
        if (window is null)
        {
            return CreateErrorResponse(message.Id, $"找不到 ID 为 {windowId} 的窗口", CallErrorKind.ReferenceError);
        }

        // 分发到对应的窗口方法
        object? result;
        try
        {
            result = DispatchWindowAction(window, action.ToLowerInvariant(), payload);
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(message.Id, ex.Message, CallErrorKind.RuntimeError);
        }

        // 同时广播窗口操作事件，便于其他监听者感知窗口变化
        _events.Emit($"wails:window:{action}", payload, message.WindowId);

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
    /// 从消息类型字符串中提取操作名。
    /// 例如 "window.setTitle" 提取出 "setTitle"，"window" 返回空字符串。
    /// </summary>
    /// <param name="type">消息类型字符串。</param>
    /// <param name="namespace">命名空间前缀（如 "window"）。</param>
    /// <returns>操作名，若类型不包含子操作则返回空字符串。</returns>
    private static string ExtractActionFromType(string type, string @namespace)
    {
        var prefix = @namespace + ".";
        if (type.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            type.Length > prefix.Length)
        {
            return type[prefix.Length..];
        }

        return string.Empty;
    }

    /// <summary>
    /// 根据操作名分发到对应的 <see cref="WebviewWindow"/> 方法。
    /// 对应 Wails v3 前端的 window 命名空间 API。
    /// </summary>
    /// <param name="window">目标窗口实例。</param>
    /// <param name="action">小写操作名（如 "settitle"、"minimize"）。</param>
    /// <param name="payload">消息载荷，包含操作参数。</param>
    /// <returns>操作返回值（查询类操作），无返回值的操作返回 null。</returns>
    private static object? DispatchWindowAction(WebviewWindow window, string action, JsonElement payload)
    {
        switch (action)
        {
            // 标题与尺寸
            case "settitle":
                window.SetTitle(payload.GetProperty("title").GetString()!);
                return null;
            case "setsize":
                window.SetSize(GetInt(payload, "width"), GetInt(payload, "height"));
                return null;
            case "setminsize":
                window.SetMinSize(GetInt(payload, "width"), GetInt(payload, "height"));
                return null;
            case "setmaxsize":
                window.SetMaxSize(GetInt(payload, "width"), GetInt(payload, "height"));
                return null;
            case "setposition":
                window.SetPosition(GetInt(payload, "x"), GetInt(payload, "y"));
                return null;

            // 显示/隐藏/状态
            case "close":
                window.Close();
                return null;
            case "minimize":
                window.Minimise();
                return null;
            case "maximize":
                window.Maximise();
                return null;
            case "unminimize":
                window.UnMinimise();
                return null;
            case "unmaximize":
                window.UnMaximise();
                return null;
            case "show":
                window.Show();
                return null;
            case "hide":
                window.Hide();
                return null;
            case "centre":
                window.Centre();
                return null;
            case "restore":
                window.Restore();
                return null;
            case "focus":
                window.Focus();
                return null;

            // 全屏与置顶
            case "setalwayson top":
            case "setalwaysontop":
                window.SetAlwaysOnTop(GetBool(payload, "onTop"));
                return null;
            case "setfullscreen":
                if (GetBool(payload, "fullscreen"))
                {
                    window.Fullscreen();
                }
                else
                {
                    window.UnFullscreen();
                }

                return null;
            case "unfullscreen":
                window.UnFullscreen();
                return null;
            case "setframeless":
                window.SetFrameless(GetBool(payload, "frameless"));
                return null;

            // DevTools
            case "opendevtools":
                window.OpenDevTools();
                return null;
            case "closedevtools":
                window.CloseDevTools();
                return null;

            // 缩放
            case "setzoom":
                window.SetZoom((float)GetDouble(payload, "zoom"));
                return null;
            case "zoomin":
                window.ZoomIn();
                return null;
            case "zoomout":
                window.ZoomOut();
                return null;
            case "zoomreset":
                window.SetZoom(1.0f);
                return null;

            // 导航
            case "goback":
                window.GoBack();
                return null;
            case "goforward":
                window.GoForward();
                return null;
            case "reload":
                window.Reload();
                return null;
            case "seturl":
                window.SetURL(payload.GetProperty("url").GetString()!);
                return null;
            case "sethtml":
                window.SetHTML(payload.GetProperty("html").GetString()!);
                return null;

            // 打印与导出
            case "print":
                window.Print();
                return null;
            case "printtopdf":
                var pdfPath = payload.GetProperty("path").GetString()!;
                if (payload.TryGetProperty("options", out var pdfOptsEl) && pdfOptsEl.ValueKind == JsonValueKind.Object)
                {
                    var pdfOpts = pdfOptsEl.Deserialize<PrintToPdfOptions>(JsonOptions.DefaultSerializerOptions);
                    window.PrintToPDF(pdfPath, pdfOpts);
                }
                else
                {
                    window.PrintToPDF(pdfPath);
                }

                return null;

            // 执行 JS 与注入 CSS
            case "execjs":
                window.ExecJS(payload.GetProperty("js").GetString()!);
                return null;
            case "injectcss":
                window.InjectCSS(payload.GetProperty("css").GetString()!);
                return null;

            // 透明度
            case "setopacity":
                window.SetOpacity((float)GetDouble(payload, "opacity"));
                return null;
            case "getopacity":
                return window.GetOpacity();

            // 可调整大小
            case "setresizable":
                window.SetResizable(GetBool(payload, "resizable"));
                return null;

            // 自定义协议
            case "registercustomscheme":
                window.RegisterCustomScheme(payload.GetProperty("scheme").GetString()!);
                return null;

            // 任务栏
            case "setskiptaskbar":
                window.SetSkipTaskbar(GetBool(payload, "skip"));
                return null;
            case "setignorecursorevents":
                window.SetIgnoreCursorEvents(GetBool(payload, "ignore"));
                return null;
            case "setbadgecount":
                window.SetBadgeCount(GetInt(payload, "count"));
                return null;
            case "setbadgelabel":
                window.SetBadgeLabel(payload.TryGetProperty("label", out var lblEl) ? lblEl.GetString() : null);
                return null;
            case "setvisibleonallworkspaces":
                window.SetVisibleOnAllWorkspaces(GetBool(payload, "visible"));
                return null;
            case "setbordercolor":
                window.SetBorderColor(payload.TryGetProperty("color", out var colorEl) ? colorEl.GetString() : null);
                return null;
            case "setfiledropenabled":
                window.SetFileDropEnabled(GetBool(payload, "enabled"));
                return null;

            // 查询类操作（有返回值）
            case "getsize":
                var (sw, sh) = window.GetSize();
                return new Dictionary<string, object?> { ["width"] = sw, ["height"] = sh };
            case "getposition":
                var (px, py) = window.GetPosition();
                return new Dictionary<string, object?> { ["x"] = px, ["y"] = py };
            case "geturl":
                return window.GetURL();
            case "getzoom":
                return window.GetZoom();
            case "isfullscreen":
                return window.IsFullscreen();
            case "ismaximised":
                return window.IsMaximised();
            case "isminimised":
                return window.IsMinimised();
            case "isvisible":
                return window.IsVisible();
            case "isfocused":
                return window.IsFocused();

            default:
                throw new InvalidOperationException($"未知的窗口操作: {action}");
        }
    }

    /// <summary>
    /// 从 JSON 载荷中安全获取 int 值。
    /// </summary>
    private static int GetInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var prop) ? prop.GetInt32() : 0;
    }

    /// <summary>
    /// 从 JSON 载荷中安全获取 bool 值。
    /// </summary>
    private static bool GetBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var prop) && prop.GetBoolean();
    }

    /// <summary>
    /// 从 JSON 载荷中安全获取 double 值。
    /// </summary>
    private static double GetDouble(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var prop) ? prop.GetDouble() : 0.0;
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
        try
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
        catch (OperationCanceledException)
        {
            // 停止时取消令牌触发的取消异常是预期行为，正常退出循环。
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
/// 拖放消息的载荷，包含拖入的文件/数据信息。
/// 对应 Wails v3 前端拖放事件。
/// </summary>
public class DragPayload
{
    /// <summary>
    /// 拖入的文件路径列表。
    /// </summary>
    [JsonPropertyName("files")]
    public string[]? Files { get; set; }

    /// <summary>
    /// 拖入的数据（文本或自定义数据）。
    /// </summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    /// <summary>
    /// 鼠标 X 坐标。
    /// </summary>
    [JsonPropertyName("x")]
    public int X { get; set; }

    /// <summary>
    /// 鼠标 Y 坐标。
    /// </summary>
    [JsonPropertyName("y")]
    public int Y { get; set; }

    /// <summary>
    /// 发送窗口 ID。
    /// </summary>
    [JsonPropertyName("windowId")]
    public uint? WindowId { get; set; }
}

/// <summary>
/// 上下文菜单消息的载荷。
/// 对应 Wails v3 前端右键菜单事件。
/// </summary>
public class ContextMenuPayload
{
    /// <summary>
    /// 鼠标 X 坐标。
    /// </summary>
    [JsonPropertyName("x")]
    public int X { get; set; }

    /// <summary>
    /// 鼠标 Y 坐标。
    /// </summary>
    [JsonPropertyName("y")]
    public int Y { get; set; }

    /// <summary>
    /// 触发上下文菜单的元素标识。
    /// </summary>
    [JsonPropertyName("contextId")]
    public string? ContextId { get; set; }

    /// <summary>
    /// 发送窗口 ID。
    /// </summary>
    [JsonPropertyName("windowId")]
    public uint? WindowId { get; set; }
}

/// <summary>
/// 窗口操作消息的载荷。
/// 对应 Wails v3 前端窗口操作事件。
/// </summary>
public class WindowPayload
{
    /// <summary>
    /// 窗口操作类型（如 minimize、maximize、close、focus 等）。
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 目标窗口 ID。
    /// </summary>
    [JsonPropertyName("windowId")]
    public uint? WindowId { get; set; }

    /// <summary>
    /// 附加数据。
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; set; }
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
