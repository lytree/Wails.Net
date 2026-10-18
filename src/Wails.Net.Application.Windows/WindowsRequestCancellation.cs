using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace Wails.Net.Application.Platform;

/// <summary>
/// 单个 WebView2 的 Windows 逐请求取消（CDP <c>Fetch</c>/<c>Network</c>）。
/// 对应 Wails v3 Go 版本 <c>pkg/application/request_cancellation_windows.go</c> 中的
/// <c>windowsRequestCancellation</c> 结构及其 <c>enableSession</c>/<c>resumeRequest</c>/<c>close</c> 逻辑。
/// </summary>
/// <remarks>
/// <para>
/// 机制（与 Go 对齐）：
/// <list type="number">
/// <item>经 CDP <c>Fetch.enable</c> 仅拦截自定义 scheme（<c>http://wails.localhost/*</c>）的请求；</item>
/// <item><c>Fetch.requestPaused</c> 时为该请求登记一个不可猜测的私有标识
/// （经 <c>Fetch.continueRequest</c> 注入 <see cref="RequestIdHeader"/> 请求头）后立即放行；</item>
/// <item>CDP <c>Network</c> 终态事件（<c>loadingFailed</c> 表示浏览器中止、<c>loadingFinished</c> 表示完成）
/// 按请求身份取消对应令牌，使在途请求管线（尤其是 <c>/wails/stream/poll</c> 长轮询）得以提前结束，
/// 而不再依赖「响应前仅以 204 收尾」。</item>
/// </list>
/// </para>
/// <para>
/// 与 Go 的差异（本次显式未实现，属独立子特性）：Go 版本还实现了 Keepalive 绑定脚本与
/// <c>Target.setAutoAttach</c> 子会话（worker）跟踪；本实现在根会话启用 <c>Fetch</c>，
/// 仍可拦截并放行 worker 请求，但不跟踪子会话的 <c>Network</c> 中止事件。
/// </para>
/// <para>
/// 影响面：未取消请求行为不变——请求头仅多一个私有标识，且该标识在进入
/// <c>WebviewRequestPipeline</c> 之前被移除，响应与响应体完全一致；
/// <c>Fetch</c> 拦截范围严格限定为 <c>wails.localhost</c> 的 <c>Request</c> 阶段，
/// 不涉及外部主机、其它 scheme 或 <c>Response</c> 阶段。
/// </para>
/// <para>
/// 验收方式：本机无 GUI，无法端到端验证，**以构建 + 代码审查验收**。
/// 若 <c>Fetch.enable</c> 在运行时不支持该域，调用会失败并降级为「不启用拦截」，
/// 退回既有 204 语义，不向用户暴露错误。
/// </para>
/// </remarks>
internal sealed class WindowsRequestCancellation
{
    /// <summary>
    /// 私有桥接标识请求头，对应 Go <c>internal/assetserver/webview/request_tracker.go</c>
    /// 的 <c>RequestIDHeader</c>（<c>X-Wails-Request-Id</c>）。
    /// 由 CDP <c>Fetch.continueRequest</c> 注入，进入请求管线前被移除。
    /// </summary>
    internal const string RequestIdHeader = "X-Wails-Request-Id";

    /// <summary>CDP <c>Fetch.enable</c> 方法名。</summary>
    private const string MethodFetchEnable = "Fetch.enable";

    /// <summary>CDP <c>Fetch.disable</c> 方法名。</summary>
    private const string MethodFetchDisable = "Fetch.disable";

    /// <summary>CDP <c>Fetch.continueRequest</c> 方法名。</summary>
    private const string MethodFetchContinue = "Fetch.continueRequest";

    /// <summary>
    /// CDP <c>Fetch.enable</c> 参数。
    /// 与 Go <c>request_cancellation_windows.go:86</c> 的两条 pattern 完全一致：
    /// 只拦截 <c>http://wails.localhost</c> 的请求阶段，避免影响其它主机与阶段。
    /// </summary>
    private const string FetchEnableParameters =
        "{\"patterns\":[" +
        "{\"urlPattern\":\"http://wails.localhost/*\",\"requestStage\":\"Request\"}," +
        "{\"urlPattern\":\"http://wails.localhost:*/*\",\"requestStage\":\"Request\"}]}";

