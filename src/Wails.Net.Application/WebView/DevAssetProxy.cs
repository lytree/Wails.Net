using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Application.Webview;

/// <summary>
/// dev 模式资产反向代理。
/// <para>
/// 对应 Wails v3 Go 版本 <c>internal/assetserver/build_dev.go</c>：dev 模式下 assetserver 将
/// 静态资源请求反向代理到前端 dev server（vite/其它），并用大容量连接池
/// （<c>devProxyMaxIdleConnsPerHost=512</c>）与重试传输（<c>retryTransport</c>）缓解前端大量动态
/// import 并发连接导致的 TIME_WAIT 端口耗尽。
/// </para>
/// <para>
/// Wails.Net 的对应实现：当宿主配置了 <c>DesktopHostOptions.DevServerUrl</c>（由
/// <see cref="Hosting.DesktopApplicationBuilder"/> 创建并注入到 <see cref="Application.DevAssetProxy"/>）时，
/// <see cref="WebviewRequestPipeline"/> 在处理非 IPC / 非 stream 的资源请求时优先委托本类，
/// 把请求转发到前端 dev server 并回传其响应；生产模式（未配置 DevServerUrl）不启用，行为与改造前一致。
/// </para>
/// <para>
/// <b>保留上游 Content-Type</b>：vite 对 <c>.ts</c> / 模块请求返回的 MIME 与本地「按扩展名推断」不同，
/// 直接透传可避免 ES module 因 MIME 不符被浏览器拒绝；同时透传
/// <c>Cache-Control</c> / <c>ETag</c> / <c>Last-Modified</c> / <c>Vary</c> 等缓存相关头。
/// </para>
/// <remarks>
/// 本类仅做 HTTP 转发与映射：URL 拼接、重试策略等纯逻辑可单测；端到端行为需真实 dev server 与 GUI 会话验证。
/// </remarks>
/// </summary>
internal sealed class DevAssetProxy : IDisposable
{
    /// <summary>
    /// 连接池并发上限。对齐 Go <c>devProxyMaxIdleConnsPerHost=512</c>：
    /// 提高并发与空闲连接上限，避免前端大量动态 import 导致的 TIME_WAIT 端口耗尽。
    /// </summary>
    internal const int MaxConnectionsPerServer = 512;

    /// <summary>默认最大尝试次数（首次 + 2 次重试）。</summary>
    internal const int DefaultMaxAttempts = 3;

    /// <summary>默认连接超时（与嵌入式 WebView2 初始化宽限期内的等待相匹配）。</summary>
    internal static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly Uri _baseUri;
    private readonly HttpClient _client;
    private readonly HttpMessageHandler _handler;
    private readonly bool _ownsHandler;
    private readonly ILogger? _logger;
    private readonly int _maxAttempts;
    private readonly Func<int, TimeSpan> _retryDelay;
    private bool _disposed;

