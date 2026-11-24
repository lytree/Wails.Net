using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 下载管理器：直链（多连接分片）+ m3u8 下载，带并发控制、进度推送与取消。
/// <para>
/// 与 res-downloader 对齐：重放嗅探到的请求头（按 <c>UseHeaders</c> 白名单）、覆盖 UA、
/// 支持上游代理下载（<c>DownloadProxy</c>）、视频号清晰度改写（<c>Quality</c>）、
/// 按 <c>FilenameLen</c> / <c>FilenameTime</c> 生成文件名、按 <c>TaskNumber</c> 分段并发。
/// </para>
/// </summary>
public sealed class DownloadManager : IDisposable
{
    private const long SegmentThresholdBytes = 1024 * 1024;

    private readonly SettingsStore _settings;
    private readonly ResourceRepository _repository;
    private readonly ILogger<DownloadManager> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellation = new();
    private readonly object _clientGate = new();

    private HttpClient? _client;
    private string _clientKey = string.Empty;
    private int _running;
    private bool _disposed;

    /// <summary>
    /// 创建下载管理器。
    /// </summary>
    /// <param name="settings">设置存储。</param>
    /// <param name="repository">资源仓库。</param>
    /// <param name="logger">日志。</param>
    public DownloadManager(SettingsStore settings, ResourceRepository repository, ILogger<DownloadManager> logger)
    {
        _settings = settings;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>资源状态变化时触发（在后台线程上调用）。</summary>
    public event Action<ResourceItem>? ResourceUpdated;

    /// <summary>
    /// 将资源加入下载队列并立即开始（受并发限制）。
    /// </summary>
    /// <param name="resource">要下载的资源。</param>
    /// <returns>入队后的资源快照；已在下载中返回 <c>null</c>。</returns>
    public ResourceItem? Enqueue(ResourceItem resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (_cancellation.ContainsKey(resource.Id))
        {
            return null;
        }

        var cts = new CancellationTokenSource();
        if (!_cancellation.TryAdd(resource.Id, cts))
        {
            cts.Dispose();
            return null;
        }

        var queued = _repository.Update(resource.Id, item =>
        {
            item.Status = ResourceStatus.Pending;
            item.Progress = 0;
            item.Message = "排队中";
        });

        if (queued is not null)
        {
            ResourceUpdated?.Invoke(queued);
        }

        _ = RunAsync(resource, cts);
        return queued;
    }

    /// <summary>
    /// 取消指定资源的下载。
    /// </summary>
    /// <param name="resourceId">资源标识。</param>
    /// <returns>找到并已请求取消返回 <c>true</c>。</returns>
    public bool Cancel(string resourceId)
    {
        if (!_cancellation.TryGetValue(resourceId, out var cts))
        {
            return false;
        }

        cts.Cancel();
        return true;
    }

    /// <summary>判断资源是否正在下载（含排队）。</summary>
    /// <param name="resourceId">资源标识。</param>
    /// <returns>正在下载返回 <c>true</c>。</returns>
    public bool IsActive(string resourceId) => _cancellation.ContainsKey(resourceId);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var cts in _cancellation.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _cancellation.Clear();

        lock (_clientGate)
        {
            _client?.Dispose();
            _client = null;
        }
    }

