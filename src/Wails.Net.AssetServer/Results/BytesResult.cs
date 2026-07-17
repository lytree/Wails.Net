using System.Globalization;
using System.Net;
using System.Text;

namespace Wails.Net.AssetServer.Results;

/// <summary>
/// 字节数组响应结果，用于 200 完整响应和 206 Range 部分响应。
/// <para>
/// M8 新增。携带内容字节组、MIME 类型、ETag、Last-Modified 及可选的 Range 信息。
/// <see cref="WriteAsync"/> 负责设置所有必要的 HTTP 头并写入响应体。
/// </para>
/// </summary>
public class BytesResult : IAssetResult
{
    /// <summary>
    /// Range 写入缓冲区大小（80KB，与 .NET FileStream 默认一致）。
    /// </summary>
    private const int BufferSize = 81920;

    /// <summary>
    /// 获取 HTTP 状态码（200 或 206）。
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// 获取内容类型（MIME）。
    /// </summary>
    public string? ContentType { get; }

    /// <summary>
    /// 获取资源内容字节组。
    /// </summary>
    public byte[] Content { get; }

    /// <summary>
    /// 获取 ETag 值（带双引号），用于协商缓存。
    /// </summary>
    public string? ETag { get; }

    /// <summary>
    /// 获取最后修改时间（UTC），用于设置 Last-Modified 头。
    /// </summary>
    public DateTime? LastModified { get; }

    /// <summary>
    /// 获取 Range 信息（Offset, Length）。非 null 时表示 206 部分响应。
    /// </summary>
    public (long Offset, long Length)? Range { get; }

    /// <summary>
    /// 获取资源的完整长度（用于 206 响应的 Content-Range 头）。
    /// </summary>
    public long TotalLength { get; }

    /// <summary>
    /// 使用指定内容构造 <see cref="BytesResult" /> 实例（200 响应）。
    /// </summary>
    /// <param name="content">资源内容字节组。</param>
    /// <param name="contentType">内容类型（MIME）。</param>
    /// <param name="eTag">ETag 值（带双引号）。</param>
    /// <param name="lastModified">最后修改时间（UTC）。</param>
    public BytesResult(byte[] content, string? contentType, string? eTag = null, DateTime? lastModified = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        ContentType = contentType;
        ETag = eTag;
        LastModified = lastModified;
        StatusCode = 200;
        TotalLength = content.Length;
    }

    /// <summary>
    /// 使用指定内容和 Range 信息构造 <see cref="BytesResult" /> 实例（206 响应）。
    /// </summary>
    /// <param name="content">资源完整内容字节组。</param>
    /// <param name="contentType">内容类型（MIME）。</param>
    /// <param name="offset">Range 偏移量。</param>
    /// <param name="length">Range 长度。</param>
    /// <param name="eTag">ETag 值（带双引号）。</param>
    /// <param name="lastModified">最后修改时间（UTC）。</param>
    public BytesResult(byte[] content, string? contentType, long offset, long length, string? eTag = null, DateTime? lastModified = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        ContentType = contentType;
        ETag = eTag;
        LastModified = lastModified;
        StatusCode = 206;
        Range = (offset, length);
        TotalLength = content.Length;
    }

    /// <summary>
    /// 异步将结果写入 HTTP 响应。
    /// 设置状态码、Content-Type、ETag、Last-Modified、Cache-Control、Accept-Ranges 头，
    /// 并写入响应体（完整内容或 Range 部分）。
    /// </summary>
    /// <param name="response">HTTP 响应对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task WriteAsync(HttpListenerResponse response, CancellationToken cancellationToken = default)
    {
        response.StatusCode = StatusCode;

        // 设置 Content-Type
        if (!string.IsNullOrEmpty(ContentType))
        {
            response.ContentType = ContentType;
        }

        // 设置 ETag
        if (ETag is not null)
        {
            response.Headers[AssetServer.Headers.ETag] = ETag;
        }

        // 设置 Last-Modified（RFC 1123 格式）
        if (LastModified.HasValue)
        {
            response.Headers[AssetServer.Headers.LastModified] =
                LastModified.Value.ToString("R", CultureInfo.InvariantCulture);
        }

        // 设置缓存控制
        response.Headers[AssetServer.Headers.CacheControl] = "no-cache";
        response.Headers[AssetServer.Headers.AcceptRanges] = "bytes";

        if (Range.HasValue)
        {
            // 206 Partial Content
            var (offset, length) = Range.Value;
            response.Headers[AssetServer.Headers.ContentRange] =
                $"bytes {offset}-{offset + length - 1}/{TotalLength}";
            response.ContentLength64 = length;

            // 分块写入 Range 内容
            await WriteRangeAsync(response, offset, length, cancellationToken);
        }
        else
        {
            // 200 完整内容
            response.ContentLength64 = Content.Length;
            await response.OutputStream.WriteAsync(Content.AsMemory(0, Content.Length), cancellationToken);
        }
    }

    /// <summary>
    /// 分块写入 Range 内容（使用 81920 字节缓冲区）。
    /// </summary>
    /// <param name="response">HTTP 响应对象。</param>
    /// <param name="offset">起始偏移量。</param>
    /// <param name="length">写入长度。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task WriteRangeAsync(HttpListenerResponse response, long offset, long length, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(length, BufferSize)];
        var remaining = length;
        var currentOffset = offset;

        while (remaining > 0)
        {
            var toRead = (int)Math.Min(remaining, buffer.Length);
            Buffer.BlockCopy(Content, (int)currentOffset, buffer, 0, toRead);
            await response.OutputStream.WriteAsync(buffer.AsMemory(0, toRead), cancellationToken);
            currentOffset += toRead;
            remaining -= toRead;
        }
    }
}
