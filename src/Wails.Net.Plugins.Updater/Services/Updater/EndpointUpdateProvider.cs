using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Wails.Net.Plugins.Updater.Services;

/// <summary>
/// Endpoint 更新提供者：从任意 URI（http/https/file 或注入的自定义协议）获取
/// "Wails Update Manifest" JSON 文档，并按当前平台/架构选择对应资产。
/// <para>
/// 对应 Wails v3 Go 版本 <c>pkg/updater/providers/endpoint/endpoint.go</c> 中的
/// <c>endpoint.Provider</c>（<c>Check</c> / <c>buildURL</c> / <c>pickArtifact</c> /
/// <c>buildVerification</c> / <c>resolveURL</c>）。
/// </para>
/// <para>
/// 协议与宿主无关：静态文件服务器（S3 / GitHub Pages / 任意 CDN）可发布一份列出全部
/// 平台资产的清单；动态更新服务器则可读取本提供者发送的
/// <c>platform</c>/<c>arch</c>/<c>version</c>/<c>channel</c> 查询参数后，返回仅含单个
/// 资产的清单。两种形态是同一份文档，提供者都会挑选与运行平台匹配的资产。
/// </para>
/// <para>
/// 与 Go 版本的差异：Wails.Net 的 <see cref="IUpdateProvider.CheckAsync"/> 不携带当前
/// 版本与平台参数，版本比较由 <see cref="UpdaterService.IsNewerVersion"/> 统一完成，
/// 因此本提供者不在此处做 semver 过滤，仅返回清单（无匹配或清单缺失时返回 null 或抛错）。
/// 平台/架构通过 <see cref="Platform"/> / <see cref="Arch"/> 显式覆盖，默认按运行时推断。
/// </para>
/// </summary>
public sealed class EndpointUpdateProvider : IUpdateProvider
{
    /// <summary>
    /// 本提供者理解的最新清单 schemaVersion（对应 Go endpoint.schemaVersionMax = 1）。
    /// 更高 schemaVersion 的清单会被明确拒绝，而不是被半解析。
    /// </summary>
    public const int SchemaVersionMax = 1;

    /// <summary>
    /// 清单响应体大小上限（8 MiB），与 Go endpoint/appcast/keygen 提供者一致。
    /// </summary>
    private const int MaxManifestBytes = 8 * 1024 * 1024;

    /// <summary>
    /// HTTP 客户端实例，用于 http/https 清单请求。
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 清单 URL 模板，可含 <c>{{platform}}</c>/<c>{{arch}}</c>/<c>{{version}}</c>/<c>{{channel}}</c> 占位符。
    /// </summary>
    private readonly string _urlTemplate;

    /// <summary>
    /// 请求的发布通道（可选）。
    /// </summary>
    private readonly string _channel;

    /// <summary>
    /// 附加到清单请求的 HTTP 头列表。
    /// </summary>
    private readonly HTTPHeader[] _headers;

    /// <summary>
    /// 资产名称匹配模式（不区分大小写的子串匹配），为空时按平台/架构选择。
    /// </summary>
    private readonly string _assetNamePattern;

    /// <summary>
    /// 自定义协议加载器，用于处理 http/https/file 之外的 scheme（如 <c>wails://</c>）。
    /// 为空时遇到不支持的协议会抛出 <see cref="NotSupportedException"/>。
    /// </summary>
    private readonly Func<string, CancellationToken, Task<Stream?>>? _customSchemeLoader;

    /// <inheritdoc />
    public string Name => "endpoint";

    /// <summary>
    /// 用于替换清单 URL 中 <c>{{version}}</c> 占位符 / <c>version</c> 查询参数的当前版本号（可选）。
    /// 对应 Go <c>endpoint.CheckRequest.CurrentVersion</c>；Wails.Net 的 provider 接口不传递
    /// 当前版本，需要动态更新服务器按版本返回清单时，可在此显式设置，为空时省略 version 参数。
    /// </summary>
    public string? CurrentVersion { get; set; }