    /// <summary>CDP 空参数（<c>Network.enable</c> / <c>Fetch.disable</c>）。</summary>
    private const string EmptyParameters = "{}";

    /// <summary>JSON 反序列化选项。</summary>
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 预置的「已取消」令牌。用于「非空标识但无对应登记」的场景
    /// （对应 Go <c>request_tracker.go</c> 的 <c>Wrap</c> 中对缺失/非法标识
    /// 返回立即取消上下文的语义），确保被提前中止的请求不会被复活。
    /// 该源永不释放，作为进程级常量使用。
    /// </summary>
    private static readonly CancellationToken _cancelledToken = CreateCancelledToken();

    private readonly CoreWebView2 _webview;
    private readonly ILogger? _logger;

    /// <summary>
    /// 窗口级取消令牌（<c>Win32WebviewWindow._requestCancellation</c>）。
    /// 各请求令牌链接到该令牌，窗口关闭时一并取消，对应 Go 中 tracker 的父上下文。
    /// </summary>
    private readonly CancellationToken _windowToken;

    /// <summary>保护登记字典与 <see cref="_closed"/> 的同步锁（CDP 回调可能来自不同线程）。</summary>
    private readonly object _sync = new();

    /// <summary>按 CDP <c>networkId</c> 登记的请求（对应 Go <c>RequestTracker.byID</c>）。</summary>
    private readonly Dictionary<string, TrackedRequest> _byNetworkId = new(StringComparer.Ordinal);

    /// <summary>按私有标识登记的请求（对应 Go <c>RequestTracker.byToken</c>）。</summary>
    private readonly Dictionary<string, TrackedRequest> _byToken = new(StringComparer.Ordinal);

    /// <summary>已订阅的 CDP 事件接收器，用于关闭时退订。</summary>
    private readonly List<(CoreWebView2DevToolsProtocolEventReceiver Receiver,
        EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> Handler)> _subscriptions = [];

    /// <summary>会话是否已关闭（此时不再调用 CDP，也不再登记请求）。</summary>
    private bool _closed;

    /// <summary>
    /// 创建某窗口的逐请求取消会话。
    /// </summary>
    /// <param name="webview">已初始化的 WebView2 核心实例（不得为 null；窗口关闭后不得再传入）。</param>
    /// <param name="windowToken">
    /// 窗口级取消令牌（<c>Win32WebviewWindow._requestCancellation</c>）。
    /// 各请求令牌链接到该令牌，窗口关闭时一并取消，对应 Go 中 tracker 的父上下文。
    /// </param>
    /// <param name="logger">日志记录器，可为 null。</param>
    internal WindowsRequestCancellation(CoreWebView2 webview, CancellationToken windowToken, ILogger? logger)
    {
        _webview = webview;
        _windowToken = windowToken;
        _logger = logger;
    }

    /// <summary>
    /// 启动 CDP 逐请求取消：订阅事件并在根会话启用 <c>Network</c>/<c>Fetch</c>。
    /// 必须在首次导航之前调用，才能覆盖初始页面加载产生的自定义 scheme 请求。
    /// 对应 Go <c>startRequestCancellation</c> + <c>enableSession("")</c>。
    /// </summary>
    internal void Start()
    {
        if (_closed)
        {
            return;
        }

        // 必须先订阅事件，再启用 Fetch，避免漏掉首批 Fetch.requestPaused。
        // 三项订阅全部成功才启用拦截：否则暂停的请求可能永远无法放行
        // （对应 Go subscribe 失败即整体放弃启用，不调用 Fetch.enable）。
        var subscribed = Subscribe("Fetch.requestPaused", OnFetchRequestPaused)
            & Subscribe("Network.loadingFailed", OnNetworkTerminal)
            & Subscribe("Network.loadingFinished", OnNetworkTerminal);
        if (!subscribed)
        {
            Disable();
            return;
        }

        // Network 为被动观测（只收事件、不改变请求流转）；Fetch 为限定范围的拦截。
        _ = CallAsync("Network.enable", EmptyParameters);
        _ = CallAsync(MethodFetchEnable, FetchEnableParameters);
    }

