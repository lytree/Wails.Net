using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Wails.Net.Application.Services.Updater;

/// <summary>
/// GitLab Releases 更新提供者，通过 GitLab REST API 获取最新发布版本。
/// <para>
/// API 端点：<c>{apiBase}/projects/{projectId}/releases/permalink/latest</c>
/// 默认 apiBase 为 <c>https://gitlab.com/api/v4/</c>，自托管 GitLab 可替换。
/// </para>
/// <para>
/// 默认行为：
/// <list type="bullet">
/// <item>从 <c>tag_name</c> 字段提取版本号（去除前缀 'v'）。</item>
/// <item>从 <c>assets.links</c> 数组中选择首个匹配平台的链接作为下载 URL。</item>
/// <item>使用 <c>description</c> 字段作为发行说明。</item>
/// <item>使用 <c>released_at</c> 字段作为发布日期。</item>
/// </list>
/// </para>
/// <para>
/// 私有仓库或避免 API 速率限制：通过 <c>token</c> 参数设置 GitLab Personal Access Token。
/// </para>
/// </summary>
public sealed class GitLabUpdateProvider : IUpdateProvider
{
    /// <summary>
    /// GitLab API 默认基础 URL（公共 GitLab）。
    /// </summary>
    private const string DefaultApiBase = "https://gitlab.com/api/v4/";

    private readonly HttpClient _httpClient;
    private readonly string _projectId;
    private readonly string? _token;
    private readonly string _apiBase;
    private readonly string _assetNamePattern;

    /// <inheritdoc />
    public string Name => "gitlab";

    /// <summary>
    /// 构造 GitLab Releases 更新提供者。
    /// </summary>
    /// <param name="httpClient">HTTP 客户端实例。</param>
    /// <param name="projectId">
    /// GitLab 项目 ID（数字形式，如 12345）或 URL 编码的路径（如 mygroup%2Fmyproject）。
    /// </param>
    /// <param name="token">GitLab Personal Access Token（可选）。</param>
    /// <param name="apiBase">GitLab API 基础 URL，默认为 https://gitlab.com/api/v4/。</param>
    /// <param name="assetNamePattern">
    /// 资产链接名称匹配模式（不区分大小写的子串匹配）。
    /// 为空时选择首个链接。例如 "windows-x64"。
    /// </param>
    public GitLabUpdateProvider(
        HttpClient httpClient,
        string projectId,
        string? token = null,
        string? apiBase = null,
        string assetNamePattern = "")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("projectId 不能为空。", nameof(projectId));
        }
        _projectId = projectId;
        _token = token;
        _apiBase = string.IsNullOrWhiteSpace(apiBase) ? DefaultApiBase : apiBase!;
        _assetNamePattern = assetNamePattern ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var url = $"{_apiBase}projects/{Uri.EscapeDataString(_projectId)}/releases/permalink/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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

        var description = root.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String
            ? descEl.GetString()
            : null;

        var releasedAt = root.TryGetProperty("released_at", out var relEl) && relEl.ValueKind == JsonValueKind.String
            && DateTime.TryParse(relEl.GetString(), out var relDate)
                ? relDate
                : (DateTime?)null;

        // 从 assets.links 数组中选择首个匹配平台的链接
        string? downloadUrl = null;
        if (root.TryGetProperty("assets", out var assetsEl) &&
            assetsEl.TryGetProperty("links", out var linksEl) &&
            linksEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in linksEl.EnumerateArray())
            {
                var name = link.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString() ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrEmpty(_assetNamePattern) ||
                    name.Contains(_assetNamePattern, StringComparison.OrdinalIgnoreCase))
                {
                    if (link.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
                    {
                        downloadUrl = urlEl.GetString();
                    }
                    break;
                }
            }
        }

        return new UpdateManifest
        {
            Version = version,
            ReleaseNotes = description,
            DownloadURL = downloadUrl ?? string.Empty,
            PublishedDate = releasedAt
        };
    }
}
