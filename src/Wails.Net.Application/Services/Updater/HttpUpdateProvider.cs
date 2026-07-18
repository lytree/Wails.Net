using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Wails.Net.Application.Services.Updater;

/// <summary>
/// HTTP URL 更新提供者，从单个 URL 获取更新清单 JSON。
/// <para>
/// 对应 Wails v3 默认的更新检查行为：向 <see cref="UpdaterConfig.UpdateURL"/> 发送 GET 请求，
/// 解析响应为 <see cref="UpdateManifest"/>。
/// </para>
/// <para>
/// 此提供者为向后兼容保留：<see cref="UpdaterService"/> 在未显式注册任何 provider 时，
/// 会自动使用此提供者并从 <see cref="UpdaterConfig.UpdateURL"/> 拉取。
/// </para>
/// </summary>
public sealed class HttpUpdateProvider : IUpdateProvider
{
    /// <summary>
    /// HTTP 客户端实例。
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 更新检查 URL。
    /// </summary>
    private readonly string _updateUrl;

    /// <summary>
    /// 附加到请求的 HTTP 头列表。
    /// </summary>
    private readonly HTTPHeader[] _headers;

    /// <inheritdoc />
    public string Name => "http";

    /// <summary>
    /// 使用指定 HttpClient、URL 和请求头构造 HTTP 更新提供者。
    /// </summary>
    /// <param name="httpClient">HTTP 客户端实例。</param>
    /// <param name="updateUrl">更新检查 URL。</param>
    /// <param name="headers">附加 HTTP 头，可为空。</param>
    public HttpUpdateProvider(HttpClient httpClient, string updateUrl, HTTPHeader[]? headers = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(updateUrl))
        {
            throw new ArgumentException("更新检查 URL 不能为空。", nameof(updateUrl));
        }
        _updateUrl = updateUrl;
        _headers = headers ?? [];
    }

    /// <inheritdoc />
    public async Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _updateUrl);
        foreach (var header in _headers)
        {
            if (!string.IsNullOrEmpty(header.Key))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<UpdateManifest>(json);
    }
}
