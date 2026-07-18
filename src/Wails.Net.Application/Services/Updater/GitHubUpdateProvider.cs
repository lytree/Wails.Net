using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Wails.Net.Application.Services.Updater;

/// <summary>
/// GitHub Releases 更新提供者，通过 GitHub REST API 获取最新发布版本。
/// <para>
/// API 端点：<c>https://api.github.com/repos/{owner}/{repo}/releases/latest</c>
/// </para>
/// <para>
/// 默认行为：
/// <list type="bullet">
/// <item>从 <c>tag_name</c> 字段提取版本号（去除前缀 'v'）。</item>
/// <item>从 <c>assets</c> 数组中选择首个匹配当前平台的资产作为下载 URL。</item>
/// <item>使用 <c>body</c> 字段作为发行说明。</item>
/// <item>使用 <c>published_at</c> 字段作为发布日期。</item>
/// </list>
/// </para>
/// <para>
/// 私有仓库或避免 API 速率限制：通过 <c>token</c> 参数设置 GitHub Personal Access Token。
/// </para>
/// </summary>
public sealed class GitHubUpdateProvider : IUpdateProvider
{
    /// <summary>
    /// GitHub API 基础 URL，可被企业版 GitHub 替换。
    /// </summary>
    private const string DefaultApiBase = "https://api.github.com/";

    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repo;
    private readonly string? _token;
    private readonly string _apiBase;
    private readonly string _assetNamePattern;

    /// <inheritdoc />
    public string Name => "github";

    /// <summary>
    /// 构造 GitHub Releases 更新提供者。
    /// </summary>
    /// <param name="httpClient">HTTP 客户端实例。</param>
    /// <param name="owner">仓库所有者（owner）。</param>
    /// <param name="repo">仓库名称（repo）。</param>
    /// <param name="token">GitHub Personal Access Token（可选，用于私有仓库或提升速率限制）。</param>
    /// <param name="apiBase">GitHub API 基础 URL，默认为 https://api.github.com/，企业版可替换。</param>
    /// <param name="assetNamePattern">
    /// 资产名称匹配模式（不区分大小写的子串匹配）。
    /// 为空时选择首个资产。例如 "windows-x64" 选择包含该子串的资产。
    /// </param>
    public GitHubUpdateProvider(
        HttpClient httpClient,
        string owner,
        string repo,
        string? token = null,
        string? apiBase = null,
        string assetNamePattern = "")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ArgumentException("owner 不能为空。", nameof(owner));
        }
        if (string.IsNullOrWhiteSpace(repo))
        {
            throw new ArgumentException("repo 不能为空。", nameof(repo));
        }
        _owner = owner;
        _repo = repo;
        _token = token;
        _apiBase = string.IsNullOrWhiteSpace(apiBase) ? DefaultApiBase : apiBase!;
        _assetNamePattern = assetNamePattern ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var url = $"{_apiBase}repos/{_owner}/{_repo}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Wails.Net", "1.0"));
        if (!string.IsNullOrEmpty(_token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // tag_name 通常为 "v1.2.3"，去除前缀 'v'
        var tagName = root.TryGetProperty("tag_name", out var tagEl) && tagEl.ValueKind == JsonValueKind.String
            ? tagEl.GetString() ?? string.Empty
            : string.Empty;
        var version = tagName.TrimStart('v', 'V');

        var body = root.TryGetProperty("body", out var bodyEl) && bodyEl.ValueKind == JsonValueKind.String
            ? bodyEl.GetString()
            : null;

        var publishedAt = root.TryGetProperty("published_at", out var pubEl) && pubEl.ValueKind == JsonValueKind.String
            && DateTime.TryParse(pubEl.GetString(), out var pubDate)
                ? pubDate
                : (DateTime?)null;

        // 选择首个匹配平台的资产
        string? downloadUrl = null;
        long? contentLength = null;
        if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsEl.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString() ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrEmpty(_assetNamePattern) ||
                    name.Contains(_assetNamePattern, StringComparison.OrdinalIgnoreCase))
                {
                    if (asset.TryGetProperty("browser_download_url", out var dlEl) && dlEl.ValueKind == JsonValueKind.String)
                    {
                        downloadUrl = dlEl.GetString();
                    }
                    if (asset.TryGetProperty("size", out var sizeEl) && sizeEl.TryGetInt64(out var size))
                    {
                        contentLength = size;
                    }
                    break;
                }
            }
        }

        return new UpdateManifest
        {
            Version = version,
            ReleaseNotes = body,
            DownloadURL = downloadUrl ?? string.Empty,
            ContentLength = contentLength,
            PublishedDate = publishedAt
        };
    }
}
