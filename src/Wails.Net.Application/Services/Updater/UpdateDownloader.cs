using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Wails.Net.Application.Services.Updater;

/// <summary>
/// 更新下载器，负责流式下载更新包，支持断点续传、进度回调和校验和验证。
/// 对应 Wails v3 Go 版本 download.go 中的 download 函数。
/// </summary>
public sealed class UpdateDownloader
{
    /// <summary>
    /// HTTP 客户端实例。
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 附加到每个请求的 HTTP 头。
    /// </summary>
    private readonly HTTPHeader[] _headers;

    /// <summary>
    /// 是否禁用校验和验证。
    /// </summary>
    private readonly bool _disableChecksumVerification;

    /// <summary>
    /// 使用指定 HttpClient、请求头和校验和配置构造下载器实例。
    /// </summary>
    /// <param name="httpClient">HTTP 客户端实例。</param>
    /// <param name="headers">附加请求头列表，可为 null。</param>
    /// <param name="disableChecksumVerification">是否禁用校验和验证。</param>
    public UpdateDownloader(HttpClient httpClient, HTTPHeader[]? headers = null, bool disableChecksumVerification = false)
    {
        _httpClient = httpClient;
        _headers = headers ?? [];
        _disableChecksumVerification = disableChecksumVerification;
    }

    /// <summary>
    /// 异步下载更新包到指定路径。
    /// 使用 HttpCompletionOption.ResponseHeadersRead 流式下载，支持断点续传和进度回调。
    /// 下载完成后自动验证 SHA256 校验和（除非禁用）。
    /// </summary>
    /// <param name="manifest">更新清单，包含下载 URL 和校验和信息。</param>
    /// <param name="targetPath">本地目标文件路径。</param>
    /// <param name="progress">进度回调，可为 null。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示下载操作的异步任务。</returns>
    /// <exception cref="InvalidOperationException">下载 URL 为空。</exception>
    /// <exception cref="InvalidDataException">校验和验证失败。</exception>
    public async Task DownloadAsync(
        UpdateManifest manifest,
        string targetPath,
        IProgress<UpdateProgressEventArgs>? progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(manifest.DownloadURL))
        {
            throw new InvalidOperationException("更新清单中的下载 URL 为空。");
        }

        // 确保目标目录存在
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // 检查本地文件以支持断点续传
        long existingSize = 0;
        if (File.Exists(targetPath))
        {
            existingSize = new FileInfo(targetPath).Length;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, manifest.DownloadURL);

        // 附加自定义请求头
        foreach (var header in _headers)
        {
            if (!string.IsNullOrEmpty(header.Key))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // 断点续传：添加 Range 头
        if (existingSize > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingSize, null);
        }

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // 判断服务器是否支持断点续传（206 = Partial Content）
        var isResume = response.StatusCode == System.Net.HttpStatusCode.PartialContent && existingSize > 0;
        long totalBytes;
        long bytesDownloaded;

        if (isResume)
        {
            // 从 Content-Range 获取总大小
            totalBytes = response.Content.Headers.ContentRange?.Length ?? -1;
            bytesDownloaded = existingSize;
        }
        else
        {
            // 全新下载，截断已有文件
            existingSize = 0;
            bytesDownloaded = 0;
            totalBytes = response.Content.Headers.ContentLength ?? -1;
        }

        // 如果响应未提供总大小，使用清单中的 ContentLength
        if (totalBytes <= 0 && manifest.ContentLength is { } manifestLength and > 0)
        {
            totalBytes = manifestLength;
        }

        var fileMode = isResume ? FileMode.Append : FileMode.Create;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(
            targetPath, fileMode, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        long bytesThisSession = 0;

        int read;
        while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            bytesDownloaded += read;
            bytesThisSession += read;

            // 节流进度报告（约每 100ms 一次，对应 Go 版本的 ~10/sec）
            var elapsed = stopwatch.Elapsed;
            if (elapsed - lastReport >= TimeSpan.FromMilliseconds(100) ||
                (totalBytes > 0 && bytesDownloaded >= totalBytes))
            {
                lastReport = elapsed;
                var elapsedSeconds = elapsed.TotalSeconds;
                var bps = elapsedSeconds > 0 ? (long)(bytesThisSession / elapsedSeconds) : 0;
                var pct = totalBytes > 0 ? (double)bytesDownloaded / totalBytes * 100.0 : 0;

                progress?.Report(new UpdateProgressEventArgs
                {
                    ProgressPercentage = pct,
                    BytesDownloaded = bytesDownloaded,
                    TotalBytes = totalBytes,
                    BytesPerSecond = bps
                });
            }
        }

        await fileStream.FlushAsync(ct);

        // 校验和验证
        if (!_disableChecksumVerification)
        {
            var checksum = manifest.Checksum;
            if (!string.IsNullOrEmpty(checksum))
            {
                var valid = await VerifyChecksumAsync(targetPath, checksum!);
                if (!valid)
                {
                    File.Delete(targetPath);
                    throw new InvalidDataException("校验和验证失败：下载的文件 SHA256 与预期值不匹配。");
                }
            }
        }
    }

    /// <summary>
    /// 异步验证文件 SHA256 校验和是否与预期值匹配。
    /// 使用 SHA256.HashDataAsync（.NET 10）流式计算哈希。
    /// </summary>
    /// <param name="filePath">要验证的文件路径。</param>
    /// <param name="expectedChecksum">预期的 SHA256 十六进制字符串。</param>
    /// <returns>校验和匹配返回 true，否则返回 false。</returns>
    public async Task<bool> VerifyChecksumAsync(string filePath, string expectedChecksum)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        expectedChecksum = expectedChecksum.Trim().ToLowerInvariant();

        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        var hashBytes = await SHA256.HashDataAsync(stream);
        var actualChecksum = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase);
    }
}
