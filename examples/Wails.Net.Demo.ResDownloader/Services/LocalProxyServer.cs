using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 本地 MITM 代理服务器（HTTP + HTTPS 中间人），负责转发流量、嗅探媒体资源，
/// 并在同一端口上提供下载器自身的本地 API（预览 / 证书下载）。
/// <para>
/// 与 res-downloader 对齐的能力：
/// 1. 域名规则决定是否解密（不匹配的域名走裸隧道透传）；
/// 2. 上游代理级联（HTTP absolute-form / HTTPS CONNECT）；
/// 3. 微信视频号 JS 注入与 <c>objectDesc</c> 上报解析（含 DecodeKey、清晰度、封面）；
/// 4. <c>/__rdapi/preview</c> 与 <c>/__rdapi/cert</c> 本地接口。
/// </para>
/// </summary>
public sealed partial class LocalProxyServer : IAsyncDisposable
{
    private const string LocalApiPrefix = "/__rdapi/";
    private const string WeChatPath = "/res-downloader/wechat";

    private readonly CertificateAuthority _authority;
    private readonly ResourceRepository _repository;
    private readonly SettingsStore _settings;
    private readonly ILogger<LocalProxyServer> _logger;
    private readonly HttpClient _previewClient;
    private readonly object _gate = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private string _host = "127.0.0.1";
    private int _port = 8899;
    private volatile string[] _activeTypes = [ResourceClassify.All];

