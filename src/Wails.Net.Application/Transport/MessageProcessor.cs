using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Commands;
using Wails.Net.Application.Events;
using Wails.Net.Application.Menus;
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

        /// <summary>
        /// 取消运行中调用。
        /// 对应 Wails v3 Go 版本 messageprocessor.go 中的 <c>cancelCallRequest = 10</c>
        /// （objectNames["CancelCall"]）。
        /// 前端通过此消息类型请求后端取消指定 callId 对应的运行中绑定调用。
        /// </summary>
        public const string Cancel = "cancel";
    }

    /// <summary>
    /// 绑定管理器实例。
    /// </summary>
    private readonly BindingManager _bindings;

    /// <summary>
    /// 命令调度器实例（可选），用于在 BindingManager 未找到方法时回退到命令路径。
    /// </summary>
    private readonly CommandDispatcher? _commands;

    /// <summary>
    /// DI 服务容器（可选），用于创建命令上下文以传递 WindowId。
    /// </summary>
    private readonly IServiceProvider? _services;

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
    /// 运行中调用登记表：callId（即 <see cref="Message.Id"/>） → 链接的取消令牌源。
    /// 对应 Wails v3 Go 版本 messageprocessor.go 中的 <c>runningCalls map[string]context.CancelFunc</c>。
    /// <para>
    /// 每次绑定调用创建独立的 CTS（与全局 <see cref="_cts"/> 链接），使应用关闭时取消所有运行中调用，
    /// 同时允许前端通过 <see cref="MessageTypes.Cancel"/> 消息按 callId 取消单个调用。
    /// </para>
    /// <para>
    /// <b>线程安全</b>：使用 <see cref="ConcurrentDictionary{TKey, TValue}"/> 实现并发安全，
    /// 无需显式加锁（与 Wails v3 Go 版本的 sync.Mutex 实现等价但更轻量）。
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningCalls = new();

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
    /// 使用指定的绑定管理器、事件处理器、窗口查找器和命令调度器构造 MessageProcessor 实例。
    /// 命令调度器用于在前端调用方法名在 BindingManager 中未找到时（如 "counter.increment"）回退查找命令。
    /// </summary>
    /// <param name="bindings">绑定管理器。</param>
    /// <param name="events">事件处理器。</param>
    /// <param name="windowLookup">窗口查找器，根据窗口 ID 返回对应的窗口实例。</param>
    /// <param name="commands">命令调度器，可为 null；为 null 时不进行命令回退。</param>
    public MessageProcessor(
        BindingManager bindings,
        EventProcessor events,
        Func<uint, WebviewWindow?> windowLookup,
        CommandDispatcher? commands)
    {
        _bindings = bindings;
        _events = events;
        _windowLookup = windowLookup;
        _commands = commands;
    }

    /// <summary>
    /// 使用指定的绑定管理器、事件处理器、窗口查找器、命令调度器和 DI 服务容器构造 MessageProcessor 实例。
    /// DI 服务容器用于创建命令上下文，使窗口命令能识别目标窗口 ID。
    /// </summary>
    /// <param name="bindings">绑定管理器。</param>
    /// <param name="events">事件处理器。</param>
    /// <param name="windowLookup">窗口查找器，根据窗口 ID 返回对应的窗口实例。</param>
    /// <param name="commands">命令调度器，可为 null；为 null 时不进行命令回退。</param>
    /// <param name="services">DI 服务容器，用于创建命令上下文。</param>
    public MessageProcessor(
        BindingManager bindings,
        EventProcessor events,
        Func<uint, WebviewWindow?> windowLookup,
        CommandDispatcher? commands,
        IServiceProvider? services)
    {
        _bindings = bindings;
        _events = events;
        _windowLookup = windowLookup;
        _commands = commands;
        _services = services;
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
            // 窗口操作优先走 CommandDispatcher（WindowPlugin 命令路径），
            // 若命令未找到则回退到 ProcessWindow 内部的 DispatchWindowAction 硬编码分发（向后兼容）。
            MessageTypes.Window => await ProcessWindowAsync(message),
            // 取消运行中调用（P0-B1）：前端通过 "cancel" 消息按 callId 取消未完成的绑定调用。
            MessageTypes.Cancel => ProcessCancel(message),
            // 未识别的命名空间（如 "notification.show"、"application.hide"、"tray.setIcon"）
            // 回退到 CommandDispatcher 查找命令，借鉴 Tauri v2 的 "核心即插件" 哲学：
            // 所有系统操作都以插件命令形式注册，前端 wails.<namespace>.<method>() 调用
            // 通过此回退路径路由到对应插件命令。
            _ => await ProcessCommandFallbackAsync(message)
        };
    }

    /// <summary>
    /// 处理绑定调用消息。
    /// 调用顺序：BindingManager（含源生成器调用器） → CommandDispatcher（命令路径）。
    /// 当 BindingManager 未找到方法且 CommandDispatcher 已配置时，回退到命令调度器查找。
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

        // P0-B1：为每个绑定调用创建独立的 CTS 并注册到 _runningCalls，
        // 使前端可通过 "cancel" 消息按 callId 取消此调用。
        // 对应 Wails v3 Go 版本 messageprocessor_call.go 中的：
        //   ctx, cancel := context.WithCancel(context.WithoutCancel(ctx))
        //   m.runningCalls[*callID] = cancel
        //   defer delete(m.runningCalls, *callID)
        // callId 复用 message.Id（与 Wails v3 前端 calls.ts 中以 generateID() 同时作为消息 ID 和 call-id 一致）。
        var callId = message.Id;
        using var callCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

        // 注册到运行中调用表。若 callId 已存在（极少见的 ID 冲突），返回错误。
        // 对应 Wails v3 中的 ambiguousID 检查。
        if (!_runningCalls.TryAdd(callId, callCts))
        {
            return CreateErrorResponse(message.Id, $"callId 冲突：{callId} 已存在运行中调用", CallErrorKind.RuntimeError);
        }

        try
        {
            var args = callPayload.Args ?? Array.Empty<JsonElement>();
            Dictionary<string, object?> result;

            // 优先按 ID 调用，其次按名称调用
            // 按 ID 调用通过 GeneratedBindingsMetadata 查找 ID 对应的方法全名，
            // 然后委托到按名称调用路径（源生成器生成的强类型调用器，零反射）。
            if (callPayload.Id is not null)
            {
                var methodName = TryResolveMethodNameById(callPayload.Id.Value);
                if (methodName is not null)
                {
                    result = await _bindings.Call(methodName, args, callCts.Token);
                }
                else
                {
                    return CreateErrorResponse(message.Id, $"未找到 ID 为 {callPayload.Id.Value} 的绑定方法",
                        CallErrorKind.ReferenceError);
                }
            }
            else if (!string.IsNullOrEmpty(callPayload.Name))
            {
                result = await _bindings.Call(callPayload.Name!, args, callCts.Token);

                // 若 BindingManager 未找到方法（且未通过源生成器注册），
                // 尝试回退到 CommandDispatcher 查找命令（如 "counter.increment"、"notification.show"）
                if (IsNotFoundResult(result, callPayload.Name!) && _commands is not null)
                {
                    var commandResult = await TryDispatchCommandAsync(callPayload.Name!, args, message.WindowId, message.Origin, callCts.Token);
                    if (commandResult is not null)
                    {
                        result = commandResult;
                    }
                }
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
        catch (OperationCanceledException)
        {
            // 调用被取消（前端发送 cancel 消息或应用关闭触发）。
            // 对应 Wails v3 中 ctx 取消时 boundMethod.Call 返回 context.Canceled。
            return CreateErrorResponse(message.Id, "调用已被取消", CallErrorKind.RuntimeError);
        }
        finally
        {
            // 调用完成（无论成功、失败、取消）后从运行中调用表移除。
            // 对应 Wails v3 messageprocessor_call.go 中的 defer delete(m.runningCalls, *callID)。
            _runningCalls.TryRemove(callId, out _);
        }
    }

    /// <summary>
    /// 通过 FNV-1a 哈希 ID 查找对应的方法全名。
    /// 在 <see cref="GeneratedBindingsMetadata.Methods"/> 中线性扫描，匹配 <see cref="BoundMethodInfo.Id"/>。
    /// 数据由源生成器在编译期填充，运行时零反射（遵循 AGENTS.md §3.4）。
    /// </summary>
    /// <param name="id">FNV-1a 32 位哈希 ID。</param>
    /// <returns>匹配的方法全名；未找到返回 null。</returns>
    private static string? TryResolveMethodNameById(uint id)
    {
        foreach (var method in GeneratedBindingsMetadata.Methods)
        {
            if (method.Id == id)
            {
                return method.FullName;
            }
        }
        return null;
    }

    /// <summary>
    /// 处理取消运行中调用的消息。
    /// 对应 Wails v3 Go 版本 messageprocessor_call.go 中的 <c>processCallCancelMethod</c>。
    /// <para>
    /// 从消息载荷中读取 <c>callId</c>，在 <see cref="_runningCalls"/> 中查找对应的 CTS 并调用 <see cref="CancellationTokenSource.Cancel"/>。
    /// 若 callId 不存在（已完成或无效），保持幂等返回成功（与 Wails v3 行为一致）。
    /// </para>
    /// </summary>
    /// <param name="message">取消调用消息，载荷包含 <c>callId</c> 字段。</param>
    /// <returns>固定返回成功响应（取消请求本身不携带错误）。</returns>
    private ResponseMessage? ProcessCancel(Message message)
    {
        CancelCallPayload? cancelPayload;
        try
        {
            cancelPayload = message.Payload.Deserialize<CancelCallPayload>(JsonOptions.DefaultSerializerOptions);
        }
        catch (JsonException)
        {
            return CreateErrorResponse(message.Id, "无效的取消调用载荷", CallErrorKind.TypeError);
        }

        if (cancelPayload is null || string.IsNullOrEmpty(cancelPayload.CallId))
        {
            return CreateErrorResponse(message.Id, "缺少 callId 字段", CallErrorKind.TypeError);
        }

        // 查找并取消运行中调用。TryRemove 保证只取消一次，避免重复 Cancel 调用。
        // 对应 Wails v3 messageprocessor_call.go processCallCancelMethod:
        //   cancel = m.runningCalls[*callID]
        //   if cancel != nil { cancel() }
        if (_runningCalls.TryRemove(cancelPayload.CallId!, out var cts))
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS 已被调用完成路径 dispose（极小概率的竞态），忽略。
            }
            // 注意：不在此 dispose cts，因为调用方的 finally 块负责 dispose。
            // 此处只是触发取消信号，CTS 的 dispose 由创建者（ProcessCallAsync）的 using 块负责。
        }

        // 取消请求总是返回成功（即使 callId 不存在），匹配 Wails v3 行为。
        return new ResponseMessage
        {
            Id = message.Id,
            Type = MessageTypes.Response,
            Result = new Dictionary<string, object?>
            {
                ["result"] = true,
                ["error"] = null
            }
        };
    }

    /// <summary>
    /// 处理未识别命名空间的消息，回退到 CommandDispatcher 查找命令。
    /// 借鉴 Tauri v2 的 "核心即插件" 哲学：所有系统原生操作（窗口、应用、托盘、菜单、剪贴板等）
    /// 都以插件命令形式注册。前端通过 <c>wails.&lt;namespace&gt;.&lt;method&gt;(args)</c> 调用时，
    /// 消息类型为 <c>"namespace.method"</c>，此方法将其作为命令名派发到 CommandDispatcher。
    /// </summary>
    /// <param name="message">未识别命名空间的消息，<see cref="Message.Type"/> 作为命令名。</param>
    /// <returns>命令调用结果响应；若 CommandDispatcher 未配置或命令不存在则返回 null。</returns>
    private async Task<ResponseMessage?> ProcessCommandFallbackAsync(Message message)
    {
        // CommandDispatcher 未配置时（如单元测试场景），保持向后兼容返回 null
        if (_commands is null)
        {
            return null;
        }

        // 将消息类型作为命令名（如 "notification.show"），原始 payload 作为参数对象
        // CommandDispatcher 会从 parametersElement 反序列化命令方法参数
        var request = new InvokeRequest(Guid.NewGuid(), message.Type, message.Payload);

        // 创建带 WindowId 和 WindowName 的上下文，使窗口命令能识别目标窗口，
        // 并使 CommandDispatcher 能执行窗口级 Capability 运行时隔离（对应 Tauri v2 Capability.Windows）
        // 同时传入 Origin 实现 Capability.remote 远程 URL 校验（对应 Tauri v2 Capability.remote）
        var ctx = _services is not null
            ? new CommandContext(_services, message.WindowId, _cts.Token, ResolveWindowName(message.WindowId), message.Origin)
            : null;

        var response = await _commands.DispatchAsync(request, ctx);

        if (!response.Success)
        {
            // 命令未找到或执行失败时返回 null，保持与 _ => null 的向后兼容
            return null;
        }

        return new ResponseMessage
        {
            Id = message.Id,
            Type = MessageTypes.Response,
            Result = new Dictionary<string, object?>
            {
                ["result"] = response.Result,
                ["error"] = null
            }
        };
    }

    /// <summary>
    /// 判断 BindingManager 返回的结果是否为 "未找到方法" 错误。
    /// 直接检查错误字典的 message 字段，避免 JSON 序列化转义非 ASCII 字符导致字符串匹配失败。
    /// </summary>
    /// <param name="result">BindingManager 返回的结果字典。</param>
    /// <param name="name">方法名（用于校验错误信息）。</param>
    /// <returns>若结果中包含 "未找到" 错误且错误信息提及该方法名则返回 true。</returns>
    private static bool IsNotFoundResult(Dictionary<string, object?> result, string name)
    {
        if (result.TryGetValue("error", out var errorObj) && errorObj is not null)
        {
            // errorObj 为 CallError.ToJson() 返回的 Dictionary<string, object?>，
            // 包含 message、cause、kind 三个字段。直接检查 message 字段。
            if (errorObj is Dictionary<string, object?> errorDict
                && errorDict.TryGetValue("message", out var msgObj)
                && msgObj is string msg)
            {
                return msg.Contains("未找到") && msg.Contains(name);
            }
        }

        return false;
    }

    /// <summary>
    /// 尝试通过 CommandDispatcher 派发命令。
    /// 将 JSON 参数数组转换为 CommandDispatcher 期望的 JsonElement 参数形式。
    /// </summary>
    /// <param name="commandName">命令名称。</param>
    /// <param name="args">JSON 参数数组。</param>
    /// <param name="windowId">窗口 ID（可为 null）。</param>
    /// <param name="origin">调用来源 URL（可为 null），用于 Capability.remote 远程 URL 校验。</param>
    /// <param name="cancellationToken">调用级取消令牌（P0-B1：支持取消运行中命令调用）。</param>
    /// <returns>调用结果字典；若命令派发抛出异常则返回包含错误的字典。</returns>
    private async Task<Dictionary<string, object?>?> TryDispatchCommandAsync(
        string commandName,
        JsonElement[] args,
        uint? windowId,
        string? origin,
        CancellationToken cancellationToken)
    {
        try
        {
            // 将参数数组转换为 JsonElement（用于 CommandDispatcher 参数绑定）
            // CommandDispatcher 会从 Parameters 中按方法参数类型逐个反序列化
            JsonElement parametersElement;
            if (args.Length == 1)
            {
                // 单参数场景：使用第一个参数
                parametersElement = args[0];
            }
            else if (args.Length > 1)
            {
                // 多参数场景：包装为数组
                var bytes = JsonSerializer.SerializeToUtf8Bytes(args, JsonOptions.DefaultSerializerOptions);
                parametersElement = JsonDocument.Parse(bytes).RootElement.Clone();
            }
            else
            {
                // 无参数场景：使用空对象
                parametersElement = default;
            }

            var request = new InvokeRequest(Guid.NewGuid(), commandName, parametersElement);

            // 创建带 WindowId 和 WindowName 的上下文，使窗口命令能识别目标窗口。
            // 借鉴 ASP.NET Core 的 DI 模式：CommandDispatcher 从 _services 获取依赖，
            // 命令方法通过 ICommandContext.WindowId 定位目标 WebviewWindow。
            // 同时传入 WindowName 支持窗口级 Capability 运行时隔离。
            // 同时传入 Origin 支持 Capability.remote 远程 URL 校验。
            // P0-B1：使用调用级 cancellationToken 替代全局 _cts.Token，使前端可取消运行中命令。
            var ctx = _services is not null
                ? new CommandContext(_services, windowId, cancellationToken, ResolveWindowName(windowId), origin)
                : null;
            var response = await _commands!.DispatchAsync(request, ctx);

            if (response.Success)
            {
                return new Dictionary<string, object?>
                {
                    ["result"] = response.Result,
                    ["error"] = null
                };
            }

            return new Dictionary<string, object?>
            {
                ["result"] = null,
                ["error"] = new CallError(response.Error ?? "命令调用失败", null, CallErrorKind.RuntimeError).ToJson()
            };
        }
        catch (OperationCanceledException)
        {
            // 命令被取消，向上抛出由 ProcessCallAsync 统一处理为 "调用已被取消"。
            throw;
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object?>
            {
                ["result"] = null,
                ["error"] = new CallError(ex.Message, null, CallErrorKind.RuntimeError).ToJson()
            };
        }
    }

    /// <summary>
    /// 根据窗口 ID 解析窗口名称，用于窗口级 Capability 运行时隔离。
    /// </summary>
    /// <param name="windowId">窗口 ID，可为 null。</param>
    /// <returns>窗口名称；若 <paramref name="windowId"/> 为 null 或查找失败则返回 null。</returns>
    private string? ResolveWindowName(uint? windowId)
    {
        if (windowId is null || _windowLookup is null)
        {
            return null;
        }

        try
        {
            return _windowLookup(windowId.Value)?.Name;
        }
        catch
        {
            // 窗口查找失败时返回 null，权限校验退化为仅检查全局授权
            return null;
        }
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
    /// <para>
    /// P1-4：与 Wails v3 行为对齐。解析 <see cref="ContextMenuPayload"/>，
    /// 通过 <see cref="_windowLookup"/> 查找目标 <see cref="WebviewWindow"/>，
    /// 调用 <c>window.Impl.OpenContextMenu(ContextMenuData)</c> 弹出已注册的上下文菜单。
    /// </para>
    /// <para>
    /// 同时保留 <c>wails:contextmenu</c> 事件广播作为额外的可订阅钩子（不影响主路径）。
    /// </para>
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

        // 将上下文菜单事件转发为标准事件广播（保留钩子）
        _events.Emit("wails:contextmenu", menuPayload, message.WindowId);

        // P1-4：与 Wails v3 对齐，查找目标窗口并调用 OpenContextMenu(ContextMenuData)
        if (_windowLookup is not null && message.WindowId is { } windowId)
        {
            var window = _windowLookup(windowId);
            if (window?.Impl is not null)
            {
                var data = new ContextMenuData
                {
                    // 兼容旧前端格式：Id 优先，为空时回退到 ContextId
                    Id = !string.IsNullOrEmpty(menuPayload.Id) ? menuPayload.Id : (menuPayload.ContextId ?? string.Empty),
                    X = menuPayload.X,
                    Y = menuPayload.Y,
                    Data = menuPayload.Data
                };
                try
                {
                    window.Impl.OpenContextMenu(data);
                }
                catch (Exception ex)
                {
                    _events.Emit("wails:error", new { source = "contextmenu", error = ex.Message }, windowId);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 处理窗口操作消息。
    /// 优先将消息派发到 <see cref="CommandDispatcher"/>（走 WindowPlugin 注册的 <c>window.*</c> 命令路径，
    /// 借鉴 Tauri v2 的 "核心即插件" 哲学）；若 CommandDispatcher 未配置或命令未找到，
    /// 回退到 <see cref="DispatchWindowAction"/> 硬编码分发（向后兼容，未注册 WindowPlugin 时仍可用）。
    /// 同时将窗口操作事件转发为标准的 Wails 事件进行广播。
    /// </summary>
    /// <param name="message">窗口操作消息，类型为 "window" 或 "window.&lt;action&gt;"。</param>
    /// <returns>包含操作结果的响应消息，若无法处理则返回错误响应。</returns>
    private async Task<ResponseMessage?> ProcessWindowAsync(Message message)
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

        // 优先尝试 CommandDispatcher（WindowPlugin 命令路径）。
        // ProcessCommandFallbackAsync 将 message.Type（如 "window.setTitle"）作为命令名派发，
        // 若 WindowPlugin 已注册则命中对应命令；若命令未找到则返回 null，继续走硬编码回退。
        if (_commands is not null)
        {
            var commandResponse = await ProcessCommandFallbackAsync(message);
            if (commandResponse is not null)
            {
                // 命中插件命令：广播事件并返回响应
                _events.Emit($"wails:window:{action}", payload, message.WindowId);
                return commandResponse;
            }
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

        // 回退到硬编码分发（向后兼容：未注册 WindowPlugin 或命令未注册时使用）
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
            // 通过源生成器生成的元数据查询绑定方法名列表，零反射（遵循 AGENTS.md §3.4）。
            "bindings" => _bindings.GetRegisteredMethodNames(),
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

    /// <summary>
    /// 调用来源 URL（前端页面 origin），可为 null。
    /// 用于 Capability.remote 字段的运行时校验：当权限附带远程 URL 限制时，
    /// 调度器据此校验调用来源是否在允许模式集合内。
    /// 本地源（wails://、http(s)://localhost、http(s)://127.0.0.1、null）始终允许。
    /// </summary>
    [JsonPropertyName("origin")]
    public string? Origin { get; set; }
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
/// 取消运行中调用消息的载荷。
/// 对应 Wails v3 Go 版本 messageprocessor_call.go 中 <c>processCallCancelMethod</c>
/// 读取的 <c>args.AsMap().String("call-id")</c> 字段。
/// </summary>
public class CancelCallPayload
{
    /// <summary>
    /// 要取消的调用 ID。
    /// <para>
    /// 此 ID 与原始 <see cref="Message.Id"/> 一致：前端 _wailsInvoke 生成 id 时同时作为消息 ID 和 call-id。
    /// 对应 Wails v3 前端 calls.ts 中 <c>const id = generateID()</c> 同时用于 <c>callResponses</c> 键和 <c>"call-id"</c> 字段。
    /// </para>
    /// </summary>
    [JsonPropertyName("callId")]
    public string? CallId { get; set; }
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
/// 对应 Wails v3 Go 版本 <c>messageprocessor_contextmenu.go</c> 中的 <c>ContextMenuData</c> 结构（P1-4）。
/// <para>
/// 字段命名与 Wails v3 前端 <c>contextmenu.ts</c> 发送格式一致：
/// <c>{id, x, y, data}</c>。windowId 来自 <see cref="Message.WindowId"/>，不在此重复。
/// </para>
/// <para>
/// 兼容性：保留 <c>ContextId</c> 字段以接受旧前端格式（值为 <c>"contextId"</c>），
/// 读取时优先使用 <c>Id</c>，若 <c>Id</c> 为空则回退到 <c>ContextId</c>。
/// </para>
/// </summary>
public class ContextMenuPayload
{
    /// <summary>
    /// 已注册的上下文菜单 ID。
    /// 对应前端 CSS 变量 <c>--custom-contextmenu</c> 的值。
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// 鼠标 X 坐标（clientX，相对于浏览器视口）。
    /// </summary>
    [JsonPropertyName("x")]
    public int X { get; set; }

    /// <summary>
    /// 鼠标 Y 坐标（clientY，相对于浏览器视口）。
    /// </summary>
    [JsonPropertyName("y")]
    public int Y { get; set; }

    /// <summary>
    /// 来自前端 CSS 变量 <c>--custom-contextmenu-data</c> 的额外数据字符串。
    /// 透传到菜单项点击回调。
    /// </summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    /// <summary>
    /// 兼容旧前端格式的元素标识。
    /// 若 <see cref="Id"/> 为空，则回退使用此值作为菜单 ID。
    /// </summary>
    [JsonPropertyName("contextId")]
    public string? ContextId { get; set; }
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