    /// <summary>
    /// 覆盖当前运行平台（对应 Go <c>CheckRequest.Platform</c> / <c>runtime.GOOS</c>）。
    /// 为空时按运行时操作系统推断（windows / darwin / linux / android / freebsd / ios）。
    /// 主要用于测试与交叉目标场景。
    /// </summary>
    public string? Platform { get; set; }

    /// <summary>
    /// 覆盖当前运行架构（对应 Go <c>CheckRequest.Arch</c> / <c>runtime.GOARCH</c>）。
    /// 为空时按运行时进程架构推断（amd64 / arm64 / 386 / arm / wasm）。
    /// </summary>
    public string? Arch { get; set; }

    /// <summary>
    /// 构造 Endpoint 更新提供者。
    /// </summary>
    /// <param name="httpClient">HTTP 客户端实例，用于 http/https 清单请求。</param>
    /// <param name="manifestUrl">
    /// 清单 URL（对应 Go <c>endpoint.Config.URL</c>），必填。可嵌入
    /// <c>{{platform}}</c>/<c>{{arch}}</c>/<c>{{version}}</c>/<c>{{channel}}</c> 占位符；
    /// 未被占位符消费的参数会作为查询参数追加。
    /// </param>
    /// <param name="channel">
    /// 请求的发布通道（对应 Go <c>endpoint.Config.Channel</c>），可选。作为 channel 查询参数
    /// / 占位符发送，并在客户端额外校验：清单声明了不同的非空 channel 时视为"无更新"。
    /// </param>
    /// <param name="headers">
    /// 附加到清单请求的 HTTP 头（对应 Go <c>endpoint.Config.Headers</c>），可选。
    /// </param>
    /// <param name="assetNamePattern">
    /// 资产名称匹配模式（不区分大小写的子串匹配）。不为空时按用户模式优先选择资产；
    /// 为空时按平台/架构选择（对应 Go <c>pickArtifact</c>）。
    /// </param>
    /// <param name="customSchemeLoader">
    /// 可选的清单流加载器，用于处理 http/https/file 之外的 scheme（例如 <c>wails://</c>）。
    /// 参数为完整请求 URL 与取消令牌；返回 null 表示"无更新"。
    /// </param>
    /// <exception cref="ArgumentNullException">httpClient 为 null。</exception>
    /// <exception cref="ArgumentException">manifestUrl 为空。</exception>
    public EndpointUpdateProvider(
        HttpClient httpClient,
        string manifestUrl,
        string channel = "",
        HTTPHeader[]? headers = null,
        string assetNamePattern = "",
        Func<string, CancellationToken, Task<Stream?>>? customSchemeLoader = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            throw new ArgumentException("清单 URL 不能为空。", nameof(manifestUrl));
        }

        _urlTemplate = manifestUrl;
        _channel = channel ?? string.Empty;
        _headers = headers ?? [];
        _assetNamePattern = assetNamePattern ?? string.Empty;
        _customSchemeLoader = customSchemeLoader;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 对应 Go <c>endpoint.Provider.Check</c>：获取清单、按 channel 过滤、按平台/架构挑选资产、
    /// 映射校验信息并解析资产 URL。清单请求返回 HTTP 204/404 或自定义加载器返回 null 时返回
    /// null（无更新）；清单格式错误、schemaVersion 过高、缺少 version、无匹配资产等返回明确异常。
    /// </remarks>
    public async Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var requestUrl = BuildUrl();

