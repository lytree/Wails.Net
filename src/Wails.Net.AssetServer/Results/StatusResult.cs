using System.Net;

namespace Wails.Net.AssetServer.Results;

/// <summary>
/// 纯状态码响应结果，无响应体。
/// <para>
/// M8 新增。用于 304 Not Modified、404 Not Found、416 Range Not Satisfiable 等无响应体的场景。
/// </para>
/// </summary>
public class StatusResult : IAssetResult
{
    /// <summary>
    /// 获取 HTTP 状态码。
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// 获取内容类型。始终为 null（无响应体）。
    /// </summary>
    public string? ContentType => null;

    /// <summary>
    /// 使用指定状态码构造 <see cref="StatusResult" /> 实例。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码（如 304、404、416）。</param>
    public StatusResult(int statusCode)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// 异步将结果写入 HTTP 响应。
    /// 仅设置状态码，不写入响应体。
    /// </summary>
    /// <param name="response">HTTP 响应对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task WriteAsync(HttpListenerResponse response, CancellationToken cancellationToken = default)
    {
        response.StatusCode = StatusCode;
        response.ContentLength64 = 0;
        return Task.CompletedTask;
    }
}