    /// <summary>
    /// 消费请求携带的私有标识，返回对应的在途取消令牌。
    /// 对应 Go <c>RequestTracker.Wrap</c>：
    /// <list type="bullet">
    /// <item>无标识（空/为 null）→ 返回 null，表示该请求不纳入 CDP 跟踪，行为与改造前一致；</item>
    /// <item>标识命中且未被认领 → 标记为已认领并返回其令牌（浏览器中止时该令牌会被取消）；</item>
    /// <item>标识非空但无对应登记 → 请求在拦截回调前已被中止（或标识非法），
    /// 返回预置的已取消令牌，确保不复活该请求。</item>
    /// </list>
    /// </summary>
    /// <param name="token">从请求头读取到的私有标识，可为 null。</param>
    /// <returns>需要并入请求取消源的令牌；未纳入跟踪时返回 null。</returns>
    internal CancellationToken? Consume(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        lock (_sync)
        {
            if (!_closed && _byToken.TryGetValue(token, out var entry) && !entry.Claimed)
            {
                entry.Claimed = true;
                return entry.Cancellation.Token;
            }
        }

        return _cancelledToken;
    }

    /// <summary>
    /// 关闭会话：释放 CDP 拦截、退订事件并取消全部在途请求。
    /// 对应 Go <c>windowsRequestCancellation.close</c>（含 <c>Fetch.disable</c> 与退订）。
    /// 必须在窗口销毁、<c>_webview</c> 置空之前、于 UI 线程调用。
    /// </summary>
    internal void Close()
    {
        if (_closed)
        {
            return;
        }

        // Fetch.disable 会释放任何仍处于暂停状态的请求（即使启用只完成了一半）。
        _ = CallAsync(MethodFetchDisable, EmptyParameters);

        Disable();
    }

    /// <summary>
    /// 仅做本地清理（不调用 CDP），可从任意线程调用且幂等。
    /// 用于 <c>Fetch.enable</c> 失败后的降级，以及 <see cref="Close"/> 的收尾。
    /// </summary>
    private void Disable()
    {
        List<TrackedRequest> pending;
        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            pending = [.. _byNetworkId.Values];
            _byNetworkId.Clear();
            _byToken.Clear();
        }

        // 在锁外取消，避免在持有 _sync 时同步触发管线回调。
        foreach (var entry in pending)
        {
            entry.Cancellation.Cancel();
        }

