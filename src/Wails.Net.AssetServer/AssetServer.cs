using System.Net;
using System.Security.Cryptography;
using System.Text;
using Wails.Net.AssetServer.Middleware;

namespace Wails.Net.AssetServer;

/// <summary>
/// 资源服务器，处理 HTTP 请求并提供静态资源服务。
/// 对应 Wails v3 Go 版本 <c>internal/assetserver/assetserver.go</c> 中的 AssetServer 结构。
/// 支持 Range 请求（部分内容）、ETag 缓存、404 处理及中间件链拦截。
/// </summary>
public class AssetServer
{
    /// <summary>
    /// 常用 HTTP 头部名称，与 Wails v3 Go 版本 <c>common.go</c> 一致。
    /// </summary>
    public static class Headers
    {
        /// <summary>Content-Type 头。</summary>
        public const string ContentType = "Content-Type";

        /// <summary>Content-Length 头。</summary>
        public const string ContentLength = "Content-Length";

        /// <summary>Content-Range 头。</summary>
        public const string ContentRange = "Content-Range";

        /// <summary>Accept-Ranges 头。</summary>
        public const string AcceptRanges = "Accept-Ranges";

        /// <summary>Range 头。</summary>
        public const string Range = "Range";

        /// <summary>ETag 头。</summary>
        public const string ETag = "ETag";

        /// <summary>If-None-Match 头。</summary>
        public const string IfNoneMatch = "If-None-Match";

        /// <summary>Last-Modified 头。</summary>
        public const string LastModified = "Last-Modified";

        /// <summary>If-Modified-Since 头。</summary>
        public const string IfModifiedSince = "If-Modified-Since";

        /// <summary>Cache-Control 头。</summary>
        public const string CacheControl = "Cache-Control";

        /// <summary>Access-Control-Allow-Origin 头（CORS）。</summary>
        public const string AccessControlAllowOrigin = "Access-Control-Allow-Origin";

        /// <summary>X-Wails-Window-Id 头。</summary>
        public const string WindowId = "x-wails-window-id";

        /// <summary>X-Wails-Window-Name 头。</summary>
        public const string WindowName = "x-wails-window-name";
    }

    /// <summary>
    /// 资源服务器选项。
    /// </summary>
    private readonly AssetOptions _options;

    /// <summary>
    /// 中间件链，用于按顺序处理资源请求。
    /// </summary>
    private readonly MiddlewareChain _middlewareChain = new();

    /// <summary>
    /// 获取资源服务器选项。
    /// </summary>
    public AssetOptions Options => _options;

    /// <summary>
    /// 使用指定选项构造 <see cref="AssetServer" /> 实例。
    /// </summary>
    /// <param name="options">资源服务器选项。</param>
    public AssetServer(AssetOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// 添加基于路径的中间件到处理链。
    /// </summary>
    /// <param name="middleware">要添加的中间件实例。</param>
    public void Use(IMiddleware middleware)
    {
        _middlewareChain.Use(middleware);
    }

    /// <summary>
    /// 添加基于 HTTP 上下文的中间件到处理链。
    /// </summary>
    /// <param name="middleware">要添加的 HTTP 中间件实例。</param>
    public void Use(IHttpMiddleware middleware)
    {
        _middlewareChain.Use(middleware);
    }

    /// <summary>
    /// 根据路径异步返回资源内容。
    /// 通过中间件链处理请求，最终由派生类提供的资源读取逻辑兜底。
    /// </summary>
    /// <param name="path">请求的资源路径。</param>
    /// <returns>资源内容字节组；若资源不存在则返回空字节数组。</returns>
    public virtual async Task<byte[]> ServeAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var result = await _middlewareChain.ExecuteAsync(path, p => Task.FromResult(ReadAssetCore(p)));
        return result ?? [];
    }

    /// <summary>
    /// 处理完整的 HTTP 请求，包括中间件链、Range 请求、ETag 缓存和 404 处理。
    /// 对应 Wails v3 Go 版本 AssetServer.ServeHTTP 方法。
    /// </summary>
    /// <param name="context">HTTP 请求上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    public virtual async Task ServeHttpAsync(HttpListenerContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        var response = context.Response;

        // 添加 CORS 响应头
        response.Headers[Headers.AccessControlAllowOrigin] = "*";