    /// <summary>
    /// 创建 dev 资产反向代理。
    /// </summary>
    /// <param name="devServerBaseUri">前端 dev server 的 http/https 绝对基地址。</param>
    /// <param name="logger">可选日志记录器。</param>
    /// <param name="handler">可选的 HTTP 消息处理器（测试注入用）；为 null 时创建带大容量连接池的 <see cref="SocketsHttpHandler"/>。</param>
    /// <param name="maxAttempts">最大尝试次数（含首次），至少为 1。</param>
    /// <param name="retryDelay">重试延迟提供器（测试注入用）；为 null 时使用默认指数退避。</param>
    internal DevAssetProxy(
        Uri devServerBaseUri,
        ILogger? logger = null,
        HttpMessageHandler? handler = null,
        int maxAttempts = DefaultMaxAttempts,
        Func<int, TimeSpan>? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(devServerBaseUri);
        if (!devServerBaseUri.IsAbsoluteUri ||
            (devServerBaseUri.Scheme != Uri.UriSchemeHttp && devServerBaseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                $"dev server URL 必须是 http/https 绝对 URI：{devServerBaseUri}", nameof(devServerBaseUri));
        }

        _baseUri = devServerBaseUri;
        _logger = logger;
        _maxAttempts = maxAttempts < 1 ? 1 : maxAttempts;
        _retryDelay = retryDelay ?? DefaultRetryDelay;
        _ownsHandler = handler is null;
        _handler = handler ?? new SocketsHttpHandler
        {
            MaxConnectionsPerServer = MaxConnectionsPerServer,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = DefaultConnectTimeout,
        };
        // disposeHandler:false —— 处理器由本类按 _ownsHandler 决定是否释放（注入的处理器不释放）。
        _client = new HttpClient(_handler, disposeHandler: false)
        {
            // 单请求不设固定超时；由调用方的 CancellationToken 控制。
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>前端 dev server 基地址（只读，供诊断 / 测试断言）。</summary>
    internal Uri BaseUri => _baseUri;

    /// <summary>
    /// 转发一次 GET 资源请求到前端 dev server，并映射为 <see cref="WebviewResponse"/>。
    /// </summary>
    /// <param name="path">请求绝对路径（以 '/' 开头）。</param>
    /// <param name="query">请求查询串（含前导 '?' 或省略）；可为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 上游响应（含状态码 / Content-Type / 响应体 / 缓存头）；
    /// 在重试耗尽后仍失败时返回 <c>null</c>，由调用方回退到本地资源，避免中断页面加载。
    /// </returns>
    internal async Task<WebviewResponse?> TryGetAsync(
        string path,
        string? query,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = BuildTargetUri(_baseUri, path, query);

        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, target);
                // 请求「不压缩」并在收到后原样转发：WebView2 的响应对象无法正确承载
                // 由本层解压后的实体再声明 Content-Encoding（会二次解压 / 校验不一致）。
                request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");

                using var response = await _client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                return new WebviewResponse
                {
                    StatusCode = (int)response.StatusCode,
                    StatusText = response.ReasonPhrase ?? "OK",
                    ContentType = contentType,
                    Body = body,
                    Headers = CollectForwardHeaders(response),
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
            {
                if (attempt >= _maxAttempts)
                {
                    _logger?.LogWarning(ex, "dev 资产代理转发失败（已尝试 {Attempts} 次）：{Target}", attempt, target);
                    return null;
                }

                _logger?.LogDebug(
                    ex, "dev 资产代理转发失败，{DelayMs}ms 后重试：{Target}", _retryDelay(attempt).TotalMilliseconds, target);
                await Task.Delay(_retryDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 拼接转发目标 URI：<c>base + path + query</c>。
    /// <para>
    /// base 的 scheme/host/port 与可选路径前缀保留；<paramref name="path"/> 以 '/' 开头（缺失时补齐）；
    /// <paramref name="query"/> 缺前导 '?' 时补齐。对应 Go dev 代理的 <c>NewSingleHostReverseProxy</c>
    /// 语义（原样透传路径与查询串）。
    /// </para>
    /// </summary>
    /// <param name="baseUri">前端 dev server 基地址。</param>
    /// <param name="path">请求绝对路径。</param>
    /// <param name="query">请求查询串（含或不含前导 '?'）。</param>
    /// <returns>转发目标 URI。</returns>
    internal static Uri BuildTargetUri(Uri baseUri, string path, string? query)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        var builder = new StringBuilder(baseUri.GetLeftPart(UriPartial.Authority));

        // 保留 base 的路径前缀（若有），避免丢弃用户配置的子路径。
        var basePath = baseUri.AbsolutePath;
        if (!string.IsNullOrEmpty(basePath) && basePath != "/")
        {
            builder.Append(basePath.TrimEnd('/'));
        }

        var relative = string.IsNullOrEmpty(path) ? "/" : path;
        if (!relative.StartsWith('/'))
        {
            relative = "/" + relative;
        }
        builder.Append(relative);

        if (!string.IsNullOrEmpty(query))
        {
            builder.Append(query.StartsWith('?') ? query : "?" + query);
        }

        return new Uri(builder.ToString());
    }

    /// <summary>
    /// 收集需要透传给 WebView 的缓存相关响应头（缺失时为 null）。
    /// </summary>
    /// <param name="response">上游响应。</param>
    /// <returns>透传头字典；无匹配头时为 null。</returns>
    private static IReadOnlyDictionary<string, string>? CollectForwardHeaders(HttpResponseMessage response)
    {
        string[] names = ["Cache-Control", "ETag", "Last-Modified", "Vary", "Expires"];
        Dictionary<string, string>? headers = null;
        foreach (var name in names)
        {
            // 缓存相关头可能落在响应头（HttpResponseHeaders）或内容头（HttpContentHeaders）上，两处都查。
            if (TryGetHeader(response.Headers, name, out var value) ||
                TryGetHeader(response.Content.Headers, name, out value))
            {
                (headers ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))[name] = value;
            }
        }

        return headers;

        static bool TryGetHeader(
            System.Net.Http.Headers.HttpHeaders source,
            string name,
            out string value)
        {
            if (source.TryGetValues(name, out var values))
            {
                value = string.Join(", ", values);
                return true;
            }

            value = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// 默认重试延迟：指数退避（第 1 次 50ms、第 2 次 100ms…），上限封顶 1.6s。
    /// </summary>
    /// <param name="attempt">已失败的尝试次数（从 1 开始）。</param>
    /// <returns>重试前等待时长。</returns>
    private static TimeSpan DefaultRetryDelay(int attempt)
    {
        var exponent = Math.Min(attempt, 5) - 1;
        return TimeSpan.FromMilliseconds(50 * Math.Pow(2, exponent));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
        if (_ownsHandler)
        {
            _handler.Dispose();
        }
    }
}
