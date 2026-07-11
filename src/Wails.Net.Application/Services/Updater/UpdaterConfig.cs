using System.Text.Json.Serialization;

namespace Wails.Net.Application.Services.Updater;

/// <summary>
/// HTTP 请求头，用于更新检查和下载请求。
/// 对应 Wails v3 Go 版本 config.go 中 Config.Headers 的条目。
/// </summary>
public sealed class HTTPHeader
{
    /// <summary>
    /// 获取或设置请求头名称。
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置请求头值。
    /// </summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// 更新服务配置，定义检查更新、下载和安装的行为。
/// 对应 Wails v3 Go 版本 config.go 中的 Config 结构。
/// </summary>
public sealed class UpdaterConfig
{
    /// <summary>
    /// 获取或设置检查更新的 URL。
    /// 服务将向此 URL 发送 GET 请求以获取更新清单。
    /// </summary>
    public string UpdateURL { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置当前应用版本号。
    /// 为 null 时将在服务启动时从 ApplicationOptions.Version 填充。
    /// </summary>
    public string? CurrentVersion { get; set; }

    /// <summary>
    /// 获取或设置附加到更新检查和下载请求的 HTTP 头列表。
    /// </summary>
    public HTTPHeader[] Headers { get; set; } = [];

    /// <summary>
    /// 获取或设置一个值，指示发现更新后是否自动下载。
    /// </summary>
    public bool AutoDownload { get; set; }

    /// <summary>
    /// 获取或设置一个值，指示下载完成后是否自动安装。
    /// </summary>
    public bool AutoInstall { get; set; }

    /// <summary>
    /// 获取或设置一个值，指示是否禁用下载校验和验证。
    /// </summary>
    public bool DisableChecksumVerification { get; set; }

    /// <summary>
    /// 获取或设置是否启用签名验证（默认 false）。
    /// 启用后将在安装前验证更新包的代码签名。
    /// </summary>
    public bool VerifySignature { get; set; } = false;

    /// <summary>
    /// 获取或设置期望的签名者名称（可选）。
    /// 启用签名验证后，将校验实际签名者是否与此值匹配。
    /// Windows 上与 Authenticode 证书 Subject 字段匹配；Linux 上与 GPG GOODSIG 行匹配。
    /// </summary>
    public string? ExpectedSigner { get; set; }

    /// <summary>
    /// 获取或设置下载目录，默认为系统临时目录。
    /// </summary>
    public string DownloadDirectory { get; set; } = Path.GetTempPath();
}

/// <summary>
/// 更新清单，从 UpdateURL 获取的远程版本信息。
/// 对应 Wails v3 Go 版本 updater 包中 Release 结构的 JSON 表示。
/// </summary>
public sealed class UpdateManifest
{
    /// <summary>
    /// 获取或设置新版本号。
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置发行说明文本，可为 null。
    /// </summary>
    [JsonPropertyName("releaseNotes")]
    public string? ReleaseNotes { get; set; }

    /// <summary>
    /// 获取或设置更新包下载 URL。
    /// </summary>
    [JsonPropertyName("downloadUrl")]
    public string DownloadURL { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 SHA256 校验和（十六进制字符串），可为 null 表示不验证。
    /// </summary>
    [JsonPropertyName("checksum")]
    public string? Checksum { get; set; }

    /// <summary>
    /// 获取或设置更新包签名，可为 null。
    /// </summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>
    /// 获取或设置更新包内容长度（字节），可为 null 表示未知。
    /// </summary>
    [JsonPropertyName("contentLength")]
    public long? ContentLength { get; set; }

    /// <summary>
    /// 获取或设置发布日期，可为 null。
    /// </summary>
    [JsonPropertyName("publishedDate")]
    public DateTime? PublishedDate { get; set; }
}
