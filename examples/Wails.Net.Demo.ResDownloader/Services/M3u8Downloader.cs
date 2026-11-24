using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// m3u8（HLS）下载器：解析播放列表、选择最高码率、下载分片、AES-128 解密并按序合并为 .ts 文件。
/// </summary>
internal sealed class M3u8Downloader
{
    private const int ParallelSegments = 4;
    private readonly HttpClient _http;
    private readonly ILogger _logger;

    /// <summary>
    /// 创建 m3u8 下载器。
    /// </summary>
    /// <param name="http">不带系统代理的 HTTP 客户端。</param>
    /// <param name="logger">日志。</param>
    public M3u8Downloader(HttpClient http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// 下载整个播放列表并写入目标文件。
    /// </summary>
    /// <param name="resource">资源信息（提供直链）。</param>
    /// <param name="filePath">输出文件路径。</param>
    /// <param name="headers">请求头（Referer / UA / Cookie 等）。</param>
    /// <param name="onProgress">进度回调：已完成分片、总片数、已写入字节。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task DownloadAsync(
        ResourceItem resource,
        string filePath,
        IReadOnlyDictionary<string, string> headers,
        Action<int, int, long> onProgress,
        CancellationToken ct)
    {
        var playlistUri = new Uri(resource.Url);
        var text = await GetTextAsync(playlistUri, headers, ct).ConfigureAwait(false);

        var variants = ParseVariants(text);
        if (variants.Count > 0)
        {
            var best = variants.OrderByDescending(v => v.Bandwidth).First();
            playlistUri = new Uri(playlistUri, best.Uri);
            _logger.LogDebug("m3u8 主播放列表，选用码率 {Bandwidth} 的 {Uri}", best.Bandwidth, playlistUri);
            text = await GetTextAsync(playlistUri, headers, ct).ConfigureAwait(false);
        }

        var playlist = ParseMedia(text);
        if (playlist.Segments.Count == 0)
        {
            throw new InvalidOperationException("播放列表中没有可下载的分片");
        }

        var total = playlist.Segments.Count;
        var keyCache = new ConcurrentDictionary<string, Task<byte[]>>(StringComparer.Ordinal);
        long received = 0;

        await using var file = new FileStream(
            filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 128 * 1024, FileOptions.Asynchronous);

        if (!string.IsNullOrEmpty(playlist.InitUri))
        {
            var init = await GetBytesAsync(new Uri(playlistUri, playlist.InitUri), headers, null, ct)
                .ConfigureAwait(false);
            await file.WriteAsync(init, ct).ConfigureAwait(false);
            received += init.Length;
        }

        for (var index = 0; index < total; index += ParallelSegments)
        {
            ct.ThrowIfCancellationRequested();
            var batch = playlist.Segments
                .Skip(index)
                .Take(ParallelSegments)
                .ToList();

            var chunks = await Task.WhenAll(batch.Select(segment =>
                FetchSegmentAsync(playlistUri, segment, headers, keyCache, ct))).ConfigureAwait(false);

            foreach (var chunk in chunks)
            {
                await file.WriteAsync(chunk, ct).ConfigureAwait(false);
                received += chunk.Length;
            }

            onProgress(Math.Min(index + batch.Count, total), total, received);
        }

        await file.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task<byte[]> FetchSegmentAsync(
        Uri playlistUri,
        SegmentInfo segment,
        IReadOnlyDictionary<string, string> headers,
        ConcurrentDictionary<string, Task<byte[]>> keyCache,
        CancellationToken ct)
    {
        var segmentUri = new Uri(playlistUri, segment.Uri);
        var range = segment.RangeLength is > 0
            ? new RangeHeaderValue(segment.RangeOffset ?? 0, (segment.RangeOffset ?? 0) + segment.RangeLength.Value - 1)
            : null;

        var data = await GetBytesAsync(segmentUri, headers, range, ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(segment.KeyUri))
        {
            return data;
        }

        var keyUri = new Uri(playlistUri, segment.KeyUri);
        var key = await keyCache.GetOrAdd(
            keyUri.AbsoluteUri,
            _ => GetBytesAsync(keyUri, headers, null, ct)).ConfigureAwait(false);

        var iv = segment.Iv ?? BuildSequenceIv(segment.Sequence);
        return Decrypt(data, key, iv);
    }

    private async Task<string> GetTextAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        using var request = BuildRequest(uri, headers, null);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private async Task<byte[]> GetBytesAsync(
        Uri uri, IReadOnlyDictionary<string, string> headers, RangeHeaderValue? range, CancellationToken ct)
    {
        using var request = BuildRequest(uri, headers, range);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private static HttpRequestMessage BuildRequest(
        Uri uri, IReadOnlyDictionary<string, string> headers, RangeHeaderValue? range)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        foreach (var (name, value) in headers)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        if (!request.Headers.Contains("Accept"))
        {
            request.Headers.TryAddWithoutValidation("Accept", "*/*");
        }
        if (range is not null)
        {
            request.Headers.Range = range;
        }

        return request;
    }

    private static byte[] Decrypt(byte[] data, byte[] key, byte[] iv)
    {
        try
        {
            return Transform(data, key, iv, PaddingMode.PKCS7);
        }
        catch (CryptographicException)
        {
            // 少数站点分片未按 PKCS7 填充，按无填充重试并手工裁剪
            var plain = Transform(data, key, iv, PaddingMode.None);
            var padding = plain.Length > 0 ? plain[^1] : 0;
            return padding > 0 && padding <= 16 && padding <= plain.Length
                ? plain[..^padding]
                : plain;
        }
    }

    private static byte[] Transform(byte[] data, byte[] key, byte[] iv, PaddingMode padding)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = padding;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] BuildSequenceIv(long sequence)
    {
        var iv = new byte[16];
        var value = (ulong)Math.Max(0, sequence);
        for (var i = 0; i < 8; i++)
        {
            iv[15 - i] = (byte)(value >> (i * 8));
        }

        return iv;
    }

