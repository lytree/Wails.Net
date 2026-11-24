using System.Text;

namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 带缓冲的流读取器：用于在劫持后的连接上按 <c>CRLFCRLF</c> 切分 HTTP 报文头，
/// 且不会吞掉报文头之后的正文首字节。
/// </summary>
internal sealed class HttpBufferedReader
{
    private const int MaxHeadSize = 256 * 1024;
    private readonly Stream _stream;
    private byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;

    /// <summary>
    /// 基于指定流创建读取器。
    /// </summary>
    /// <param name="stream">底层网络流（明文或 TLS）。</param>
    public HttpBufferedReader(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// 读取一个完整 HTTP 报文头（含结尾空行）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>报文头字节；对端正常关闭返回 <c>null</c>。</returns>
    public async ValueTask<byte[]?> ReadHeadAsync(CancellationToken ct)
    {
        var scanFrom = 0;
        while (true)
        {
            for (var i = Math.Max(_start, scanFrom); i + 3 < _end; i++)
            {
                if (_buffer[i] == (byte)'\r' && _buffer[i + 1] == (byte)'\n'
                    && _buffer[i + 2] == (byte)'\r' && _buffer[i + 3] == (byte)'\n')
                {
                    var length = i + 4 - _start;
                    var head = new byte[length];
                    Buffer.BlockCopy(_buffer, _start, head, 0, length);
                    _start = i + 4;
                    return head;
                }
            }

            scanFrom = Math.Max(_start, _end - 3);

            if (_end - _start >= MaxHeadSize)
            {
                throw new InvalidDataException("HTTP 报文头超过大小限制");
            }

            if (!await FillAsync(ct).ConfigureAwait(false))
            {
                if (_end > _start)
                {
                    throw new EndOfStreamException("HTTP 报文头不完整");
                }

                return null;
            }
        }
    }

    /// <summary>
    /// 读取正文数据（优先消费缓冲区剩余字节）。
    /// </summary>
    /// <param name="destination">目标缓冲区。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>读取字节数，0 表示流结束。</returns>
    public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct)
    {
        if (_start < _end)
        {
            var count = Math.Min(destination.Length, _end - _start);
            _buffer.AsMemory(_start, count).CopyTo(destination);
            _start += count;
            return count;
        }

        return await _stream.ReadAsync(destination, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 读取一个字节。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>字节值；流结束返回 -1。</returns>
    public async ValueTask<int> ReadByteAsync(CancellationToken ct)
    {
        if (_start < _end)
        {
            return _buffer[_start++];
        }

        if (!await FillAsync(ct).ConfigureAwait(false))
        {
            return -1;
        }

        return _buffer[_start++];
    }

    /// <summary>
    /// 读取一行（以 LF 结尾，剥离 CR）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>行文本；流结束返回 <c>null</c>。</returns>
    public async ValueTask<string?> ReadLineAsync(CancellationToken ct)
    {
        var builder = new StringBuilder(64);
        while (true)
        {
            var value = await ReadByteAsync(ct).ConfigureAwait(false);
            if (value < 0)
            {
                return builder.Length == 0 ? null : builder.ToString();
            }

            if (value == (byte)'\n')
            {
                if (builder.Length > 0 && builder[^1] == '\r')
                {
                    builder.Length -= 1;
                }

                return builder.ToString();
            }

            if (builder.Length > 8192)
            {
                throw new InvalidDataException("HTTP 行长度超限");
            }

            builder.Append((char)value);
        }
    }

    /// <summary>
    /// 把读取器中尚未消费的缓冲字节与新底层流拼接成一个可读流。
    /// <para>CONNECT 解析后若读取器提前读到了 TLS 握手字节，必须用本方法回放，否则握手会失败。</para>
    /// </summary>
    /// <param name="inner">后续要使用的底层流（同一连接）。</param>
    /// <returns>带前缀缓冲的流。</returns>
    public Stream Detach(Stream inner)
    {
        var count = _end - _start;
        if (count <= 0)
        {
            return inner;
        }

        var prefix = new byte[count];
        Buffer.BlockCopy(_buffer, _start, prefix, 0, count);
        _start = 0;
        _end = 0;
        return new PrefixedReadStream(inner, prefix);
    }

    private async ValueTask<bool> FillAsync(CancellationToken ct)
    {
        if (_start > 0)
        {
            var remaining = _end - _start;
            if (remaining > 0)
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, remaining);
            }

            _start = 0;
            _end = remaining;
        }

        if (_end == _buffer.Length)
        {
            if (_buffer.Length >= MaxHeadSize)
            {
                return false;
            }

            var grown = new byte[Math.Min(_buffer.Length * 2, MaxHeadSize)];
            Buffer.BlockCopy(_buffer, 0, grown, 0, _end);
            _buffer = grown;
        }

        var read = await _stream.ReadAsync(_buffer.AsMemory(_end), ct).ConfigureAwait(false);
        if (read <= 0)
        {
            return false;
        }

        _end += read;
        return true;
    }
}

