using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Wails.Net.Cli.Mcp;

/// <summary>
/// stdio 传输：按行读取 JSON-RPC 消息并从标准输出写回响应。
/// 对应 Wails v3 Go 版本 <c>mcp.StdioTransport</c> 的使用方式。
/// </summary>
internal static class McpStdioTransport
{
    /// <summary>
    /// 运行 stdio 服务器直到标准输入关闭或收到取消请求。
    /// </summary>
    /// <param name="server">MCP 服务器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码。</returns>
    public static async Task<int> RunAsync(McpServer server, CancellationToken cancellationToken)
    {
        Console.Error.WriteLine(
            $"Wails.Net MCP server ready over stdio; root={server.Root} token={server.Token}");

        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        using var reader = new StreamReader(input, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
        using var writer = new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true };
        using var writeGate = new SemaphoreSlim(1, 1);

        var pending = new List<Task>();
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var message = line;
            pending.Add(Task.Run(
                async () =>
                {
                    var response = await server.HandleLineAsync(message, cancellationToken).ConfigureAwait(false);
                    if (response is null)
                    {
                        return;
                    }

                    var text = McpProtocol.Serialize(response);
                    await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await writer.WriteLineAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
                        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        writeGate.Release();
                    }
                },
                cancellationToken));

            pending.RemoveAll(task => task.IsCompleted);
        }

        await Task.WhenAll(pending).ConfigureAwait(false);
        return 0;
    }
}

/// <summary>
/// Streamable HTTP 传输：仅监听 127.0.0.1，强制 loopback Host/Origin 与 Bearer 令牌。
/// 对应 Wails v3 Go 版本 <c>internal/commands/mcp.go</c> 的 <c>runHTTP</c>
/// 与 beta.19 PR #6092 的 origin/token 认证硬化。
/// </summary>
internal static class McpHttpTransport
{
    /// <summary>MCP HTTP 端点路径。</summary>
    public const string EndpointPath = "/mcp";

    /// <summary>HTTP 请求体上限（4 MiB）。</summary>
    public const long MaxRequestBodyBytes = 4 * 1024 * 1024;

    /// <summary>
    /// 运行 loopback Streamable HTTP 服务器直到取消。
    /// </summary>
    /// <param name="server">MCP 服务器。</param>
    /// <param name="port">端口；0 表示自动选择空闲端口。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出码。</returns>
    public static async Task<int> RunAsync(McpServer server, int port, CancellationToken cancellationToken)
    {
        if (port is < 0 or > 65535)
        {
            throw new InvalidOperationException($"非法 HTTP 端口：{port}");
        }

        var actualPort = port == 0 ? FindFreeLoopbackPort() : port;
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{actualPort}/");
        listener.Start();

        Console.Error.WriteLine("Wails.Net MCP server ready over Streamable HTTP");
        Console.Error.WriteLine($"endpoint=http://127.0.0.1:{actualPort}{EndpointPath}");
        Console.Error.WriteLine($"root={server.Root}");
        Console.Error.WriteLine($"bearer-token={server.Token}");

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                listener.Stop();
            }
            catch (ObjectDisposedException)
            {
                // 已停止
            }
        });

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                break;
            }

            _ = HandleRequestAsync(server, context, cancellationToken);
        }

        return 0;
    }

    /// <summary>
    /// 判断 Host 头是否为 loopback（127.0.0.1 / ::1 / localhost）。
    /// </summary>
    /// <param name="hostPort">Host 或 authority 字符串。</param>
    /// <returns>为 loopback 时返回 <c>true</c>。</returns>
    public static bool IsLoopbackHost(string? hostPort)
    {
        if (string.IsNullOrWhiteSpace(hostPort))
        {
            return false;
        }

        var host = hostPort;
        if (Uri.TryCreate($"http://{hostPort}", UriKind.Absolute, out var uri))
        {
            host = uri.Host;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// 判断 Origin 头是否可接受：缺失、或 http scheme 且 host 为 loopback。
    /// </summary>
    /// <param name="origin">Origin 头。</param>
    /// <returns>可接受时返回 <c>true</c>。</returns>
    public static bool IsLoopbackOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp
            && string.IsNullOrEmpty(uri.UserInfo)
            && IsLoopbackHost(uri.Authority);
    }

    /// <summary>
    /// 选择空闲 loopback 端口。
    /// </summary>
    /// <returns>端口号。</returns>
    public static int FindFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private static async Task HandleRequestAsync(
        McpServer server,
        HttpListenerContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                response.AddHeader("Allow", "POST");
                response.Close();
                return;
            }

            if (!string.Equals(request.Url?.AbsolutePath, EndpointPath, StringComparison.Ordinal))
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Close();
                return;
            }

            if (!IsLoopbackHost(request.UserHostName) || !IsLoopbackOrigin(request.Headers["Origin"]))
            {
                await WriteTextAsync(
                    response,
                    HttpStatusCode.Forbidden,
                    "MCP HTTP requests must use loopback Host and Origin headers").ConfigureAwait(false);
                return;
            }

            if (!TryReadBearer(request, out var token) || !McpToken.Matches(token, server.Token))
            {
                response.AddHeader("WWW-Authenticate", "Bearer");
                await WriteTextAsync(response, HttpStatusCode.Unauthorized, "invalid or missing bearer token")
                    .ConfigureAwait(false);
                return;
            }

            if (request.ContentLength64 > MaxRequestBodyBytes)
            {
                await WriteTextAsync(response, HttpStatusCode.RequestEntityTooLarge, "request body too large")
                    .ConfigureAwait(false);
                return;
            }

            var body = await ReadBodyAsync(request.InputStream, cancellationToken).ConfigureAwait(false);
            if (body is null)
            {
                await WriteTextAsync(response, HttpStatusCode.RequestEntityTooLarge, "request body too large")
                    .ConfigureAwait(false);
                return;
            }

            var message = await server.HandleLineAsync(body, cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                response.StatusCode = (int)HttpStatusCode.Accepted;
                response.Close();
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(McpProtocol.Serialize(message));
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            response.Close();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Response.Abort();
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
        {
            context.Response.Abort();
        }
    }

    private static bool TryReadBearer(HttpListenerRequest request, out string token)
    {
        token = string.Empty;
        var header = request.Headers["Authorization"];
        if (string.IsNullOrEmpty(header))
        {
            return false;
        }

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = header[prefix.Length..].Trim();
        return token.Length > 0;
    }

    private static async Task<string?> ReadBodyAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaxRequestBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static async Task WriteTextAsync(HttpListenerResponse response, HttpStatusCode status, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.StatusCode = (int)status;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }
}
