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
    /// 创建 200 响应。
    /// </summary>
    public static WebviewResponse Ok(string contentType, byte[] body) =>
        new()
        {
            StatusCode = 200,
            StatusText = "OK",
            ContentType = contentType,
            Body = body
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
    public static WebviewResponse MethodNotAllowed() =>
        new()
        {
            StatusCode = 405,
            StatusText = "Method Not Allowed",
            ContentType = "text/plain; charset=utf-8",
            Body = "Method Not Allowed"u8.ToArray()
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
