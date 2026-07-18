using System.Collections.Concurrent;
using System.Text.Json;
using Wails.Net.Application.Bindings;
using Wails.Net.Application.Windows;

namespace Wails.Net.Application.Transport;

/// <summary>
/// 原生 IPC 传输层默认实现（P0-C2）。
/// 通过 WebView 原生 postMessage 通道收发消息，避免 HTTP 协议栈开销。
/// <para>
/// 与 <see cref="HttpTransport"/> 并存：
/// <list type="bullet">
/// <item>小消息（&lt; 512KB）：走原生 postMessage，低延迟。</item>
/// <item>大消息（&gt;= 512KB）：前端自动回退到 HTTP 分块上传（<see cref="HttpTransport"/>）。</item>
/// </list>
/// </para>
/// <para>
/// 实现策略：
/// <list type="number">
/// <item>窗口创建时由平台 <c>IPlatformApp</c> 调用 <see cref="RegisterWindow"/> 注册平台实现。</item>
/// <item>注册过程会调用 <c>impl.SetNativeMessageHandler</c> 安装消息路由回调。</item>
/// <item>前端通过 <c>window.chrome.webview.postMessage(jsonStr)</c> 发送消息。</item>
/// <item>平台实现接收消息并调用注册的回调（<see cref="HandleIncomingAsync"/>）。</item>
/// <item>回调将消息委托给 <see cref="MessageProcessor"/> 处理，响应通过 <see cref="PostToWindowAsync"/> 回推。</item>
/// <item>事件广播（<see cref="IWailsEventListener.NotifyEvent"/>）通过同一通道推送到所有窗口。</item>
/// </list>
/// </para>
/// </summary>
public sealed class NativeIpcTransport : INativeIpcTransport
{
    /// <summary>
    /// 消息处理器实例。
    /// </summary>
    private readonly MessageProcessor _processor;

    /// <summary>
    /// 按窗口 ID 索引的平台实现实例。
    /// </summary>
    private readonly ConcurrentDictionary<uint, IWebviewWindowImpl> _windowImpls = new();

    /// <summary>
    /// 使用指定消息处理器构造 <see cref="NativeIpcTransport"/> 实例。
    /// </summary>
    /// <param name="processor">消息处理器实例，用于解析与路由 IPC 消息。</param>
    public NativeIpcTransport(MessageProcessor processor)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
    }

    /// <inheritdoc />
    public bool SupportsBinary => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public int RegisteredWindowCount => _windowImpls.Count;

    /// <summary>
    /// 注册窗口与其平台实现。
    /// <para>
    /// 注册过程会调用 <paramref name="impl"/> 的 <c>SetNativeMessageHandler</c>，
    /// 将 <see cref="HandleIncomingAsync"/> 安装为消息回调。
    /// 之后前端通过原生 postMessage 发送的消息会自动路由到 <see cref="MessageProcessor"/>。
    /// </para>
    /// </summary>
    /// <param name="windowId">窗口 ID。</param>
    /// <param name="impl">平台特定的窗口实现（必须支持原生 postMessage 接收）。</param>
    public void RegisterWindow(uint windowId, IWebviewWindowImpl impl)
    {
        ArgumentNullException.ThrowIfNull(impl);
        _windowImpls[windowId] = impl;

        // 安装消息路由：平台 impl 接收到原生 postMessage 后调用 HandleIncomingAsync
        impl.SetNativeMessageHandler(message => HandleIncomingAsync(windowId, message));
    }

    /// <inheritdoc />
    public void UnregisterWindow(uint windowId)
    {
        if (_windowImpls.TryRemove(windowId, out var impl))
        {
            // 解绑消息回调，恢复默认路由（回到 Application.HandleMessageFromFrontend 路径）
            impl.SetNativeMessageHandler(null);
        }
    }

    /// <inheritdoc />
    public async Task PostToWindowAsync(uint windowId, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!_windowImpls.TryGetValue(windowId, out var impl))
        {
            return;
        }

        await impl.PostNativeMessageAsync(message);
    }

    /// <summary>
    /// 接收前端消息并转发到 <see cref="MessageProcessor"/>，将响应回推到前端。
    /// <para>
    /// 由 <c>IWebviewWindowImpl.SetNativeMessageHandler</c> 注册的回调调用。
    /// 调用方应在平台原生消息事件（如 WebView2 <c>WebMessageReceived</c>）中调用本方法。
    /// </para>
    /// </summary>
    /// <param name="windowId">来源窗口 ID。</param>
    /// <param name="message">前端发送的原始 JSON 字符串。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    public async Task HandleIncomingAsync(uint windowId, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var parsed = _processor.ParseMessage(message);
        if (parsed is null)
        {
            return;
        }

        // 注入窗口 ID（若消息未显式指定）
        if (parsed.WindowId is null)
        {
            parsed.WindowId = windowId;
        }

        var response = await _processor.ProcessAsync(parsed);
        if (response is null)
        {
            return;
        }

        // 将响应序列化为 JSON 并通过原生通道回推到前端
        var responseJson = JsonSerializer.Serialize(response, JsonOptions.DefaultSerializerOptions);
        await PostToWindowAsync(windowId, responseJson);
    }

    /// <summary>
    /// 实现 <see cref="IWailsEventListener.NotifyEvent"/>：将事件通过原生 postMessage 推送到所有已注册窗口。
    /// <para>
    /// 与 <see cref="EventIPCTransport"/> 互斥——应用启动时二选一。
    /// 当 <c>Application.UseNativeIpc = true</c> 时，由 <see cref="NativeIpcTransport"/> 替代
    /// <see cref="EventIPCTransport"/> 作为事件广播通道。
    /// </para>
    /// </summary>
    /// <param name="eventName">事件名称。</param>
    /// <param name="data">事件数据，可为 null。</param>
    public void NotifyEvent(string eventName, object? data)
    {
        if (_windowImpls.IsEmpty)
        {
            return;
        }

        // 构造事件消息：{ type: "event", name, data }
        // 前端通过 __wailsNative.onMessage 接收，按 type 分发到事件监听器
        var payload = new EventPayload(eventName, data);
        var json = JsonSerializer.Serialize(payload, JsonOptions.DefaultSerializerOptions);

        foreach (var windowId in _windowImpls.Keys)
        {
            _ = PostToWindowAsync(windowId, json);
        }
    }

    /// <summary>
    /// 事件推送载荷。
    /// </summary>
    private sealed class EventPayload
    {
        /// <summary>消息类型，固定为 "event"。</summary>
        public string Type => "event";

        /// <summary>事件名称。</summary>
        public string Name { get; }

        /// <summary>事件数据。</summary>
        public object? Data { get; }

        public EventPayload(string name, object? data)
        {
            Name = name;
            Data = data;
        }
    }
}