        foreach (var (receiver, handler) in _subscriptions)
        {
            try
            {
                receiver.DevToolsProtocolEventReceived -= handler;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "退订 WebView2 CDP 事件失败。");
            }
        }

        _subscriptions.Clear();
    }

    /// <summary>
    /// 订阅单个 CDP 事件。对应 Go <c>subscribe</c>（<c>OnDevTools</c>）。
    /// </summary>
    /// <param name="eventName">CDP 事件全名（<c>{domain}.{event}</c>）。</param>
    /// <param name="handler">事件处理委托。</param>
    /// <returns>订阅成功返回 true；运行时不支持该事件时返回 false 并静默降级。</returns>
    private bool Subscribe(
        string eventName,
        EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> handler)
    {
        try
        {
            var receiver = _webview.GetDevToolsProtocolEventReceiver(eventName);
            receiver.DevToolsProtocolEventReceived += handler;
            _subscriptions.Add((receiver, handler));
            return true;
        }
        catch (Exception ex)
        {
            // 运行时不支持该事件时静默降级：不启用拦截，保持既有 204 语义。
            _logger?.LogDebug(ex, "订阅 WebView2 CDP 事件失败：{Event}", eventName);
            return false;
        }
    }

    /// <summary>
    /// 处理 CDP <c>Fetch.requestPaused</c>：登记请求标识并立即放行。
    /// 对应 Go <c>resumeRequest</c>（<c>Fetch.continueRequest</c>）。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="args">CDP 事件参数。</param>
    private void OnFetchRequestPaused(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        if (_closed)
        {
            return;
        }

        PausedRequest? paused;
        try
        {
            paused = JsonSerializer.Deserialize<PausedRequest>(args.ParameterObjectAsJson, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger?.LogDebug(ex, "解析 WebView2 Fetch.requestPaused 失败。");
            return;
        }

        if (paused is null || string.IsNullOrEmpty(paused.RequestId))
        {
            return;
        }

        // 登记请求身份（对应 Go tracker.Start(networkId)），重定向复用 networkId 时替换旧登记。
        var token = Register(paused.NetworkId);

        // 重建请求头：剔除任何既有的私有标识，注入本次令牌（对应 Go resumeRequest 的 headers 组装）。
        var headers = new List<HeaderEntry>();
        if (paused.Request?.Headers is { } original)
        {
            foreach (var pair in original)
            {
                if (!string.Equals(pair.Key, RequestIdHeader, StringComparison.OrdinalIgnoreCase))
                {
                    headers.Add(new HeaderEntry(pair.Key, pair.Value));
                }
            }
        }

        if (token is not null)
        {
            headers.Add(new HeaderEntry(RequestIdHeader, token));
        }

        // 必须立即放行，否则该请求将永久挂起。
        var parameters = JsonSerializer.Serialize(
            new ContinueRequestParameters(paused.RequestId, headers), _jsonOptions);
        _ = CallAsync(MethodFetchContinue, parameters);
    }

    /// <summary>
    /// 处理 CDP <c>Network.loadingFailed</c> / <c>Network.loadingFinished</c>：
    /// 按请求身份取消对应令牌。
    /// 对应 Go <c>event</c> 中非 <c>requestWillBeSent</c> 分支的 <c>tracker.Cancel(requestId)</c>。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="args">CDP 事件参数。</param>
    private void OnNetworkTerminal(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs args)
    {
        if (_closed)
        {
            return;
        }

        string? requestId = null;
        try
        {
            using var document = JsonDocument.Parse(args.ParameterObjectAsJson);
            if (document.RootElement.TryGetProperty("requestId", out var id))
            {
                requestId = id.GetString();
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogDebug(ex, "解析 WebView2 Network 终态事件失败。");
        }

        if (!string.IsNullOrEmpty(requestId))
        {
            CancelById(requestId);
        }
    }

    /// <summary>
    /// 登记一个在途请求，返回其私有标识。
    /// 对应 Go <c>RequestTracker.start</c>：重定向可能复用 <c>networkId</c>，先移除旧登记。
    /// </summary>
    /// <param name="networkId">CDP 请求网络标识。</param>
    /// <returns>私有标识；无法登记（会话已关闭或标识为空）时返回 null。</returns>
    private string? Register(string? networkId)
    {
        if (string.IsNullOrEmpty(networkId))
        {
            return null;
        }

        TrackedRequest? superseded = null;
        string token;
        lock (_sync)
        {
            if (_closed)
            {
                return null;
            }

            if (_byNetworkId.TryGetValue(networkId, out superseded))
            {
                _byNetworkId.Remove(networkId);
                _byToken.Remove(superseded.Token);
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_windowToken);
            var entry = new TrackedRequest(networkId, Guid.NewGuid().ToString("N"), cancellation);
            _byNetworkId[networkId] = entry;
            _byToken[entry.Token] = entry;
            token = entry.Token;
        }

        // 旧请求（被重定向取代）在锁外取消。
        superseded?.Cancellation.Cancel();
        return token;
    }

    /// <summary>
    /// 按 CDP 请求标识取消并移除登记。
    /// 对应 Go <c>RequestTracker.Cancel</c>。
    /// </summary>
    /// <param name="networkId">CDP 请求网络标识。</param>
    private void CancelById(string networkId)
    {
        TrackedRequest? entry;
        lock (_sync)
        {
            if (!_byNetworkId.TryGetValue(networkId, out entry))
            {
                return;
            }

            _byNetworkId.Remove(networkId);
            _byToken.Remove(entry.Token);
        }

        // 只取消、不释放：请求管线可能仍持有由该令牌派生的链接取消源，
        // 由管线的 Dispose 释放注册，避免释放竞态。
        entry.Cancellation.Cancel();
    }

    /// <summary>
    /// 在 UI 线程外发的 CDP 调用（结果与错误仅用于日志，不再回调 WebView2）。
    /// 对应 Go <c>call</c> / <c>CallDevToolsProtocolMethod</c>。
    /// </summary>
    /// <param name="method">CDP 方法全名（<c>{domain}.{method}</c>）。</param>
    /// <param name="parameters">JSON 参数。</param>
    /// <returns>调用任务。</returns>
    private async Task CallAsync(string method, string parameters)
    {
        // 守卫：窗口未初始化或已关闭时不得调用 CDP。
        if (_closed)
        {
            return;
        }

        try
        {
            await _webview.CallDevToolsProtocolMethodAsync(method, parameters).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "WebView2 CDP 调用失败：{Method}", method);

            // Fetch 拦截不可用时立即降级：不再登记请求、不再注入标识头，
            // 退回既有「窗口级取消 + 204」语义。
            if (string.Equals(method, MethodFetchEnable, StringComparison.Ordinal))
            {
                Disable();
            }
        }
    }

    /// <summary>
    /// 创建一个立即处于取消状态的令牌（用于预置的 <see cref="_cancelledToken"/>）。
    /// </summary>
    /// <returns>已取消的令牌。</returns>
    private static CancellationToken CreateCancelledToken()
    {
        var source = new CancellationTokenSource();
        source.Cancel();
        return source.Token;
    }

    /// <summary>
    /// 已登记的在途请求（对应 Go <c>request_tracker.go</c> 的 <c>trackedRequest</c>）。
    /// </summary>
    /// <param name="networkId">CDP 请求网络标识。</param>
    /// <param name="token">不可猜测的私有标识。</param>
    /// <param name="cancellation">该请求的取消源（链接到窗口级令牌）。</param>
    private sealed class TrackedRequest(
        string networkId, string token, CancellationTokenSource cancellation)
    {
        /// <summary>CDP 请求网络标识。</summary>
        public string NetworkId { get; } = networkId;

        /// <summary>私有标识。</summary>
        public string Token { get; } = token;

        /// <summary>该请求的取消源。</summary>
        public CancellationTokenSource Cancellation { get; } = cancellation;

        /// <summary>是否已被请求管线认领（对应 Go <c>trackedRequest.claimed</c>）。</summary>
        public bool Claimed { get; set; }
    }

    /// <summary>CDP <c>Fetch.requestPaused</c> 事件参数。</summary>
    private sealed class PausedRequest
    {
        /// <summary>本次暂停的请求标识（用于 <c>Fetch.continueRequest</c>）。</summary>
        [JsonPropertyName("requestId")]
        public string? RequestId { get; init; }

        /// <summary>网络层请求标识（用于与 <c>Network</c> 事件关联）。</summary>
        [JsonPropertyName("networkId")]
        public string? NetworkId { get; init; }

        /// <summary>原始请求信息。</summary>
        [JsonPropertyName("request")]
        public PausedRequestData? Request { get; init; }
    }

    /// <summary>CDP <c>Fetch.requestPaused</c> 中的请求信息。</summary>
    private sealed class PausedRequestData
    {
        /// <summary>原始请求头。</summary>
        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; init; }
    }

    /// <summary>CDP 请求头键值对（用于 <c>Fetch.continueRequest</c> 参数）。</summary>
    /// <param name="name">请求头名称。</param>
    /// <param name="value">请求头值。</param>
    private sealed class HeaderEntry(string name, string value)
    {
        /// <summary>请求头名称。</summary>
        [JsonPropertyName("name")]
        public string Name { get; } = name;

        /// <summary>请求头值。</summary>
        [JsonPropertyName("value")]
        public string Value { get; } = value;
    }

    /// <summary>CDP <c>Fetch.continueRequest</c> 参数。</summary>
    /// <param name="requestId">本次暂停的请求标识。</param>
    /// <param name="headers">重建后的请求头。</param>
    private sealed class ContinueRequestParameters(string requestId, List<HeaderEntry> headers)
    {
        /// <summary>本次暂停的请求标识。</summary>
        [JsonPropertyName("requestId")]
        public string RequestId { get; } = requestId;

        /// <summary>重建后的请求头。</summary>
        [JsonPropertyName("headers")]
        public List<HeaderEntry> Headers { get; } = headers;
    }
}