    /// <summary>
    /// 创建代理服务器实例。
    /// </summary>
    /// <param name="authority">证书颁发机构。</param>
    /// <param name="repository">资源仓库。</param>
    /// <param name="settings">设置存储。</param>
    /// <param name="logger">日志。</param>
    public LocalProxyServer(
        CertificateAuthority authority,
        ResourceRepository repository,
        SettingsStore settings,
        ILogger<LocalProxyServer> logger)
    {
        _authority = authority;
        _repository = repository;
        _settings = settings;
        _logger = logger;

        // 预览必须绕过系统代理，否则会再次进入本机代理形成回环
        _previewClient = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 6,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AutomaticDecompression = DecompressionMethods.None,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>嗅探到新资源时触发（在代理线程上调用）。</summary>
    public event Action<ResourceItem>? ResourceSniffed;

    /// <summary>代理是否正在监听。</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _listener is not null;
            }
        }
    }

    /// <summary>当前监听地址（Host:Port）。</summary>
    public string Address
    {
        get
        {
            lock (_gate)
            {
                return $"{_host}:{_port}";
            }
        }
    }

    /// <summary>当前监听端口。</summary>
    public int Port
    {
        get
        {
            lock (_gate)
            {
                return _port;
            }
        }
    }

    /// <summary>
    /// 设置参与嗅探的资源类型；传入空集合表示全部。
    /// </summary>
    /// <param name="types">资源类型集合。</param>
    public void SetActiveTypes(IEnumerable<string>? types)
    {
        var list = types?.Where(type => !string.IsNullOrWhiteSpace(type)).Select(type => type.Trim()).ToArray();
        _activeTypes = list is { Length: > 0 } ? list : [ResourceClassify.All];
    }

    /// <summary>
    /// 启动代理。
    /// </summary>
    /// <param name="host">监听地址。</param>
    /// <param name="port">监听端口。</param>
    public void Start(string host, int port)
    {
        lock (_gate)
        {
            if (_listener is not null)
            {
                return;
            }

            var address = ResolveListenAddress(host);
            var listener = new TcpListener(address, port);
            listener.Start();

            _listener = listener;
            _host = host;
            _port = port;
            _cts = new CancellationTokenSource();
            _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
            _logger.LogInformation("代理已启动：{Address}", Address);
        }
    }

    /// <summary>
    /// 停止代理并断开所有连接。
    /// </summary>
    /// <returns>停止完成的任务。</returns>
    public async Task StopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;
        TcpListener? listener;

        lock (_gate)
        {
            loop = _acceptLoop;
            cts = _cts;
            listener = _listener;
            _acceptLoop = null;
            _cts = null;
            _listener = null;
        }

        if (listener is null)
        {
            return;
        }

        try
        {
            cts?.Cancel();
            listener.Stop();
            if (loop is not null)
            {
                await loop.ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "停止代理时忽略的异常");
        }
        finally
        {
            cts?.Dispose();
            _logger.LogInformation("代理已停止");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _previewClient.Dispose();
    }

    private static IPAddress ResolveListenAddress(string host)
    {
        var value = (host ?? string.Empty).Trim();
        if (value is "*" or "+" or "0.0.0.0")
        {
            return IPAddress.Any;
        }

        if (value == "::")
        {
            return IPAddress.IPv6Any;
        }

        return IPAddress.TryParse(value, out var address) ? address : IPAddress.Loopback;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var reader = new HttpBufferedReader(stream);

                while (!ct.IsCancellationRequested)
                {
                    var headBytes = await ReadHeadWithTimeoutAsync(reader, ct).ConfigureAwait(false);
                    if (headBytes is null)
                    {
                        break;
                    }

                    HttpRequestHead request;
                    try
                    {
                        request = Http1.ParseRequest(headBytes);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "解析请求头失败");
                        break;
                    }

                    if (request.Target.StartsWith(LocalApiPrefix, StringComparison.Ordinal))
                    {
                        await HandleLocalApiAsync(stream, reader, request, ct).ConfigureAwait(false);
                        return;
                    }

                    if (request.Target.StartsWith(WeChatPath, StringComparison.Ordinal))
                    {
                        await HandleWeChatReportAsync(stream, reader, request, ct).ConfigureAwait(false);
                        return;
                    }

                    if (request.Method == "CONNECT")
                    {
                        await HandleConnectAsync(stream, reader, request, ct).ConfigureAwait(false);
                        return;
                    }

                    if (!await ForwardAsync(stream, reader, request, "http", null, ct).ConfigureAwait(false))
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 代理停止或连接空闲超时，正常退出
            }
            catch (IOException)
            {
                // 客户端提前断开
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "处理代理连接时出错");
            }
        }
    }

    /// <summary>
    /// 处理 CONNECT：按域名规则决定 MITM 解密或裸隧道透传。
    /// </summary>
    private async Task HandleConnectAsync(
        Stream clientStream, HttpBufferedReader reader, HttpRequestHead request, CancellationToken ct)
    {
        var (host, port) = SplitHostPort(request.Target, 443);
        var settings = _settings.Get();
        var rules = DomainRules.Parse(settings.Rule);
        var mitm = rules.ShouldMitm(host);

        var established = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 Connection Established\r\nProxy-Agent: Wails.Net-ResDownloader\r\n\r\n");
        await clientStream.WriteAsync(established, ct).ConfigureAwait(false);
        await clientStream.FlushAsync(ct).ConfigureAwait(false);

        // 读取器可能已经把 TLS 握手首字节读进缓冲区，必须原样回放
        var replay = reader.Detach(clientStream);

        if (!mitm)
        {
            await TunnelAsync(replay, clientStream, host, port, ct).ConfigureAwait(false);
            return;
        }

        X509Certificate2 certificate;
        try
        {
            certificate = _authority.GetServerCertificate(host);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "为 {Host} 签发证书失败", host);
            return;
        }

        using var ssl = new SslStream(replay, leaveInnerStreamOpen: false);
        try
        {
            await ssl.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    // 只协商 HTTP/1.1：HTTP/2 的多路复用会让“一请求一响应”的转发模型失效
                    ApplicationProtocols = [SslApplicationProtocol.Http11],
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 客户端不信任根证书时会在这里失败（握手 RST），属于预期情况
            _logger.LogDebug(ex, "与 {Host} 的 TLS 握手失败", host);
            return;
        }

        var tlsReader = new HttpBufferedReader(ssl);
        while (!ct.IsCancellationRequested)
        {
            var headBytes = await ReadHeadWithTimeoutAsync(tlsReader, ct).ConfigureAwait(false);
            if (headBytes is null)
            {
                break;
            }

            HttpRequestHead inner;
            try
            {
                inner = Http1.ParseRequest(headBytes);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "解析 HTTPS 请求头失败");
                break;
            }

            if (!await ForwardAsync(ssl, tlsReader, inner, "https", request.Target, ct).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    /// <summary>
    /// 裸隧道透传（域名规则未命中时使用，不做解密与嗅探）。
    /// </summary>
    private async Task TunnelAsync(
        Stream clientReadStream, Stream clientWriteStream, string host, int port, CancellationToken ct)
    {
        var settings = _settings.Get();
        TcpClient upstream;
        Stream upstreamStream;

        try
        {
            (upstream, upstreamStream) = await ConnectUpstreamAsync(settings, host, port, tls: true, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "透传连接 {Host}:{Port} 失败", host, port);
            return;
        }

        using (upstream)
        {
            try
            {
                var upload = CopyAsync(clientReadStream, upstreamStream, ct);
                var download = CopyAsync(upstreamStream, clientWriteStream, ct);
                await Task.WhenAny(upload, download).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "透传 {Host}:{Port} 中断", host, port);
            }
            finally
            {
                await upstreamStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task CopyAsync(Stream source, Stream destination, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    return;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                await destination.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// 转发一次请求/响应，并在响应头到达时尝试嗅探资源。
    /// </summary>
    private async Task<bool> ForwardAsync(
        Stream clientStream,
        HttpBufferedReader clientReader,
        HttpRequestHead request,
        string scheme,
        string? authority,
        CancellationToken ct)
    {
        string host;
        int port;
        string path;
        string url;

        if (scheme == "https" && authority is not null)
        {
            (host, port) = SplitHostPort(authority, 443);
            path = NormalizePath(request.Target);
            url = port == 443 ? $"https://{host}{path}" : $"https://{host}:{port}{path}";
        }
        else if (request.Target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 && Uri.TryCreate(request.Target, UriKind.Absolute, out var absolute))
        {
            host = absolute.Host;
            port = absolute.Port;
            path = absolute.PathAndQuery;
            url = request.Target;
        }
        else
        {
            var hostHeader = request.GetHeader("Host") ?? "localhost";
            (host, port) = SplitHostPort(hostHeader, 80);
            path = NormalizePath(request.Target);
            url = $"http://{hostHeader}{path}";
        }

        // 客户端使用 Expect: 100-continue 时先本地应答，避免等源站而卡住上传
        if (request.GetHeader("Expect")?.Contains("100-continue", StringComparison.OrdinalIgnoreCase) == true)
        {
            await clientStream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"), ct)
                .ConfigureAwait(false);
            await clientStream.FlushAsync(ct).ConfigureAwait(false);
        }

        var settings = _settings.Get();
        TcpClient upstream;
        Stream upstreamStream;

        try
        {
            (upstream, upstreamStream) = await ConnectUpstreamAsync(
                settings, host, port, tls: scheme == "https", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "连接源站 {Host}:{Port} 失败", host, port);
            await WriteGatewayTimeoutAsync(clientStream, ct).ConfigureAwait(false);
            return false;
        }

        using (upstream)
        {
            SslStream? upstreamTls = null;
            if (scheme == "https")
            {
                upstreamTls = new SslStream(upstreamStream, leaveInnerStreamOpen: false);
                try
                {
                    await upstreamTls.AuthenticateAsClientAsync(
                        new SslClientAuthenticationOptions
                        {
                            TargetHost = host,
                            ApplicationProtocols = [SslApplicationProtocol.Http11],
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        },
                        ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "与源站 {Host} 的 TLS 握手失败", host);
                    await WriteGatewayTimeoutAsync(clientStream, ct).ConfigureAwait(false);
                    await upstreamTls.DisposeAsync().ConfigureAwait(false);
                    return false;
                }

                upstreamStream = upstreamTls;
            }

            try
            {
                var mayRewrite = MayInjectScript(host, path);
                var hostHeaderValue = port == (scheme == "https" ? 443 : 80) ? host : $"{host}:{port}";

                byte[] requestHead;
                if (settings.OpenProxy && TryParseUpstreamProxy(settings.UpstreamProxy, out _)
                    && scheme == "http")
                {
                    // 走上游代理时使用 absolute-form，且不能带 Proxy-Connection 之外的逐跳头
                    requestHead = Http1.BuildAbsoluteFormRequest(request.Method, url, request);
                }
                else
                {
                    requestHead = Http1.BuildUpstreamRequest(
                        request.Method, path, hostHeaderValue, request, stripAcceptEncoding: mayRewrite);
                }

                await upstreamStream.WriteAsync(requestHead, ct).ConfigureAwait(false);

                if (request.IsChunked)
                {
                    await RelayChunkedAsync(clientReader, upstreamStream, ct).ConfigureAwait(false);
                }
                else if (request.ContentLength > 0)
                {
                    await RelayExactAsync(clientReader, upstreamStream, request.ContentLength, ct).ConfigureAwait(false);
                }

                await upstreamStream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "转发请求正文失败");
                await WriteGatewayTimeoutAsync(clientStream, ct).ConfigureAwait(false);
                return false;
            }

            var upstreamReader = new HttpBufferedReader(upstreamStream);
            HttpResponseHead response;
            try
            {
                response = await ReadResponseHeadAsync(upstreamReader, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "读取源站响应失败");
                await WriteGatewayTimeoutAsync(clientStream, ct).ConfigureAwait(false);
                return false;
            }

            if (!IsWeChatReportSuppressed(host, request))
            {
                TrySniff(url, request, response, settings);
            }

            var noBody = request.Method == "HEAD"
                         || response.StatusCode is 204 or 304
                         || (response.StatusCode >= 100 && response.StatusCode < 200);

            var clientWantsClose = request.GetHeader("Connection")
                ?.Contains("close", StringComparison.OrdinalIgnoreCase) == true;

            try
            {
                // 微信视频号的 JS 需要就地注入，先整体读出正文再改写
                if (!noBody && MayInjectScript(host, path) && !response.IsChunked
                    && response.ContentLength is > 0 and < 16 * 1024 * 1024)
                {
                    var body = await ReadExactBytesAsync(upstreamReader, (int)response.ContentLength, ct)
                        .ConfigureAwait(false);
                    var decoded = TryDecompress(body, response.GetHeader("Content-Encoding"));
                    var rewritten = TryRewriteWeChatScript(host, decoded);
                    if (rewritten is not null)
                    {
                        var head = Http1.BuildDownstreamResponse(
                            response, keepAlive: false, rewritten.Length, stripContentEncoding: true);
                        await clientStream.WriteAsync(head, ct).ConfigureAwait(false);
                        await clientStream.WriteAsync(rewritten, ct).ConfigureAwait(false);
                        await clientStream.FlushAsync(ct).ConfigureAwait(false);
                        return false;
                    }

                    // 未命中注入标记：原样回放（保持压缩状态与原始长度）
                    var originalHead = Http1.BuildDownstreamResponse(
                        response, keepAlive: false, body.Length, stripContentEncoding: false);
                    await clientStream.WriteAsync(originalHead, ct).ConfigureAwait(false);
                    await clientStream.WriteAsync(body, ct).ConfigureAwait(false);
                    await clientStream.FlushAsync(ct).ConfigureAwait(false);
                    return false;
                }

                long? length = response.ContentLength >= 0 ? response.ContentLength : null;
                var keepAlive = !clientWantsClose && (noBody || response.IsChunked || length is not null);
                var responseHead = Http1.BuildDownstreamResponse(response, keepAlive, length);
                await clientStream.WriteAsync(responseHead, ct).ConfigureAwait(false);

                if (!noBody)
                {
                    if (response.IsChunked)
                    {
                        await RelayChunkedAsync(upstreamReader, clientStream, ct).ConfigureAwait(false);
                    }
                    else if (length is > 0)
                    {
                        await RelayExactAsync(upstreamReader, clientStream, length.Value, ct).ConfigureAwait(false);
                    }
                    else if (length is null)
                    {
                        await RelayUntilEofAsync(upstreamReader, clientStream, ct).ConfigureAwait(false);
                    }
                }

                await clientStream.FlushAsync(ct).ConfigureAwait(false);
                return keepAlive;
            }
            finally
            {
                if (upstreamTls is not null)
                {
                    await upstreamTls.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// 建立到源站的连接；启用上游代理时按 HTTP/HTTPS 分别处理。
    /// </summary>
    private async Task<(TcpClient Client, Stream Stream)> ConnectUpstreamAsync(
        AppSettings settings, string host, int port, bool tls, CancellationToken ct)
    {
        UpstreamProxy proxy = default;
        var useProxy = settings.OpenProxy && TryParseUpstreamProxy(settings.UpstreamProxy, out proxy);

        var client = new TcpClient();
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(20));

            if (!useProxy)
            {
                await client.ConnectAsync(host, port, connectCts.Token).ConfigureAwait(false);
                return (client, client.GetStream());
            }

            await client.ConnectAsync(proxy.Host, proxy.Port, connectCts.Token).ConfigureAwait(false);
            var stream = client.GetStream();

            // HTTPS 需要先与上游代理建立 CONNECT 隧道；HTTP 直接用 absolute-form
            if (tls)
            {
                await stream.WriteAsync(Http1.BuildConnectRequest($"{host}:{port}"), ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);

                var reader = new HttpBufferedReader(stream);
                var head = await reader.ReadHeadAsync(ct).ConfigureAwait(false)
                           ?? throw new IOException("上游代理未返回 CONNECT 响应");
                var response = Http1.ParseResponse(head);
                if (response.StatusCode != 200)
                {
                    throw new IOException($"上游代理拒绝 CONNECT：HTTP {response.StatusCode}");
                }

                // 上游代理可能已经在隧道里回传了源站数据，需要连同缓冲区一起交出去
                return (client, reader.Detach(stream));
            }

            return (client, stream);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private readonly record struct UpstreamProxy(string Host, int Port);

    private static bool TryParseUpstreamProxy(string? value, out UpstreamProxy proxy)
    {
        proxy = default;
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            // socks5 等暂不支持，回退为直连，避免把用户网络打断
            return false;
        }

        proxy = new UpstreamProxy(uri.Host, uri.IsDefaultPort ? 80 : uri.Port);
        return true;
    }

    private async Task<HttpResponseHead> ReadResponseHeadAsync(HttpBufferedReader reader, CancellationToken ct)
    {
        // 跳过 1xx 中间响应（100 Continue / 103 Early Hints）
        for (var i = 0; i < 5; i++)
        {
            var bytes = await reader.ReadHeadAsync(ct).ConfigureAwait(false)
                        ?? throw new IOException("源站在返回响应头前关闭了连接");
            var response = Http1.ParseResponse(bytes);
            if (response.StatusCode >= 200 || response.StatusCode < 100)
            {
                return response;
            }
        }

        throw new IOException("源站返回了过多的 1xx 中间响应");
    }

    private void TrySniff(string url, HttpRequestHead request, HttpResponseHead response, AppSettings settings)
    {
        if (response.StatusCode is not (200 or 206 or 304))
        {
            return;
        }

        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;

        // 视频号详情页内的直链没有 DecodeKey，交给 objectDesc 上报的条目处理，避免重复
        if (host.EndsWith("finder.video.qq.com", StringComparison.OrdinalIgnoreCase)
            && request.GetHeader("Origin")?.Contains("mp.weixin.qq.com", StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        var resolved = MimeTypeMap.Resolve(settings.MimeMap, response.ContentType, url);
        if (resolved is null || !IsTypeActive(resolved.Classify))
        {
            return;
        }

        var length = response.ContentLength;
        var title = ResourceNaming.BuildTitle(url, response.GetHeader("Content-Disposition"), resolved.Suffix);
        var headers = CaptureHeaders(request);

        var item = new ResourceItem
        {
            Url = url,
            UrlSign = ResourceRepository.ComputeSign(url),
            Domain = ResourceNaming.TopLevelDomain(url),
            Classify = resolved.Classify,
            Suffix = resolved.Suffix,
            ContentType = response.ContentType,
            Size = length > 0 ? length : 0,
            Title = title,
            Description = string.Empty,
            Status = ResourceStatus.Ready,
            OtherData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["headers"] = headers,
            },
        };

        AddResource(item, settings);
    }

    private static string CaptureHeaders(HttpRequestHead request)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            if (Http1.IsHopByHop(header.Name)
                || string.Equals(header.Name, "Content-Length", StringComparison.OrdinalIgnoreCase)
                || string.Equals(header.Name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 同名头（如多个 Cookie / Accept）合并，重放时保持等价
            map[header.Name] = map.TryGetValue(header.Name, out var existing)
                ? existing + ", " + header.Value
                : header.Value;
        }

        return JsonSerializer.Serialize(map);
    }

    private bool IsTypeActive(string classify)
    {
        var types = _activeTypes;
        return types.Contains(ResourceClassify.All, StringComparer.OrdinalIgnoreCase)
               || types.Contains(classify, StringComparer.OrdinalIgnoreCase);
    }

    private void AddResource(ResourceItem item, AppSettings settings)
    {
        var added = _repository.Add(item, settings.InsertTail);
        if (added is not null)
        {
            ResourceSniffed?.Invoke(added);
            _logger.LogInformation("嗅探到资源：{Classify} {Url}", added.Classify, added.Url);
        }
    }

    /// <summary>
    /// 处理微信视频号注入脚本的 <c>objectDesc</c> 上报。
    /// </summary>
    private async Task HandleWeChatReportAsync(
        Stream stream, HttpBufferedReader reader, HttpRequestHead request, CancellationToken ct)
    {
        var type = 0;
        var queryIndex = request.Target.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex > 0)
        {
            foreach (var pair in request.Target[(queryIndex + 1)..].Split('&'))
            {
                if (pair.StartsWith("type=", StringComparison.Ordinal)
                    && int.TryParse(pair[5..], out var parsed))
                {
                    type = parsed;
                }
            }
        }

        byte[] body;
        if (request.IsChunked)
        {
            body = await ReadChunkedBytesAsync(reader, ct).ConfigureAwait(false);
        }
        else if (request.ContentLength is > 0 and < 4 * 1024 * 1024)
        {
            body = await ReadExactBytesAsync(reader, (int)request.ContentLength, ct).ConfigureAwait(false);
        }
        else
        {
            body = [];
        }

        await WritePlainTextAsync(stream, "ok", ct).ConfigureAwait(false);

        var settings = _settings.Get();
        if (type == 2 && !settings.WxAction)
        {
            // 关闭「全量拦截」时只接受视频详情上报
            return;
        }

        if (body.Length > 0)
        {
            TryHandleWeChatObjectDesc(body, settings);
        }
    }

    private void TryHandleWeChatObjectDesc(byte[] body, AppSettings settings)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (!root.TryGetProperty("media", out var media)
                || media.ValueKind != JsonValueKind.Array
                || media.GetArrayLength() == 0)
            {
                return;
            }

            var first = media[0];
            if (!first.TryGetProperty("url", out var urlElement)
                || urlElement.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var url = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            if (first.TryGetProperty("urlToken", out var token) && token.ValueKind == JsonValueKind.String)
            {
                url += token.GetString();
            }

            var isImage = first.TryGetProperty("mediaType", out var mediaType)
                          && mediaType.ValueKind == JsonValueKind.Number
                          && mediaType.GetDouble() == 9;

            var classify = isImage ? ResourceClassify.Image : ResourceClassify.Video;
            if (!IsTypeActive(classify))
            {
                return;
            }

            var suffix = isImage ? ".png" : ".mp4";
            var contentLength = 0L;
            if (first.TryGetProperty("fileSize", out var size))
            {
                contentLength = size.ValueKind switch
                {
                    JsonValueKind.Number => (long)size.GetDouble(),
                    JsonValueKind.String when long.TryParse(size.GetString(), out var parsed) => parsed,
                    _ => 0,
                };
            }

            var item = new ResourceItem
            {
                Url = url,
                UrlSign = ResourceRepository.ComputeSign(url),
                Domain = ResourceNaming.TopLevelDomain(url),
                Classify = classify,
                Suffix = suffix,
                ContentType = isImage ? "image/png" : "video/mp4",
                Size = contentLength,
                Title = ResourceNaming.BuildTitle(url, null, suffix),
                Description = root.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
                    ? desc.GetString() ?? string.Empty
                    : string.Empty,
                CoverUrl = first.TryGetProperty("coverUrl", out var cover) && cover.ValueKind == JsonValueKind.String
                    ? cover.GetString() ?? string.Empty
                    : string.Empty,
                DecodeKey = first.TryGetProperty("decodeKey", out var key) && key.ValueKind == JsonValueKind.String
                    ? key.GetString() ?? string.Empty
                    : string.Empty,
                Status = ResourceStatus.Ready,
            };

            if (first.TryGetProperty("spec", out var spec)
                && spec.ValueKind == JsonValueKind.Array)
            {
                var formats = new List<string>();
                foreach (var entry in spec.EnumerateArray())
                {
                    if (entry.TryGetProperty("fileFormat", out var format)
                        && format.ValueKind == JsonValueKind.String)
                    {
                        formats.Add(format.GetString() ?? string.Empty);
                    }
                }

                if (formats.Count > 0)
                {
                    item.OtherData["wx_file_formats"] = string.Join("#", formats);
                }
            }

            // 描述作为文件名时可能为空，用封面/域名兜底展示
            if (string.IsNullOrWhiteSpace(item.Description))
            {
                item.Description = string.Empty;
            }

            AddResource(item, settings);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "解析视频号 objectDesc 失败");
        }
    }

    /// <summary>
    /// 本地 API：证书下载与资源预览（解决 WebView 内的跨域 / 防盗链问题）。
    /// </summary>
    private async Task HandleLocalApiAsync(
        Stream stream, HttpBufferedReader reader, HttpRequestHead request, CancellationToken ct)
    {
        var target = request.Target;
        var queryIndex = target.IndexOf('?', StringComparison.Ordinal);
        var path = queryIndex >= 0 ? target[..queryIndex] : target;
        var query = queryIndex >= 0 ? target[(queryIndex + 1)..] : string.Empty;

        if (path.Equals(LocalApiPrefix + "cert", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = _authority.GetRootCertificateBytes();
            var head = "HTTP/1.1 200 OK\r\n" +
                       "Content-Type: application/x-x509-ca-cert\r\n" +
                       "Content-Disposition: attachment; filename=wails-net-res-downloader.cer\r\n" +
                       $"Content-Length: {bytes.Length}\r\n" +
                       "Access-Control-Allow-Origin: *\r\n" +
                       "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            return;
        }

        if (path.Equals(LocalApiPrefix + "ping", StringComparison.OrdinalIgnoreCase))
        {
            await WritePlainTextAsync(stream, "pong", ct).ConfigureAwait(false);
            return;
        }

        if (!path.Equals(LocalApiPrefix + "preview", StringComparison.OrdinalIgnoreCase))
        {
            await WriteStatusAsync(stream, 404, "Not Found", "not found", ct).ConfigureAwait(false);
            return;
        }

        var rawUrl = GetQueryValue(query, "url");
        if (string.IsNullOrWhiteSpace(rawUrl)
            || !Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            await WriteStatusAsync(stream, 400, "Bad Request", "invalid url", ct).ConfigureAwait(false);
            return;
        }

        await ProxyPreviewAsync(stream, request, uri, ct).ConfigureAwait(false);
    }

    private async Task ProxyPreviewAsync(
        Stream stream, HttpRequestHead request, Uri uri, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, uri);
        ApplyPreviewHeaders(message, uri);

        if (request.GetHeader("Range") is { } range)
        {
            message.Headers.TryAddWithoutValidation("Range", range);
        }

        HttpResponseMessage response;
        try
        {
            response = await _previewClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "预览 {Url} 失败", uri);
            await WriteStatusAsync(stream, 502, "Bad Gateway", "preview failed", ct).ConfigureAwait(false);
            return;
        }

        using (response)
        {
            var builder = new StringBuilder(256);
            builder.Append("HTTP/1.1 ").Append((int)response.StatusCode).Append(' ')
                .Append(response.ReasonPhrase ?? "OK").Append("\r\n");

            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                    || header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                builder.Append(header.Key).Append(": ").Append(string.Join(", ", header.Value)).Append("\r\n");
            }

            builder.Append("Access-Control-Allow-Origin: *\r\n");
            builder.Append("Access-Control-Allow-Headers: *\r\n");
            builder.Append("Access-Control-Expose-Headers: Content-Length, Content-Range, Accept-Ranges\r\n");
            builder.Append("Accept-Ranges: bytes\r\n");

            var length = response.Content.Headers.ContentLength;
            if (length is >= 0)
            {
                builder.Append("Content-Length: ").Append(length.Value).Append("\r\n");
            }

            builder.Append("Connection: close\r\n\r\n");
            await stream.WriteAsync(Encoding.Latin1.GetBytes(builder.ToString()), ct).ConfigureAwait(false);

            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var buffer = ArrayPool<byte>.Shared.Rent(80 * 1024);
            try
            {
                while (true)
                {
                    var read = await body.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    await stream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
    }

    private void ApplyPreviewHeaders(HttpRequestMessage message, Uri uri)
    {
        var resource = _repository.Snapshot().FirstOrDefault(item => item.Url == uri.AbsoluteUri);
        var settings = _settings.Get();

        if (resource is not null
            && resource.OtherData.TryGetValue("headers", out var json)
            && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (headers is not null)
                {
                    foreach (var (name, value) in headers)
                    {
                        if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        message.Headers.TryAddWithoutValidation(name, value);
                    }
                }
            }
            catch (JsonException)
            {
                // 头部快照损坏时退回默认请求头
            }
        }

        if (!message.Headers.Contains("Referer"))
        {
            message.Headers.TryAddWithoutValidation("Referer", uri.GetLeftPart(UriPartial.Authority) + "/");
        }

        message.Headers.TryAddWithoutValidation(
            "User-Agent", string.IsNullOrWhiteSpace(settings.UserAgent) ? "Mozilla/5.0" : settings.UserAgent);
    }

    private static string GetQueryValue(string query, string key)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (!pair[..separator].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = pair[(separator + 1)..];
            return Uri.UnescapeDataString(value.Replace("+", "%20", StringComparison.Ordinal));
        }

        return string.Empty;
    }

    private static async Task WriteStatusAsync(
        Stream stream, int code, string reason, string body, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = $"HTTP/1.1 {code} {reason}\r\n" +
                   "Content-Type: text/plain; charset=utf-8\r\n" +
                   $"Content-Length: {bytes.Length}\r\n" +
                   "Access-Control-Allow-Origin: *\r\n" +
                   "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task WritePlainTextAsync(Stream stream, string body, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = "HTTP/1.1 200 OK\r\n" +
                   "Content-Type: text/plain; charset=utf-8\r\n" +
                   $"Content-Length: {bytes.Length}\r\n" +
                   "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static bool MayInjectScript(string host, string path) =>
        host.EndsWith("res.wx.qq.com", StringComparison.OrdinalIgnoreCase)
        && path.Contains(".js", StringComparison.OrdinalIgnoreCase);

    private static bool IsWeChatReportSuppressed(string host, HttpRequestHead request) =>
        host.EndsWith("finder.video.qq.com", StringComparison.OrdinalIgnoreCase)
        && request.GetHeader("Origin")?.Contains("mp.weixin.qq.com", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// 向微信视频号的 JS 中注入上报逻辑（对应 res-downloader 的 QQ 插件）。
    /// </summary>
    private byte[]? TryRewriteWeChatScript(string host, byte[] body)
    {
        if (!host.EndsWith("res.wx.qq.com", StringComparison.OrdinalIgnoreCase) || body.Length == 0)
        {
            return null;
        }

        string text;
        try
        {
            text = Encoding.UTF8.GetString(body);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (!MediaGetterRegex().IsMatch(text) && !CommentDetailRegex().IsMatch(text))
        {
            return null;
        }

        // 注意：只替换 getter 的头部，不能补右花括号，原函数体仍然要留在 getter 内部
        var mediaSnippet = """
                           get media(){
                               try{
                                   if(this.objectDesc){
                                       fetch("https://wxapp.tc.qq.com/res-downloader/wechat?type=1", {
                                           method: "POST",
                                           mode: "no-cors",
                                           body: JSON.stringify(this.objectDesc)
                                       });
                                   }
                               }catch(e){}
                           """;

        var commentSnippet = """
                             async finderGetCommentDetail($1) {
                                 var res = await$2;
                                 try{
                                     if(res && res.data && res.data.object && res.data.object.objectDesc){
                                         fetch("https://wxapp.tc.qq.com/res-downloader/wechat?type=2", {
                                             method: "POST",
                                             mode: "no-cors",
                                             body: JSON.stringify(res.data.object.objectDesc)
                                         });
                                     }
                                 }catch(e){}
                                 return res;
                             }async
                             """;

        var rewritten = MediaGetterRegex().Replace(text, mediaSnippet);
        rewritten = CommentDetailRegex().Replace(rewritten, commentSnippet);
        return Encoding.UTF8.GetBytes(rewritten);
    }

    /// <summary>
    /// 按 <c>Content-Encoding</c> 解压响应正文（注入脚本前需要明文）。
    /// </summary>
    private static byte[] TryDecompress(byte[] body, string? contentEncoding)
    {
        if (body.Length == 0 || string.IsNullOrWhiteSpace(contentEncoding))
        {
            return body;
        }

        try
        {
            using var input = new MemoryStream(body);
            using var output = new MemoryStream(body.Length * 2);
            Stream decoder = contentEncoding.ToLowerInvariant() switch
            {
                "gzip" => new GZipStream(input, CompressionMode.Decompress),
                "deflate" => new ZLibStream(input, CompressionMode.Decompress),
                "br" => new BrotliStream(input, CompressionMode.Decompress),
                _ => input,
            };

            if (ReferenceEquals(decoder, input))
            {
                return body;
            }

            using (decoder)
            {
                decoder.CopyTo(output);
            }

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return body;
        }
    }

    private static async Task<byte[]> ReadExactBytesAsync(HttpBufferedReader reader, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(offset, count - offset), ct).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            offset += read;
        }

        return offset == count ? buffer : buffer[..offset];
    }

    private static async Task<byte[]> ReadChunkedBytesAsync(HttpBufferedReader reader, CancellationToken ct)
    {
        using var memory = new MemoryStream();
        while (true)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            var sizeText = line;
            var semicolon = sizeText.IndexOf(';', StringComparison.Ordinal);
            if (semicolon >= 0)
            {
                sizeText = sizeText[..semicolon];
            }

            if (!int.TryParse(sizeText.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size)
                || size == 0)
            {
                break;
            }

            var chunk = await ReadExactBytesAsync(reader, size, ct).ConfigureAwait(false);
            memory.Write(chunk);
            _ = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        }

        return memory.ToArray();
    }

    private static (string Host, int Port) SplitHostPort(string value, int defaultPort)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return ("localhost", defaultPort);
        }

        if (text[0] == '[')
        {
            var end = text.IndexOf(']', StringComparison.Ordinal);
            if (end > 0)
            {
                var address = text[1..end];
                var rest = text[(end + 1)..];
                if (rest.StartsWith(':')
                    && int.TryParse(rest[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ipv6Port))
                {
                    return (address, ipv6Port);
                }

                return (address, defaultPort);
            }
        }

        var colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':') == colon
            && int.TryParse(text[(colon + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
        {
            return (text[..colon], port);
        }

        return (text, defaultPort);
    }

    private static string NormalizePath(string target)
    {
        if (string.IsNullOrEmpty(target))
        {
            return "/";
        }

        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(target, UriKind.Absolute, out var uri) ? uri.PathAndQuery : "/";
        }

        return target[0] == '/' ? target : "/" + target;
    }

    private async Task<byte[]?> ReadHeadWithTimeoutAsync(HttpBufferedReader reader, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            return await reader.ReadHeadAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static async Task RelayExactAsync(
        HttpBufferedReader source, Stream destination, long count, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(80 * 1024);
        try
        {
            var remaining = count;
            while (remaining > 0)
            {
                var want = (int)Math.Min(buffer.Length, remaining);
                var read = await source.ReadAsync(buffer.AsMemory(0, want), ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task RelayUntilEofAsync(
        HttpBufferedReader source, Stream destination, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(80 * 1024);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    return;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task RelayChunkedAsync(
        HttpBufferedReader source, Stream destination, CancellationToken ct)
    {
        while (true)
        {
            var line = await source.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            await WriteAsciiAsync(destination, line + "\r\n", ct).ConfigureAwait(false);

            var sizeText = line;
            var semicolon = sizeText.IndexOf(';', StringComparison.Ordinal);
            if (semicolon >= 0)
            {
                sizeText = sizeText[..semicolon];
            }

            if (!int.TryParse(sizeText.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size))
            {
                return;
            }

            if (size == 0)
            {
                // 尾部字段，直到空行结束
                while (true)
                {
                    var trailer = await source.ReadLineAsync(ct).ConfigureAwait(false);
                    if (trailer is null)
                    {
                        return;
                    }

                    await WriteAsciiAsync(destination, trailer + "\r\n", ct).ConfigureAwait(false);
                    if (trailer.Length == 0)
                    {
                        return;
                    }
                }
            }

            await RelayExactAsync(source, destination, size, ct).ConfigureAwait(false);

            // 消费分片数据后的 CRLF
            _ = await source.ReadLineAsync(ct).ConfigureAwait(false);
            await WriteAsciiAsync(destination, "\r\n", ct).ConfigureAwait(false);
        }
    }

    private static Task WriteAsciiAsync(Stream stream, string text, CancellationToken ct) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(text), ct).AsTask();

    private static Task WriteGatewayTimeoutAsync(Stream stream, CancellationToken ct)
    {
        const string body = "{\"error\":\"upstream unavailable\"}";
        var head =
            "HTTP/1.1 502 Bad Gateway\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n";
        return stream.WriteAsync(Encoding.UTF8.GetBytes(head + body), ct).AsTask();
    }

    [GeneratedRegex(@"get\s*media\(\)\s*\{")]
    private static partial Regex MediaGetterRegex();

    [GeneratedRegex(@"async\s+finderGetCommentDetail\((\w+)\)\s*\{return([\s\S]*?)\}\s*async")]
    private static partial Regex CommentDetailRegex();
}

/// <summary>
/// 把「已经从底层流读进缓冲区的字节」重新拼回流的包装器。
/// <para>
/// CONNECT 解析后如果读取器提前读到了 TLS 握手首字节，直接用底层流会握手失败，必须先回放。
/// </para>
/// </summary>
internal sealed class PrefixedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly byte[] _prefix;
    private int _offset;

    public PrefixedReadStream(Stream inner, byte[] prefix)
    {
        _inner = inner;
        _prefix = prefix;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => _inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_offset < _prefix.Length)
        {
            var take = Math.Min(count, _prefix.Length - _offset);
            Buffer.BlockCopy(_prefix, _offset, buffer, offset, take);
            _offset += take;
            return take;
        }

        return _inner.Read(buffer, offset, count);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_offset < _prefix.Length)
        {
            var take = Math.Min(buffer.Length, _prefix.Length - _offset);
            _prefix.AsMemory(_offset, take).CopyTo(buffer);
            _offset += take;
            return ValueTask.FromResult(take);
        }

        return _inner.ReadAsync(buffer, cancellationToken);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(buffer, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && _offset >= _prefix.Length)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        if (_offset >= _prefix.Length)
        {
            return _inner.DisposeAsync();
        }

        return ValueTask.CompletedTask;
    }
}
