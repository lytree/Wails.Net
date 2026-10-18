using System.Text.Json;
using Wails.Net.Application.Bindings;

namespace Wails.Net.Application.Webview;

/// <summary>
/// WebView 响应的抽象（平台无关），由 <see cref="WebviewRequestPipeline"/> 返回，
/// 各平台原生回调负责转换为对应的 <c>CoreWebView2WebResourceResponse</c> /
/// <c>URISchemeResponse</c> / <c>NSUrlResponse</c> / <c>WebResourceResponse</c>。
/// <para>
/// 对应融合审计 §10.3 / R2-4：所有平台共用同一份响应语义（状态码、Content-Type、Body、Headers）。
/// </para>
/// </summary>
public sealed class WebviewResponse
{
    /// <summary>HTTP 状态码。</summary>
    public required int StatusCode { get; init; }

    /// <summary>状态描述（默认 "OK"）。</summary>
    public string StatusText { get; init; } = "OK";

    /// <summary>MIME 类型。</summary>
    public required string ContentType { get; init; }

    /// <summary>响应体字节。</summary>
    public required byte[] Body { get; init; }

    /// <summary>
    /// 追加响应头（可选）。键为 header 名，查找大小写不敏感。
    /// 供 Streams 端点返回 <c>Retry-After</c> / <c>Allow</c> / <c>Cache-Control</c> 等。
    /// 为 null 表示无追加头。各平台原生回调应将其逐项写入响应。
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// 创建 200 响应。
    /// </summary>
    /// <param name="contentType">MIME 类型。</param>
    /// <param name="body">响应体。</param>
    /// <param name="headers">可选追加响应头。</param>
    public static WebviewResponse Ok(string contentType, byte[] body, IReadOnlyDictionary<string, string>? headers = null) =>
        new()
        {
            StatusCode = 200,
            StatusText = "OK",
            ContentType = contentType,
            Body = body,
            Headers = headers
        };

    /// <summary>
    /// 创建 204 No Content 响应（空体）。
    /// </summary>
    /// <param name="headers">可选追加响应头。</param>
    public static WebviewResponse NoContent(IReadOnlyDictionary<string, string>? headers = null) =>
        new()
        {
            StatusCode = 204,
            StatusText = "No Content",
            ContentType = "text/plain; charset=utf-8",
            Body = [],
            Headers = headers
        };

    /// <summary>
    /// 创建 404 响应。
    /// </summary>
    public static WebviewResponse NotFound() =>
        new()
        {
            StatusCode = 404,
            StatusText = "Not Found",
            ContentType = "text/plain; charset=utf-8",
            Body = "Not Found"u8.ToArray()
        };

    /// <summary>
    /// 创建 405 响应（方法不允许）。
    /// </summary>
    /// <param name="allow">可选 <c>Allow</c> header 值（如 <c>"GET"</c>）。</param>
    public static WebviewResponse MethodNotAllowed(string? allow = null) =>
        new()
        {
            StatusCode = 405,
            StatusText = "Method Not Allowed",
            ContentType = "text/plain; charset=utf-8",
            Body = "Method Not Allowed"u8.ToArray(),
            Headers = allow is null
                ? null
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Allow"] = allow }
        };

    /// <summary>
    /// 创建 400 响应。
    /// </summary>
    /// <param name="message">错误消息（作为响应体）。</param>
    public static WebviewResponse BadRequest(string message) =>
        Text(400, "Bad Request", message);

    /// <summary>
    /// 创建 403 响应。
    /// </summary>
    /// <param name="message">错误消息（作为响应体）。</param>
    public static WebviewResponse Forbidden(string message) =>
        Text(403, "Forbidden", message);

    /// <summary>
    /// 创建 409 响应。
    /// </summary>
    /// <param name="message">错误消息（作为响应体）。</param>
    public static WebviewResponse Conflict(string message) =>
        Text(409, "Conflict", message);

    /// <summary>
    /// 创建 410 Gone 响应。Streams 用其表达「会话已消失」——告知客户端停止轮询而非永远重试。
    /// </summary>
    /// <param name="message">错误消息（作为响应体）。</param>
    public static WebviewResponse Gone(string message) =>
        Text(410, "Gone", message);

    /// <summary>
    /// 创建 429 Too Many Requests 响应，并附带 <c>Retry-After: 0</c>（告知客户端立即重试同一帧）。
    /// </summary>
    /// <param name="message">错误消息（作为响应体）。</param>
    /// <param name="retryAfter">Retry-After header 值；默认为 <c>"0"</c>。</param>
    public static WebviewResponse TooManyRequests(string message, string retryAfter = "0") =>
        Text(
            429,
            "Too Many Requests",
            message,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Retry-After"] = retryAfter });

    /// <summary>
    /// 创建纯文本响应，可附带自定义状态与响应头。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="statusText">状态描述。</param>
    /// <param name="body">响应体文本（UTF-8）。</param>
    /// <param name="headers">可选追加响应头。</param>
    public static WebviewResponse Text(int statusCode, string statusText, string body, IReadOnlyDictionary<string, string>? headers = null) =>
        new()
        {
            StatusCode = statusCode,
            StatusText = statusText,
            ContentType = "text/plain; charset=utf-8",
            Body = System.Text.Encoding.UTF8.GetBytes(body),
            Headers = headers
        };

    /// <summary>
    /// 创建 500 响应。
    /// </summary>
    public static WebviewResponse InternalError(string message) =>
        new()
        {
            StatusCode = 500,
            StatusText = "Internal Server Error",
            ContentType = "text/plain; charset=utf-8",
            Body = System.Text.Encoding.UTF8.GetBytes(message)
        };

    /// <summary>
    /// 创建 application/json 响应（默认序列化器）。
    /// </summary>
    public static WebviewResponse Json(object payload) =>
        Ok(
            "application/json",
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions.DefaultSerializerOptions)));

    /// <summary>
    /// 创建空 JSON 响应（<c>{"result":null,"error":null}</c>）。
    /// 与 Wails v3 协议兼容；用于前端无响应等待场景。
    /// </summary>
    public static WebviewResponse JsonEmpty() => Json(new
    {
        result = (object?)null,
        error = (object?)null
    });
}