    private async Task RunAsync(ResourceItem resource, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        var settings = _settings.Get();
        string? filePath = null;
        var completed = false;

        try
        {
            await AcquireSlotAsync(ct).ConfigureAwait(false);

            Update(resource.Id, item =>
            {
                item.Status = ResourceStatus.Running;
                item.Progress = 0;
                item.Message = "0%";
            });

            var directory = AppPaths.EnsureDirectory(settings.SaveDirectory);
            var fileName = ResourceNaming.BuildFileName(settings, resource);
            if (resource.Classify == ResourceClassify.M3u8)
            {
                // 分片合并后是可播放的 MPEG-TS
                fileName = Path.GetFileNameWithoutExtension(fileName) + ".ts";
            }

            filePath = MakeUniquePath(Path.Combine(directory, fileName));
            var headers = BuildHeaders(settings, resource);
            var client = GetClient(settings);

            if (resource.Classify == ResourceClassify.M3u8)
            {
                var m3u8 = new M3u8Downloader(client, _logger);
                await m3u8.DownloadAsync(
                    resource,
                    filePath,
                    headers,
                    (done, total, bytes) => Update(resource.Id, item =>
                    {
                        item.Progress = total > 0 ? Math.Round(done * 100.0 / total, 1) : 0;
                        item.Message = $"分片 {done}/{total}";
                        item.Size = bytes;
                    }),
                    ct).ConfigureAwait(false);
            }
            else
            {
                await DownloadDirectAsync(
                    settings,
                    client,
                    resource,
                    filePath,
                    headers,
                    (received, total) => Update(resource.Id, item =>
                    {
                        item.Progress = total > 0 ? Math.Round(received * 100.0 / total, 1) : 0;
                        item.Message = total > 0
                            ? $"{received * 100 / Math.Max(1, total)}%"
                            : FormatBytes(received);
                    }),
                    ct).ConfigureAwait(false);
            }

            var finalSize = new FileInfo(filePath).Length;
            completed = true;
            var completedItem = Update(resource.Id, item =>
            {
                item.Status = ResourceStatus.Done;
                item.SavePath = filePath;
                item.Size = finalSize;
                item.Progress = 100;
                item.Message = "完成";
            });

            if (completedItem is not null)
            {
                _logger.LogInformation("下载完成：{Path}（{Size} 字节）", filePath, finalSize);
            }
        }
        catch (OperationCanceledException)
        {
            Update(resource.Id, item =>
            {
                item.Status = ResourceStatus.Ready;
                item.Progress = 0;
                item.SavePath = string.Empty;
                item.Message = "已取消";
            });
            TryDelete(filePath);
        }
        catch (Exception ex)
        {
            Update(resource.Id, item =>
            {
                item.Status = ResourceStatus.Error;
                item.Message = ex.Message;
            });
            _logger.LogWarning(ex, "下载失败：{Url}", resource.Url);
            TryDelete(filePath);
        }
        finally
        {
            Interlocked.Decrement(ref _running);
            if (_cancellation.TryRemove(resource.Id, out var source))
            {
                source.Dispose();
            }

            if (!completed)
            {
                TryDelete(filePath);
            }

            cts.Dispose();
        }
    }

    private async Task DownloadDirectAsync(
        AppSettings settings,
        HttpClient client,
        ResourceItem resource,
        string filePath,
        IReadOnlyDictionary<string, string> headers,
        Action<long, long> onProgress,
        CancellationToken ct)
    {
        var url = ApplyQuality(settings, resource);
        var connections = Math.Clamp(settings.TaskNumber, 1, 16);

        long total = -1;
        var rangeSupported = false;

        if (connections > 1)
        {
            using var probe = CreateRequest(url, headers, 0, 0);
            using var response = await client
                .SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                total = response.Content.Headers.ContentRange?.Length ?? -1;
                rangeSupported = total > 0;
            }
            else if (response.IsSuccessStatusCode)
            {
                total = response.Content.Headers.ContentLength ?? -1;
            }
        }

        if (rangeSupported && total > SegmentThresholdBytes)
        {
            _logger.LogDebug(
                "下载策略：分片（连接数={Connections}，总长={Total}）", connections, total);
            await DownloadSegmentsAsync(client, url, filePath, headers, connections, total, onProgress, ct)
                .ConfigureAwait(false);
            return;
        }

