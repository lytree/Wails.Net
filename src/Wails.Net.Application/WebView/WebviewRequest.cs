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