        var bytes = await FetchManifestBytesAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            // 对应 Go：HTTP 204（动态服务器按版本比较后返回"无更新"）与 404（静态主机尚未发布清单）→ nil
            return null;
        }

        var manifest = ParseManifest(bytes);

        // 对应 Go：清单声明了与配置不同（且非空）的 channel 时视为"无更新"
        if (_channel.Length > 0
            && !string.IsNullOrEmpty(manifest.Channel)
            && !string.Equals(_channel, manifest.Channel, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var platform = ResolvePlatform();
        var arch = ResolveArch();

        var artifact = PickArtifact(manifest.Artifacts, platform, arch);
        if (artifact is null)
        {
            throw new InvalidOperationException(
                $"endpoint: 清单 {manifest.Version} 中没有匹配 {platform}/{arch} 的资产。");
        }

        if (string.IsNullOrEmpty(artifact.Url))
        {
            throw new InvalidOperationException(
                $"endpoint: 清单 {manifest.Version} 中 {platform}/{arch} 的资产缺少 url。");
        }

        var resolvedUrl = ResolveArtifactUrl(requestUrl, artifact.Url);
        var (checksum, signature) = MapVerification(artifact, manifest.Version, platform, arch);

        return new UpdateManifest
        {
            Version = TrimVersionPrefix(manifest.Version),
            ReleaseNotes = manifest.Notes,
            DownloadURL = resolvedUrl,
            Checksum = checksum,
            Signature = signature,
            ContentLength = artifact.Size > 0 ? artifact.Size : null,
            PublishedDate = ParsePublishedAt(manifest.PublishedAt)
        };
    }

    /// <summary>
    /// 构建本次检查使用的清单 URL：替换占位符，并把未被占位符消费的参数作为查询参数追加。
    /// 对应 Go <c>endpoint.Provider.buildURL</c>。
    /// </summary>
    /// <returns>完整的清单请求 URL。</returns>
    /// <exception cref="InvalidOperationException">清单 URL 无效。</exception>
    private string BuildUrl()
    {
        var raw = _urlTemplate;
        var pending = new List<KeyValuePair<string, string>>();

        var parameters = new (string Name, string Value, bool Send)[]
        {
            ("platform", ResolvePlatform(), true),
            ("arch", ResolveArch(), true),
            ("version", CurrentVersion ?? string.Empty, CurrentVersion is not null),
            ("channel", _channel, _channel.Length > 0)
        };

        foreach (var (name, value, send) in parameters)
        {
            var placeholder = "{{" + name + "}}";
            if (raw.Contains(placeholder, StringComparison.Ordinal))
            {
                // 对应 Go：占位符会被替换（即使未配置也会替换为空串），不再作为查询参数发送
                raw = raw.Replace(placeholder, Uri.EscapeDataString(value), StringComparison.Ordinal);
                continue;
            }

            if (send)
            {
                pending.Add(new KeyValuePair<string, string>(name, value));
            }
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"endpoint: 清单 URL 无效：{raw}");
        }

        if (pending.Count == 0)
        {
            return raw;
        }

        // 保留模板中已有的查询参数，再追加未被占位符消费的参数
        var appended = pending.Select(
            pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
        var separator = raw.Contains('?') ? "&" : "?";
        return $"{raw}{separator}{string.Join("&", appended)}";
    }

    /// <summary>
    /// 按请求 URL 的 scheme 获取清单字节流内容。
    /// 对应 Go <c>endpoint.Provider.Check</c> 中的请求与读取逻辑；http/https 走 HTTP，
    /// file 走本地文件系统，其他 scheme 交由 <see cref="_customSchemeLoader"/>。
    /// </summary>
    /// <param name="requestUrl">完整的清单请求 URL。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>清单字节数组；HTTP 204/404 或自定义加载器返回 null 时返回 null。</returns>
    private async Task<byte[]?> FetchManifestBytesAsync(string requestUrl, CancellationToken cancellationToken)
    {
        var scheme = Uri.TryCreate(requestUrl, UriKind.Absolute, out var uri) ? uri.Scheme : string.Empty;

        if (scheme is "http" or "https")
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            foreach (var header in _headers)
            {
                if (!string.IsNullOrEmpty(header.Key))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"endpoint: 清单请求失败：HTTP {(int)response.StatusCode}");
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await ReadLimitedAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        if (scheme == "file")
        {
            await using var stream = File.OpenRead(uri!.LocalPath);
            return await ReadLimitedAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        if (_customSchemeLoader is not null)
        {
            await using var stream = await _customSchemeLoader(requestUrl, cancellationToken).ConfigureAwait(false);
            if (stream is null)
            {
                return null;
            }

            return await ReadLimitedAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        throw new NotSupportedException(
            $"endpoint: 不支持的清单协议 \"{scheme}\"（支持 http/https/file，或通过 customSchemeLoader 注入自定义协议）。");
    }

    /// <summary>
    /// 读取流内容，超过 <see cref="MaxManifestBytes"/> 上限时抛出异常。
    /// </summary>
    /// <param name="stream">待读取的流。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>读取到的字节数组。</returns>
    private static async Task<byte[]> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxManifestBytes)
            {
                throw new InvalidDataException("endpoint: 清单响应超过 8 MiB 上限。");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// 解析清单 JSON，校验 schemaVersion 上限与必需字段。
    /// 对应 Go <c>endpoint.Provider.Check</c> 中的 schema 校验与 <c>manifest</c> 结构。
    /// </summary>
    /// <param name="bytes">清单字节内容。</param>
    /// <returns>解析后的清单。</returns>
    /// <exception cref="InvalidOperationException">JSON 非法、schemaVersion 过高或缺少 version 字段。</exception>
    private static EndpointManifest ParseManifest(byte[] bytes)
    {
        // 容忍 UTF-8 BOM：部分编辑器/工具写出的清单带 BOM，而 System.Text.Json 不接受前导 BOM。
        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, offset, bytes.Length - offset));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"endpoint: 清单 JSON 解析失败：{ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("endpoint: 清单必须是 JSON 对象。");
            }

            var manifest = new EndpointManifest
            {
                SchemaVersion = GetInt32(root, "schemaVersion"),
                Version = GetString(root, "version") ?? string.Empty,
                Channel = GetString(root, "channel"),
                Notes = GetString(root, "notes"),
                PublishedAt = GetString(root, "publishedAt")
            };

            if (manifest.SchemaVersion > SchemaVersionMax)
            {
                throw new InvalidOperationException(
                    $"endpoint: 清单 schemaVersion {manifest.SchemaVersion} 高于受支持的版本 {SchemaVersionMax}。");
            }

            if (string.IsNullOrEmpty(manifest.Version))
            {
                throw new InvalidOperationException("endpoint: 清单缺少 version 字段。");
            }

            if (root.TryGetProperty("artifacts", out var artifacts) && artifacts.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in artifacts.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    manifest.Artifacts.Add(new EndpointArtifact
                    {
                        Url = GetString(element, "url") ?? string.Empty,
                        Filename = GetString(element, "filename"),
                        Size = GetInt64(element, "size"),
                        Platform = GetString(element, "platform"),
                        Arch = GetString(element, "arch"),
                        DigestAlgo = GetString(element, "digestAlgo"),
                        Digest = GetString(element, "digest"),
                        SignatureAlgo = GetString(element, "signatureAlgo"),
                        Signature = GetString(element, "signature")
                    });
                }
            }

            return manifest;
        }
    }

    /// <summary>
    /// 挑选与平台/架构匹配的资产。对应 Go <c>endpoint.pickArtifact</c>。
    /// 用户显式指定 <see cref="_assetNamePattern"/> 时按用户模式优先（与 GitHub/GitLab 一致）；
    /// 否则返回文档顺序中首个匹配项，平台/架构为空的资产视为通配（universal）。
    /// </summary>
    /// <param name="artifacts">清单中的资产列表。</param>
    /// <param name="platform">目标平台。</param>
    /// <param name="arch">目标架构。</param>
    /// <returns>匹配的资产；无匹配返回 null。</returns>
    private EndpointArtifact? PickArtifact(List<EndpointArtifact> artifacts, string platform, string arch)
    {
        if (artifacts.Count == 0)
        {
            return null;
        }

        if (_assetNamePattern.Length > 0)
        {
            foreach (var artifact in artifacts)
            {
                var name = string.IsNullOrEmpty(artifact.Filename)
                    ? FilenameFromUrl(artifact.Url)
                    : artifact.Filename;
                if (name.Contains(_assetNamePattern, StringComparison.OrdinalIgnoreCase))
                {
                    return artifact;
                }
            }

            return null;
        }

        foreach (var artifact in artifacts)
        {
            if (!PlatformMatches(artifact.Platform, platform))
            {
                continue;
            }

            if (!ArchMatches(artifact.Arch, arch))
            {
                continue;
            }

            return artifact;
        }

        return null;
    }

    /// <summary>
    /// 判断清单资产声明的平台是否对应目标平台。
    /// 对应 Go <c>endpoint.platformMatches</c>：清单应发布 GOOS 值，但接受常见别名（macos/mac/osx、win/win32/win64），
    /// 空值表示通配。大小写不敏感。
    /// </summary>
    private static bool PlatformMatches(string? published, string want)
    {
        if (string.IsNullOrEmpty(published))
        {
            return true;
        }

        var p = published.ToLowerInvariant();
        want = want.ToLowerInvariant();
        if (p == want)
        {
            return true;
        }

        return want switch
        {
            "darwin" => p is "macos" or "mac" or "osx",
            "windows" => p is "win" or "win32" or "win64",
            _ => false
        };
    }

    /// <summary>
    /// 判断清单资产声明的架构是否对应目标架构。
    /// 对应 Go <c>endpoint.archMatches</c>：清单应发布 GOARCH 值，但接受常见别名（x86_64/x64、aarch64、i386/x86/ia32），
    /// 空值表示通配。大小写不敏感。
    /// </summary>
    private static bool ArchMatches(string? published, string want)
    {
        if (string.IsNullOrEmpty(published))
        {
            return true;
        }

        var p = published.ToLowerInvariant();
        want = want.ToLowerInvariant();
        if (p == want)
        {
            return true;
        }

        return want switch
        {
            "amd64" => p is "x86_64" or "x64",
            "arm64" => p == "aarch64",
            "386" => p is "i386" or "x86" or "ia32",
            _ => false
        };
    }

    /// <summary>
    /// 将资产 URL 解析为绝对 URL（允许相对路径）。对应 Go <c>endpoint.resolveURL</c>：
    /// 解析后的 scheme 必须是 http/https，否则明确报错。
    /// </summary>
    /// <param name="manifestUrl">清单请求 URL（作为相对解析基准）。</param>
    /// <param name="artifactUrl">资产 URL（可为相对路径）。</param>
    /// <returns>解析后的绝对资产 URL。</returns>
    /// <exception cref="InvalidOperationException">URL 无效。</exception>
    /// <exception cref="NotSupportedException">解析出的 scheme 不是 http/https。</exception>
    private static string ResolveArtifactUrl(string manifestUrl, string artifactUrl)
    {
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"endpoint: 清单 URL 无效：{manifestUrl}");
        }

        if (!Uri.TryCreate(baseUri, artifactUrl, out var resolved))
        {
            throw new InvalidOperationException($"endpoint: 资产 URL 无效：{artifactUrl}");
        }

        if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps)
        {
            throw new NotSupportedException($"endpoint: 不支持的资产 URL 协议 \"{resolved.Scheme}\"。");
        }

        return resolved.ToString();
    }

    /// <summary>
    /// 将资产的 digest/signature 字段映射为框架校验信息。
    /// 对应 Go <c>endpoint.buildVerification</c>：digest/signature 均为 base64，raw 与标准编码均可；
    /// 非法值（无法解码、有签名却缺 algo）视为错误而非静默忽略。
    /// <para>
    /// Wails.Net 差异：下载器仅支持 SHA256 校验和、minisign 签名，故仅在
    /// <c>digestAlgo=sha256</c> 时映射为十六进制 <see cref="UpdateManifest.Checksum"/>，
    /// 仅在 <c>signatureAlgo=minisign</c> 时映射 <see cref="UpdateManifest.Signature"/>；其余算法予以校验但不映射。
    /// </para>
    /// </summary>
    private static (string? Checksum, string? Signature) MapVerification(
        EndpointArtifact artifact, string version, string platform, string arch)
    {
        string? checksum = null;
        string? signature = null;

        if (!string.IsNullOrEmpty(artifact.Digest))
        {
            var digest = DecodeBase64(artifact.Digest);
            if (digest is null)
            {
                throw new InvalidDataException(
                    $"endpoint: 清单 {version} 中 {platform}/{arch} 资产的 digest 不是有效的 base64。");
            }

            if (string.Equals(artifact.DigestAlgo, "sha256", StringComparison.OrdinalIgnoreCase))
            {
                checksum = Convert.ToHexStringLower(digest);
            }
        }

        if (!string.IsNullOrEmpty(artifact.Signature))
        {
            if (string.IsNullOrEmpty(artifact.SignatureAlgo))
            {
                throw new InvalidDataException(
                    $"endpoint: 清单 {version} 中 {platform}/{arch} 资产带有 signature 但缺少 signatureAlgo。");
            }

            if (DecodeBase64(artifact.Signature) is null)
            {
                throw new InvalidDataException(
                    $"endpoint: 清单 {version} 中 {platform}/{arch} 资产的 signature 不是有效的 base64。");
            }

            if (string.Equals(artifact.SignatureAlgo, "minisign", StringComparison.OrdinalIgnoreCase))
            {
                signature = artifact.Signature;
            }
        }

        return (checksum, signature);
    }

    /// <summary>
    /// 解析清单的 <c>publishedAt</c>（RFC 3339），容忍缺失与非法值（均返回 null）。
    /// 对应 Go <c>endpoint.manifest.published</c>。
    /// </summary>
    private static DateTime? ParsePublishedAt(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    /// <summary>
    /// 去除版本号前缀 'v' / 'V'。对应 Go <c>semver.TrimPrefix</c>（Wails.Net 与 GitHub/GitLab 提供者保持一致）。
    /// </summary>
    private static string TrimVersionPrefix(string version) => version.TrimStart('v', 'V');

    /// <summary>
    /// 按运行时操作系统推断 GOOS 风格平台名（与 Go <c>runtime.GOOS</c> 对齐）。
    /// 未知平台返回空串（此时仅通配资产可被选中）。
    /// </summary>
    private static string RuntimePlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "darwin";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        if (OperatingSystem.IsAndroid())
        {
            return "android";
        }

        if (OperatingSystem.IsFreeBSD())
        {
            return "freebsd";
        }

        if (OperatingSystem.IsIOS())
        {
            return "ios";
        }

        return string.Empty;
    }

    /// <summary>
    /// 按运行时进程架构推断 GOARCH 风格架构名（与 Go <c>runtime.GOARCH</c> 对齐）。
    /// 未知架构返回空串（此时仅通配资产可被选中）。
    /// </summary>
    private static string RuntimeArch() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "amd64",
        Architecture.X86 => "386",
        Architecture.Arm64 => "arm64",
        Architecture.Arm => "arm",
        Architecture.Wasm => "wasm",
        _ => string.Empty
    };

    /// <summary>
    /// 返回当前生效的平台（优先 <see cref="Platform"/>，否则运行时推断）。
    /// </summary>
    private string ResolvePlatform() => string.IsNullOrEmpty(Platform) ? RuntimePlatform() : Platform;

    /// <summary>
    /// 返回当前生效的架构（优先 <see cref="Arch"/>，否则运行时推断）。
    /// </summary>
    private string ResolveArch() => string.IsNullOrEmpty(Arch) ? RuntimeArch() : Arch;

    /// <summary>
    /// 从 URL 中提取文件名（去除查询串）。对应 Go <c>endpoint.filenameFromURL</c>。
    /// </summary>
    private static string FilenameFromUrl(string url)
    {
        var slash = url.LastIndexOf('/');
        var name = slash >= 0 && slash + 1 < url.Length ? url[(slash + 1)..] : url;
        var query = name.IndexOf('?');
        return query >= 0 ? name[..query] : name;
    }

    /// <summary>
    /// 解码 base64（raw 无填充与标准编码均可）。对应 Go <c>endpoint.decodeB64</c>。
    /// </summary>
    /// <param name="value">base64 字符串。</param>
    /// <returns>解码后的字节；非法输入返回 null。</returns>
    private static byte[]? DecodeBase64(string value)
    {
        var trimmed = value.Trim();
        foreach (var candidate in new[] { trimmed, PadBase64(trimmed) })
        {
            try
            {
                return Convert.FromBase64String(candidate);
            }
            catch (FormatException)
            {
                // 尝试下一种编码形式
            }
        }

        return null;
    }

    /// <summary>
    /// 为无填充的 base64 字符串补齐 '=' 填充（标准 base64 长度为 4 的倍数）。
    /// </summary>
    private static string PadBase64(string value) => (value.Length % 4) switch
    {
        2 => value + "==",
        3 => value + "=",
        _ => value
    };

    /// <summary>
    /// 读取 JSON 对象的字符串属性，非字符串或缺失时返回 null。
    /// </summary>
    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>
    /// 读取 JSON 对象的 32 位整数属性，缺失或非数字时返回 0。
    /// </summary>
    private static int GetInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out var value)
            ? value
            : 0;

    /// <summary>
    /// 读取 JSON 对象的 64 位整数属性，缺失或非数字时返回 0。
    /// </summary>
    private static long GetInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt64(out var value)
            ? value
            : 0;

    /// <summary>
    /// 内部清单模型（对应 Go endpoint.manifest 结构），仅承载已映射到
    /// <see cref="UpdateManifest"/> 的字段：schemaVersion/version/channel/notes/publishedAt/artifacts。
    /// </summary>
    private sealed class EndpointManifest
    {
        /// <summary>清单 schema 版本。</summary>
        public int SchemaVersion { get; set; }

        /// <summary>清单声明的版本号。</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>清单声明的发布通道。</summary>
        public string? Channel { get; set; }

        /// <summary>发行说明。</summary>
        public string? Notes { get; set; }

        /// <summary>发布时间（RFC 3339 字符串）。</summary>
        public string? PublishedAt { get; set; }

        /// <summary>资产列表。</summary>
        public List<EndpointArtifact> Artifacts { get; } = [];
    }

    /// <summary>
    /// 内部资产模型（对应 Go endpoint.manifestArtifact 结构）。
    /// </summary>
    private sealed class EndpointArtifact
    {
        /// <summary>资产下载地址（可为相对路径）。</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>资产文件名（可选，用于名称匹配）。</summary>
        public string? Filename { get; set; }

        /// <summary>资产大小（字节）。</summary>
        public long Size { get; set; }

        /// <summary>资产适用平台（GOOS 或别名）。</summary>
        public string? Platform { get; set; }

        /// <summary>资产适用架构（GOARCH 或别名）。</summary>
        public string? Arch { get; set; }

        /// <summary>摘要算法。</summary>
        public string? DigestAlgo { get; set; }

        /// <summary>摘要值（base64）。</summary>
        public string? Digest { get; set; }

        /// <summary>签名算法。</summary>
        public string? SignatureAlgo { get; set; }

        /// <summary>签名值（base64）。</summary>
        public string? Signature { get; set; }
    }
}
