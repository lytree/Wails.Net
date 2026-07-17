using System.Net;
using System.Text;

namespace Wails.Net.AssetServer.Results;

/// <summary>
/// 错误响应结果，携带文本错误消息。
/// <para>
/// M8 新增。用于 500 Internal Server Error 等错误场景。
/// 响应体为 UTF-8 编码的错误消息文本。
/// </para>
/// </summary>
public class ErrorResult : IAssetResult
{
    /// <summary>
    /// 获取 HTTP 状态码（默认 500）。
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// 获取内容类型。始终为 "text/plain; charset=utf-8"。
    /// </summary>
    public string? ContentType => "text/plain; charset=utf-8";

    /// <summary>
    /// 获取错误消息。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 使用指定消息和状态码构造 <see cref="ErrorResult" /> 实例。
    /// </summary>
    /// <param name="message">错误消息。</param>
    /// <param name="statusCode">HTTP 状态码（默认 500）。</param>
    public ErrorResult(string message, int statusCode = 500)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        Message = message;
        StatusCode = statusCode;
    }

    /// <summary>
    /// 异步将结果写入 HTTP 响应。
    /// 设置状态码、Content-Type 并写入 UTF-8 编码的错误消息。
    /// </summary>
    /// <param name="response">HTTP 响应对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task WriteAsync(HttpListenerResponse response, CancellationToken cancellationToken = default)
    {
        response.StatusCode = StatusCode;
        response.ContentType = ContentType;

        var bytes = Encoding.UTF8.GetBytes(Message);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
    }
}