        _logger.LogDebug(
            "下载策略：单连接（连接数={Connections}，总长={Total}，支持 Range={RangeSupported}）",
            connections, total, rangeSupported);
        await DownloadSingleAsync(client, url, filePath, headers, onProgress, ct).ConfigureAwait(false);
    }

    private async Task DownloadSingleAsync(
        HttpClient client,
        string url,
        string filePath,
        IReadOnlyDictionary<string, string> headers,
        Action<long, long> onProgress,
        CancellationToken ct)
    {
        using var request = CreateRequest(url, headers, null, null);
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"源站返回 HTTP {(int)response.StatusCode}");
        }

        var total = response.Content.Headers.ContentLength ?? -1;
        var throttle = new ProgressThrottle();
        long received = 0;

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = new FileStream(
            filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 128 * 1024, FileOptions.Asynchronous);

        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;
                if (throttle.ShouldReport())
                {
                    onProgress(received, total);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        onProgress(received, total > 0 ? total : received);
        await file.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task DownloadSegmentsAsync(
        HttpClient client,
        string url,
        string filePath,
        IReadOnlyDictionary<string, string> headers,
        int connections,
        long total,
        Action<long, long> onProgress,
        CancellationToken ct)
    {
        var chunk = total / connections;
        if (chunk <= 0)
        {
            await DownloadSingleAsync(client, url, filePath, headers, onProgress, ct).ConfigureAwait(false);
            return;
        }

        var parts = new string[connections];
        long received = 0;
        var throttle = new ProgressThrottle();

        var tasks = new List<Task>(connections);
        for (var i = 0; i < connections; i++)
        {
            var index = i;
            var start = index * chunk;
            var end = index == connections - 1 ? total - 1 : start + chunk - 1;
            var partPath = $"{filePath}.part{index}";
            parts[index] = partPath;

            tasks.Add(Task.Run(async () =>
            {
                await DownloadRangeAsync(client, url, partPath, headers, start, end, receivedBytes =>
                {
                    var now = Interlocked.Add(ref received, receivedBytes);
                    if (throttle.ShouldReport())
                    {
                        onProgress(now, total);
                    }
                }, ct).ConfigureAwait(false);
            }, ct));
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            foreach (var part in parts)
            {
                TryDelete(part);
            }

            throw;
        }

        await MergePartsAsync(parts, filePath, ct).ConfigureAwait(false);
        onProgress(total, total);
    }

    private async Task DownloadRangeAsync(
        HttpClient client,
        string url,
        string partPath,
        IReadOnlyDictionary<string, string> headers,
        long start,
        long end,
        Action<int> onBytes,
        CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                using var request = CreateRequest(url, headers, start, end);
                using var response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.PartialContent)
                {
                    throw new InvalidOperationException($"分片请求返回 HTTP {(int)response.StatusCode}");
                }

                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var file = new FileStream(
                    partPath, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);

                var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
                try
                {
                    while (true)
                    {
                        var read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
                        if (read <= 0)
                        {
                            break;
                        }

                        await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        onBytes(read);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                await file.FlushAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (Exception) when (attempt < 3 && !ct.IsCancellationRequested)
            {
                // 分片失败重试两次，避免个别连接抖动导致整个任务失败
                await Task.Delay(300 * attempt, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task MergePartsAsync(string[] parts, string filePath, CancellationToken ct)
    {
        await using (var output = new FileStream(
                         filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 128 * 1024,
                         FileOptions.Asynchronous))
        {
            foreach (var part in parts)
            {
                await using var input = new FileStream(
                    part, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous);
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
            }

            await output.FlushAsync(ct).ConfigureAwait(false);
        }

        foreach (var part in parts)
        {
            TryDelete(part);
        }
    }

    private HttpClient GetClient(AppSettings settings)
    {
        IWebProxy? proxy = null;
        var useProxy = settings.DownloadProxy
                       && settings.OpenProxy
                       && TryBuildProxy(settings.UpstreamProxy, out proxy);
        var key = useProxy ? settings.UpstreamProxy.Trim() : "direct";

        lock (_clientGate)
        {
            if (_client is not null && _clientKey == key)
            {
                return _client;
            }

            _client?.Dispose();
            _clientKey = key;

            var handler = new SocketsHttpHandler
            {
                // 未配置下载代理时强制直连：系统代理此刻指向本机，会形成回环
                UseProxy = useProxy,
                Proxy = useProxy ? proxy : null,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 8,
                ConnectTimeout = TimeSpan.FromSeconds(20),
                AutomaticDecompression = DecompressionMethods.None,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };

            _client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            return _client;
        }
    }

    private static bool TryBuildProxy(string? value, out IWebProxy? proxy)
    {
        proxy = null;
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        var webProxy = new WebProxy(uri);
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            webProxy.Credentials = new NetworkCredential(
                Uri.UnescapeDataString(parts[0]),
                parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
        }

        proxy = webProxy;
        return true;
    }

    private static HttpRequestMessage CreateRequest(
        string url,
        IReadOnlyDictionary<string, string> headers,
        long? from,
        long? to)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (name, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(value)
                || name.Equals("Host", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (!request.Headers.Contains("Accept"))
        {
            request.Headers.TryAddWithoutValidation("Accept", "*/*");
        }

        if (from is not null && to is not null)
        {
            request.Headers.Range = new RangeHeaderValue(from.Value, to.Value);
        }

        return request;
    }

    /// <summary>
    /// 按设置生成下载请求头：重放嗅探到的头（白名单过滤），并用设置中的 UA 覆盖。
    /// </summary>
    private static Dictionary<string, string> BuildHeaders(AppSettings settings, ResourceItem resource)
    {
        var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (resource.OtherData.TryGetValue("headers", out var json) && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (parsed is not null)
                {
                    foreach (var (name, value) in parsed)
                    {
                        captured[name] = value;
                    }
                }
            }
            catch (JsonException)
            {
                // 头部快照损坏时忽略，仅用默认头
            }
        }

        var keys = settings.UseHeaders.Equals("default", StringComparison.OrdinalIgnoreCase)
            ? MimeTypeMap.DefaultReplayHeaders
            : settings.UseHeaders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (captured.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                result[key] = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.UserAgent))
        {
            result["User-Agent"] = settings.UserAgent;
        }

        if (!result.ContainsKey("Referer")
            && Uri.TryCreate(resource.Url, UriKind.Absolute, out var uri))
        {
            result["Referer"] = uri.GetLeftPart(UriPartial.Authority) + "/";
        }

        return result;
    }

    /// <summary>
    /// 视频号清晰度改写（对应 res-downloader 的 Quality 处理）。
    /// </summary>
    private static string ApplyQuality(AppSettings settings, ResourceItem resource)
    {
        var url = resource.Url;
        if (settings.Quality <= 0 || !url.Contains("qq.com", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (settings.Quality == 1
            && url.Contains("encfilekey=", StringComparison.Ordinal)
            && url.Contains("token=", StringComparison.Ordinal)
            && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var key = GetQueryParameter(uri.Query, "encfilekey");
            var token = GetQueryParameter(uri.Query, "token");
            if (key.Length > 0 && token.Length > 0)
            {
                return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}?encfilekey={key}&token={token}";
            }

            return url;
        }

        if (settings.Quality > 1
            && resource.OtherData.TryGetValue("wx_file_formats", out var formatsText)
            && !string.IsNullOrWhiteSpace(formatsText))
        {
            var formats = formatsText.Split('#', StringSplitOptions.RemoveEmptyEntries);
            if (formats.Length >= 2)
            {
                var qualityMap = new[] { formats[0], formats[formats.Length / 2], formats[^1] };
                var index = Math.Clamp(settings.Quality - 2, 0, qualityMap.Length - 1);
                return url + "&X-snsvideoflag=" + qualityMap[index];
            }
        }

        return url;
    }

    private static string GetQueryParameter(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator > 0 && pair[..separator].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return pair[(separator + 1)..];
            }
        }

        return string.Empty;
    }

    private async Task AcquireSlotAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var limit = Math.Max(1, _settings.Get().DownNumber);
            var current = Volatile.Read(ref _running);
            if (current < limit && Interlocked.CompareExchange(ref _running, current + 1, current) == current)
            {
                return;
            }

            await Task.Delay(150, ct).ConfigureAwait(false);
        }
    }

    private ResourceItem? Update(string resourceId, Action<ResourceItem> mutate)
    {
        var updated = _repository.Update(resourceId, mutate);
        if (updated is not null)
        {
            ResourceUpdated?.Invoke(updated);
        }

        return updated;
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return path;
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 文件被占用时保留残留文件
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        string[] units = ["KB", "MB", "GB", "TB"];
        double size = bytes / 1024.0;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{size:0.#} {units[unit]}");
    }

    /// <summary>
    /// 进度节流：每 200ms 最多推送一次，避免高频事件压垮前端。
    /// </summary>
    private sealed class ProgressThrottle
    {
        private long _lastTicks;

        public bool ShouldReport()
        {
            var now = Stopwatch.GetTimestamp();
            var last = Interlocked.Read(ref _lastTicks);
            if (now - last < Stopwatch.Frequency / 5)
            {
                return false;
            }

            Interlocked.Exchange(ref _lastTicks, now);
            return true;
        }
    }
}
