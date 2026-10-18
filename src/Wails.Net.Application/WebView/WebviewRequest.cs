namespace Wails.Net.Application.Webview;

/// <summary>
/// WebView 请求的抽象（平台无关），由 Windows / Linux / macOS / Android
/// 各平台回调构建后传给 <see cref="WebviewRequestPipeline"/>。
/// <para>
/// 对应融合审计 §10.3 / R2-4：统一四平台 WebView 请求管线，消除重复的
/// IPC body 解析、AssetServer 调用、SPA 回退、CSP/nonce 注入与错误处理逻辑。
/// </para>
/// </summary>
public sealed class WebviewRequest
{
    /// <summary>HTTP 方法（GET / POST）。</summary>
    public required string Method { get; init; }

    /// <summary>绝对路径（如 <c>/wails/call</c>、<c>/app.js</c>）。</summary>
    public required string Path { get; init; }

    /// <summary>POST 请求体（IPC 消息）；GET 请求为 null。</summary>
    public string? RequestBody { get; init; }

    /// <summary>
    /// 原始请求体字节。供需要二进制 body 的端点（如 Streams 的 <c>/wails/stream/send</c>）使用，
    /// 以便平台在不经 UTF-8 文本转换的情况下传递任意字节。
    /// 为 null 时回退到 <see cref="RequestBody"/> 的 Latin-1 字节。
    /// </summary>
    public byte[]? RequestBodyBytes { get; init; }

    /// <summary>
    /// 请求头（大小写不敏感，默认空字典）。
    /// 供 Streams 等以 header 承载协议元数据的端点使用；键为原始 header 名。
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>发起请求的窗口 ID（用于消息路由）。</summary>
    public required uint WindowId { get; init; }

    /// <summary>窗口名称（用于 per-window CSP 注入，P0-4）。</summary>
    public string? WindowName { get; init; }

    /// <summary>调用来源 URL（用于 Origin 校验）。</summary>
    public string? Origin { get; init; }

    /// <summary>
    /// 判断是否为 IPC 请求（POST + <c>/wails/</c> 路径前缀）。
    /// 与 Wails v3 协议及四平台拦截规则保持一致。
    /// </summary>
    public bool IsIpcRequest =>
        string.Equals(Method, "POST", StringComparison.OrdinalIgnoreCase) &&
        Path.StartsWith("/wails/", StringComparison.OrdinalIgnoreCase);
}