    private static List<(string Uri, long Bandwidth)> ParseVariants(string text)
    {
        var result = new List<(string Uri, long Bandwidth)>();
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bandwidth = ParseLong(GetAttribute(line, "BANDWIDTH"));
            for (var j = i + 1; j < lines.Length; j++)
            {
                var candidate = lines[j].Trim();
                if (candidate.Length == 0 || candidate.StartsWith('#'))
                {
                    continue;
                }

                result.Add((candidate, bandwidth));
                i = j;
                break;
            }
        }

        return result;
    }

    private static MediaPlaylist ParseMedia(string text)
    {
        var playlist = new MediaPlaylist();
        var lines = text.Split('\n');

        string? keyUri = null;
        byte[]? keyIv = null;
        string? keyMethod = null;
        long sequence = 0;
        long? pendingRangeLength = null;
        long? pendingRangeOffset = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.OrdinalIgnoreCase))
            {
                sequence = ParseLong(line[(line.IndexOf(':') + 1)..]);
                continue;
            }

            if (line.StartsWith("#EXT-X-KEY:", StringComparison.OrdinalIgnoreCase))
            {
                keyMethod = GetAttribute(line, "METHOD");
                keyUri = GetAttribute(line, "URI");
                var ivText = GetAttribute(line, "IV");
                keyIv = string.IsNullOrEmpty(ivText) ? null : ParseIv(ivText);
                if (!string.Equals(keyMethod, "AES-128", StringComparison.OrdinalIgnoreCase))
                {
                    keyUri = null;
                }

                continue;
            }

            if (line.StartsWith("#EXT-X-MAP:", StringComparison.OrdinalIgnoreCase))
            {
                playlist.InitUri = GetAttribute(line, "URI");
                continue;
            }

            if (line.StartsWith("#EXT-X-BYTERANGE:", StringComparison.OrdinalIgnoreCase))
            {
                var spec = line[(line.IndexOf(':') + 1)..];
                var parts = spec.Split('@', 2);
                pendingRangeLength = ParseLong(parts[0]);
                pendingRangeOffset = parts.Length > 1 ? ParseLong(parts[1]) : null;
                continue;
            }

            if (line.StartsWith('#'))
            {
                continue;
            }

            playlist.Segments.Add(new SegmentInfo
            {
                Uri = line,
                KeyUri = keyUri,
                Iv = keyIv,
                Sequence = sequence,
                RangeLength = pendingRangeLength,
                RangeOffset = pendingRangeOffset,
            });

            sequence++;
            pendingRangeLength = null;
            pendingRangeOffset = null;
        }

        return playlist;
    }

    private static string? GetAttribute(string line, string name)
    {
        var index = line.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + name.Length + 1;
        if (start >= line.Length)
        {
            return null;
        }

        if (line[start] == '"')
        {
            var end = line.IndexOf('"', start + 1);
            return end > start ? line[(start + 1)..end] : null;
        }

        var comma = line.IndexOf(',', start);
        return (comma < 0 ? line[start..] : line[start..comma]).Trim();
    }

    private static byte[]? ParseIv(string text)
    {
        var value = text.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        if (value.Length == 0 || value.Length > 32)
        {
            return null;
        }

        value = value.PadLeft(32, '0');
        var iv = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            if (!byte.TryParse(value.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
            {
                return null;
            }

            iv[i] = parsed;
        }

        return iv;
    }

    private static long ParseLong(string? text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private sealed class MediaPlaylist
    {
        public List<SegmentInfo> Segments { get; } = [];

        public string? InitUri { get; set; }
    }

    private sealed class SegmentInfo
    {
        public string Uri { get; set; } = string.Empty;

        public string? KeyUri { get; set; }

        public byte[]? Iv { get; set; }

        public long Sequence { get; set; }

        public long? RangeLength { get; set; }

        public long? RangeOffset { get; set; }
    }
}