/// <summary>
/// HTTP 头中的一行。
/// </summary>
/// <param name="Name">字段名。</param>
/// <param name="Value">字段值。</param>
internal readonly record struct HeaderLine(string Name, string Value);

/// <summary>
/// 解析后的 HTTP 请求头。
/// </summary>
internal sealed class HttpRequestHead
{
    /// <summary>请求方法。</summary>
    public string Method { get; init; } = "GET";

    /// <summary>请求目标（可能是 origin-form 或 absolute-form）。</summary>
    public string Target { get; init; } = "/";

    /// <summary>HTTP 版本。</summary>
    public string Version { get; init; } = "HTTP/1.1";

    /// <summary>请求头字段集合（保持原始顺序）。</summary>
    public List<HeaderLine> Headers { get; init; } = [];

    /// <summary>
    /// 按名称取第一个头字段值。
    /// </summary>
    /// <param name="name">字段名（忽略大小写）。</param>
    /// <returns>字段值，不存在返回 <c>null</c>。</returns>
    public string? GetHeader(string name)
    {
        foreach (var header in Headers)
        {
            if (string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return header.Value;
            }
        }

        return null;
    }

    /// <summary>Content-Length（未知为 -1）。</summary>
    public long ContentLength =>
        long.TryParse(GetHeader("Content-Length"), out var value) ? value : -1;