        // 处理 OPTIONS 预检请求
        if (string.Equals(request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 204;
            response.Close();
            return;
        }

        var path = request.Url?.AbsolutePath ?? "/";
        // 去除查询参数，仅保留路径部分
        path = path.Split('?')[0];

        // 先执行 HTTP 中间件链：若中间件已处理则返回，否则执行核心资源处理
        await _middlewareChain.ExecuteHttpAsync(context, async () =>
        {
            await ServeAssetCoreAsync(context, path, cancellationToken);
        });
    }

    /// <summary>
    /// 核心资源处理逻辑：读取资源、设置头部、处理 Range 和缓存。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="path">资源路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示处理操作的异步任务。</returns>
    private async Task ServeAssetCoreAsync(HttpListenerContext context, string path, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // 通过基于路径的中间件链处理，最终读取资源
            var content = await _middlewareChain.ExecuteAsync(path, p => Task.FromResult(ReadAssetCore(p)));

            if (content is null || content.Length == 0)
            {
                ServeNotFound(context, path);
                return;
            }

            var mimeType = GetMimeType(path);
            response.ContentType = mimeType;
            response.Headers[Headers.AcceptRanges] = "bytes";

            // 计算 ETag（基于内容的 SHA-256 前 16 字符）
            var etag = ComputeETag(content);
            response.Headers[Headers.ETag] = etag;

            // 设置 Cache-Control（静态资源默认缓存）
            response.Headers[Headers.CacheControl] = "no-cache";

            // 处理 If-None-Match（304 Not Modified）
            var ifNoneMatch = request.Headers[Headers.IfNoneMatch];
            if (!string.IsNullOrEmpty(ifNoneMatch) && string.Equals(ifNoneMatch, etag, StringComparison.Ordinal))
            {
                response.StatusCode = 304;
                response.Close();
                return;
            }

            // 处理 Range 请求
            var rangeHeader = request.Headers[Headers.Range];
            if (!string.IsNullOrEmpty(rangeHeader))
            {
                var range = ParseRangeHeader(rangeHeader, content.Length);
                if (range is not null)
                {
                    await WriteRangeContentAsync(response, content, range.Value.start, range.Value.end, cancellationToken);
                    return;
                }

                // Range 请求无效
                response.StatusCode = 416;
                response.Headers[Headers.ContentRange] = $"bytes */{content.Length}";
                response.Close();
                return;
            }

            // 完整内容响应
            response.StatusCode = 200;
            response.ContentLength64 = content.Length;
            await response.OutputStream.WriteAsync(content, cancellationToken);
            response.Close();
        }
        catch (HttpListenerException)
        {
            // 客户端已断开连接等，忽略
        }
        catch (Exception ex)
        {
            ServeError(context, ex);
        }
    }

    /// <summary>
    /// 返回 404 Not Found 响应。
    /// 若配置了自定义错误处理器则调用它，否则返回默认 404 响应。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="path">未找到的资源路径。</param>
    protected virtual void ServeNotFound(HttpListenerContext context, string path)
    {
        var response = context.Response;
        var ex = new FileNotFoundException($"资源未找到: {path}", path);

        if (_options.ErrorHandler is not null)
        {
            _options.ErrorHandler(context, ex);
            return;
        }

        response.StatusCode = 404;
        response.ContentType = "text/plain; charset=utf-8";
        var body = Encoding.UTF8.GetBytes($"404 Not Found: {path}");
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
        response.Close();
    }

    /// <summary>
    /// 返回 500 Internal Server Error 响应。
    /// 若配置了自定义错误处理器则调用它，否则返回默认 500 响应。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    /// <param name="ex">异常实例。</param>
    protected virtual void ServeError(HttpListenerContext context, Exception ex)
    {
        var response = context.Response;

        if (_options.ErrorHandler is not null)
        {
            _options.ErrorHandler(context, ex);
            return;
        }

        response.StatusCode = 500;
        response.ContentType = "text/plain; charset=utf-8";
        var body = Encoding.UTF8.GetBytes($"500 Internal Server Error: {ex.Message}");
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
        response.Close();
    }

    /// <summary>
    /// 解析 Range 请求头，支持 <c>bytes=start-end</c> 格式。
    /// </summary>
    /// <param name="rangeHeader">Range 头部值。</param>
    /// <param name="totalLength">资源总长度。</param>
    /// <returns>包含起始和结束位置的元组；若无效则返回 null。</returns>
    private static (long start, long end)? ParseRangeHeader(string rangeHeader, long totalLength)
    {
        if (string.IsNullOrEmpty(rangeHeader) || !rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rangeSpec = rangeHeader[6..].Trim();
        var commaIndex = rangeSpec.IndexOf(',');
        if (commaIndex >= 0)
        {
            rangeSpec = rangeSpec[..commaIndex].Trim();
        }

        var dashIndex = rangeSpec.IndexOf('-');
        if (dashIndex < 0)
        {
            return null;
        }

        var startStr = rangeSpec[..dashIndex].Trim();
        var endStr = rangeSpec[(dashIndex + 1)..].Trim();

        long start;
        long end;

        if (string.IsNullOrEmpty(startStr))
        {
            // 后缀范围：bytes=-N 表示最后 N 字节
            if (!long.TryParse(endStr, out var suffixLength) || suffixLength <= 0)
            {
                return null;
            }

            suffixLength = Math.Min(suffixLength, totalLength);
            start = totalLength - suffixLength;
            end = totalLength - 1;
        }
        else
        {
            if (!long.TryParse(startStr, out start) || start < 0 || start >= totalLength)
            {
                return null;
            }

            if (string.IsNullOrEmpty(endStr))
            {
                end = totalLength - 1;
            }
            else
            {
                if (!long.TryParse(endStr, out end) || end < start)
                {
                    return null;
                }

                end = Math.Min(end, totalLength - 1);
            }
        }

        return (start, end);
    }

    /// <summary>
    /// 写入 Range 响应（206 Partial Content）。
    /// </summary>
    /// <param name="response">HTTP 响应对象。</param>
    /// <param name="content">完整资源内容。</param>
    /// <param name="start">起始位置。</param>
    /// <param name="end">结束位置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示写入操作的异步任务。</returns>
    private static async Task WriteRangeContentAsync(
        HttpListenerResponse response,
        byte[] content,
        long start,
        long end,
        CancellationToken cancellationToken)
    {
        var length = end - start + 1;
        response.StatusCode = 206;
        response.Headers[Headers.ContentRange] = $"bytes {start}-{end}/{content.Length}";
        response.ContentLength64 = length;

        var buffer = new byte[Math.Min(length, 81920)];
        var offset = start;
        var remaining = length;

        while (remaining > 0)
        {
            var toRead = (int)Math.Min(remaining, buffer.Length);
            Buffer.BlockCopy(content, (int)offset, buffer, 0, toRead);
            await response.OutputStream.WriteAsync(buffer.AsMemory(0, toRead), cancellationToken);
            offset += toRead;
            remaining -= toRead;
        }

        response.Close();
    }

    /// <summary>
    /// 计算资源的 ETag（基于内容的 SHA-256 哈希前 16 字符）。
    /// </summary>
    /// <param name="content">资源内容。</param>
    /// <returns>ETag 字符串，用双引号包裹。</returns>
    private static string ComputeETag(byte[] content)
    {
        var hash = SHA256.HashData(content);
        var sb = new StringBuilder(40);
        sb.Append('"');
        for (var i = 0; i < 8; i++)
        {
            sb.Append(hash[i].ToString("x2"));
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// 核心资源读取方法，由派生类重写以提供具体的资源来源。
    /// 默认实现返回 null，表示资源不存在。
    /// </summary>
    /// <param name="path">请求的资源路径。</param>
    /// <returns>资源内容字节组；若资源不存在则返回 null。</returns>
    protected virtual byte[]? ReadAssetCore(string path)
    {
        return null;
    }

    /// <summary>
    /// 根据文件扩展名返回 MIME 类型。
    /// 对应 Wails v3 Go 版本 GetMimetype 函数。
    /// </summary>
    /// <param name="path">文件路径或文件名。</param>
    /// <returns>对应的 MIME 类型字符串；若无法识别则返回 <c>application/octet-stream</c>。</returns>
    public string GetMimeType(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "application/octet-stream";
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" or ".mjs" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".bmp" => "image/bmp",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".wasm" => "application/wasm",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".map" => "application/json",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            _ => "application/octet-stream"
        };
    }
}