    /// <summary>是否使用分块传输。</summary>
    public bool IsChunked =>
        GetHeader("Transfer-Encoding")?.Contains("chunked", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// 解析后的 HTTP 响应头。
/// </summary>
internal sealed class HttpResponseHead
{
    /// <summary>HTTP 版本。</summary>
    public string Version { get; init; } = "HTTP/1.1";

    /// <summary>状态码。</summary>
    public int StatusCode { get; init; }

    /// <summary>原因短语。</summary>
    public string ReasonPhrase { get; init; } = string.Empty;

    /// <summary>响应头字段集合（保持原始顺序）。</summary>
    public List<HeaderLine> Headers { get; init; } = [];

    /// <summary>
    /// 按名称取第一个头字段值。
    /// </summary>
    /// <param name="name">字段名（忽略大小写）。</param>
    /// <returns>字段值，不存在返回 <c>null</c>。</returns>
    public string? GetHeader(string name)
    {
        foreach (var header in Headers)
        {
            if (string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return header.Value;
            }
        }

        return null;
    }

    /// <summary>Content-Type（不含 charset 参数）。</summary>
    public string ContentType
    {
        get
        {
            var value = GetHeader("Content-Type");
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var index = value.IndexOf(';', StringComparison.Ordinal);
            return (index >= 0 ? value[..index] : value).Trim().ToLowerInvariant();
        }
    }

    /// <summary>Content-Length（未知为 -1）。</summary>
    public long ContentLength =>
        long.TryParse(GetHeader("Content-Length"), out var value) ? value : -1;

    /// <summary>是否使用分块传输。</summary>
    public bool IsChunked =>
        GetHeader("Transfer-Encoding")?.Contains("chunked", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// HTTP/1.x 报文头的解析与写出工具。
/// </summary>
internal static class Http1
{
    private static readonly string[] HopByHopHeaders =
    [
        "Connection", "Proxy-Connection", "Keep-Alive", "Proxy-Authenticate",
        "Proxy-Authorization", "TE", "Trailer", "Upgrade",
    ];

    /// <summary>
    /// 解析请求头字节。
    /// </summary>
    /// <param name="headBytes">完整报文头（含结尾空行）。</param>
    /// <returns>解析结果。</returns>
    public static HttpRequestHead ParseRequest(byte[] headBytes)
    {
        var text = Encoding.Latin1.GetString(headBytes);
        var lines = text.Split("\r\n", StringSplitOptions.None);
        var requestLine = lines[0].Split(' ', 3);

        var headers = new List<HeaderLine>(lines.Length);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                break;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            headers.Add(new HeaderLine(line[..separator].Trim(), line[(separator + 1)..].Trim()));
        }

        return new HttpRequestHead
        {
            Method = requestLine.Length > 0 ? requestLine[0].ToUpperInvariant() : "GET",
            Target = requestLine.Length > 1 ? requestLine[1] : "/",
            Version = requestLine.Length > 2 ? requestLine[2] : "HTTP/1.1",
            Headers = headers,
        };
    }

    /// <summary>
    /// 解析响应头字节。
    /// </summary>
    /// <param name="headBytes">完整报文头（含结尾空行）。</param>
    /// <returns>解析结果。</returns>
    public static HttpResponseHead ParseResponse(byte[] headBytes)
    {
        var text = Encoding.Latin1.GetString(headBytes);
        var lines = text.Split("\r\n", StringSplitOptions.None);
        var statusLine = lines[0].Split(' ', 3);

        var headers = new List<HeaderLine>(lines.Length);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                break;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            headers.Add(new HeaderLine(line[..separator].Trim(), line[(separator + 1)..].Trim()));
        }

        return new HttpResponseHead
        {
            Version = statusLine.Length > 0 ? statusLine[0] : "HTTP/1.1",
            StatusCode = statusLine.Length > 1 && int.TryParse(statusLine[1], out var code) ? code : 0,
            ReasonPhrase = statusLine.Length > 2 ? statusLine[2] : string.Empty,
            Headers = headers,
        };
    }

    /// <summary>
    /// 判断是否为逐跳（hop-by-hop）头字段。
    /// </summary>
    /// <param name="name">字段名。</param>
    /// <returns>是则返回 <c>true</c>。</returns>
    public static bool IsHopByHop(string name) =>
        HopByHopHeaders.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 构造发往源站的请求头文本。
    /// </summary>
    /// <param name="method">请求方法。</param>
    /// <param name="path">origin-form 路径。</param>
    /// <param name="host">Host 头值。</param>
    /// <param name="source">原始请求。</param>
    /// <param name="stripAcceptEncoding">是否移除 <c>Accept-Encoding</c>（需要改写响应正文时使用）。</param>
    /// <param name="keepAlive">是否复用上游连接（默认关闭，便于确定性分帧）。</param>
    /// <returns>可写入网络的 ASCII 字节。</returns>
    public static byte[] BuildUpstreamRequest(
        string method,
        string path,
        string host,
        HttpRequestHead source,
        bool stripAcceptEncoding = false,
        bool keepAlive = false)
    {
        var builder = new StringBuilder(512);
        builder.Append(method).Append(' ').Append(path).Append(" HTTP/1.1\r\n");
        builder.Append("Host: ").Append(host).Append("\r\n");

        foreach (var header in source.Headers)
        {
            if (string.Equals(header.Name, "Host", StringComparison.OrdinalIgnoreCase)
                || IsHopByHop(header.Name)
                || string.Equals(header.Name, "Expect", StringComparison.OrdinalIgnoreCase)
                || string.Equals(header.Name, "Content-Length", StringComparison.OrdinalIgnoreCase)
                || string.Equals(header.Name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (stripAcceptEncoding && string.Equals(header.Name, "Accept-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        }

        if (source.IsChunked)
        {
            builder.Append("Transfer-Encoding: chunked\r\n");
        }
        else if (source.ContentLength > 0)
        {
            builder.Append("Content-Length: ").Append(source.ContentLength).Append("\r\n");
        }

        // 抓包工具不需要复用上游连接，换取实现上的确定性与低耦合
        builder.Append(keepAlive ? "Connection: keep-alive\r\n\r\n" : "Connection: close\r\n\r\n");
        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    /// <summary>
    /// 构造经由上游代理转发的请求（请求行使用 absolute-form）。
    /// </summary>
    /// <param name="method">请求方法。</param>
    /// <param name="absoluteUri">完整 URL。</param>
    /// <param name="source">原始请求。</param>
    /// <returns>可写入网络的 ASCII 字节。</returns>
    public static byte[] BuildAbsoluteFormRequest(string method, string absoluteUri, HttpRequestHead source)
    {
        var builder = new StringBuilder(512);
        builder.Append(method).Append(' ').Append(absoluteUri).Append(" HTTP/1.1\r\n");

        if (source.GetHeader("Host") is null
            && Uri.TryCreate(absoluteUri, UriKind.Absolute, out var uri))
        {
            builder.Append("Host: ").Append(uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}").Append("\r\n");
        }

        foreach (var header in source.Headers)
        {
            if (IsHopByHop(header.Name)
                || string.Equals(header.Name, "Expect", StringComparison.OrdinalIgnoreCase)
                || string.Equals(header.Name, "Content-Length", StringComparison.OrdinalIgnoreCase)
                || string.Equals(header.Name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        }

        if (source.IsChunked)
        {
            builder.Append("Transfer-Encoding: chunked\r\n");
        }
        else if (source.ContentLength > 0)
        {
            builder.Append("Content-Length: ").Append(source.ContentLength).Append("\r\n");
        }

        builder.Append("Connection: close\r\n\r\n");
        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    /// <summary>
    /// 构造发往上游代理的 CONNECT 请求。
    /// </summary>
    /// <param name="hostPort">目标 host:port。</param>
    /// <returns>可写入网络的 ASCII 字节。</returns>
    public static byte[] BuildConnectRequest(string hostPort) =>
        Encoding.ASCII.GetBytes(
            $"CONNECT {hostPort} HTTP/1.1\r\nHost: {hostPort}\r\nProxy-Connection: keep-alive\r\n\r\n");

    /// <summary>
    /// 构造返回给客户端的响应头文本。
    /// </summary>
    /// <param name="head">源站响应头。</param>
    /// <param name="keepAlive">是否保持客户端连接。</param>
    /// <param name="contentLength">改写后的 Content-Length；null 表示保留原值。</param>
    /// <param name="stripContentEncoding">是否移除 <c>Content-Encoding</c>（正文已被解压/改写时使用）。</param>
    /// <returns>可写入网络的 ASCII 字节。</returns>
    public static byte[] BuildDownstreamResponse(
        HttpResponseHead head, bool keepAlive, long? contentLength = null, bool stripContentEncoding = false)
    {
        var builder = new StringBuilder(512);
        builder.Append("HTTP/1.1 ").Append(head.StatusCode).Append(' ').Append(head.ReasonPhrase).Append("\r\n");

        foreach (var header in head.Headers)
        {
            if (IsHopByHop(header.Name)
                || string.Equals(header.Name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (stripContentEncoding
                && string.Equals(header.Name, "Content-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        }

        var length = contentLength ?? head.ContentLength;
        if (length >= 0)
        {
            builder.Append("Content-Length: ").Append(length).Append("\r\n");
        }

        builder.Append(keepAlive ? "Connection: keep-alive\r\n\r\n" : "Connection: close\r\n\r\n");
        return Encoding.Latin1.GetBytes(builder.ToString());
    }
}
