using System.Text.Json.Serialization;

namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 资源分类常量，取值与 res-downloader 的 <c>Classify</c> 字段一致。
/// </summary>
public static class ResourceClassify
{
    /// <summary>全部类型（筛选用）。</summary>
    public const string All = "all";

    /// <summary>图片。</summary>
    public const string Image = "image";

    /// <summary>音频。</summary>
    public const string Audio = "audio";

    /// <summary>视频。</summary>
    public const string Video = "video";

    /// <summary>HLS 播放列表。</summary>
    public const string M3u8 = "m3u8";

    /// <summary>直播流（flv / mpd）。</summary>
    public const string Live = "live";

    /// <summary>未知二进制流。</summary>
    public const string Stream = "stream";

    /// <summary>PDF。</summary>
    public const string Pdf = "pdf";

    /// <summary>演示文稿。</summary>
    public const string Ppt = "ppt";

    /// <summary>表格。</summary>
    public const string Xls = "xls";

    /// <summary>文档。</summary>
    public const string Doc = "doc";

    /// <summary>字体。</summary>
    public const string Font = "font";
}

/// <summary>
/// 资源状态常量（与 res-downloader 的 <c>Status</c> 字段一致）。
/// </summary>
public static class ResourceStatus
{
    /// <summary>就绪，可下载。</summary>
    public const string Ready = "ready";

    /// <summary>已加入队列，等待并发槽位。</summary>
    public const string Pending = "pending";

    /// <summary>下载中。</summary>
    public const string Running = "running";

    /// <summary>下载失败。</summary>
    public const string Error = "error";

    /// <summary>下载完成。</summary>
    public const string Done = "done";

    /// <summary>需要后续处理（如视频解密）。</summary>
    public const string Handle = "handle";
}

/// <summary>
/// MIME 映射项：决定 <c>Content-Type</c> 对应的资源类型与保存后缀。
/// </summary>
public sealed class MimeEntry
{
    /// <summary>资源类型（见 <see cref="ResourceClassify"/>）。</summary>
    [JsonPropertyName("Type")]
    public string Type { get; set; } = ResourceClassify.Stream;

    /// <summary>保存后缀（含点，如 <c>.mp4</c>；<c>default</c> 表示按 URL 扩展名推断）。</summary>
    [JsonPropertyName("Suffix")]
    public string Suffix { get; set; } = "default";

    /// <summary>复制一份，供设置快照使用。</summary>
    /// <returns>映射副本。</returns>
    public MimeEntry Clone() => new() { Type = Type, Suffix = Suffix };
}

/// <summary>
/// 一条被嗅探到的网络资源（字段命名与 res-downloader 的 MediaInfo 对齐）。
/// </summary>
public sealed class ResourceItem
{
    /// <summary>资源唯一标识。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>完整直链（含查询串）。</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>URL 摘要（MD5），用于去重与删除。</summary>
    public string UrlSign { get; set; } = string.Empty;

    /// <summary>封面地址；视频号等平台会返回。</summary>
    public string CoverUrl { get; set; } = string.Empty;

    /// <summary>资源大小（字节）；未知时为 0。</summary>
    public long Size { get; set; }

    /// <summary>一级域名（展示用）。</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>资源类型（见 <see cref="ResourceClassify"/>）。</summary>
    public string Classify { get; set; } = ResourceClassify.Stream;

    /// <summary>保存后缀（含点）。</summary>
    public string Suffix { get; set; } = ".bin";

    /// <summary>下载状态（见 <see cref="ResourceStatus"/>）。</summary>
    public string Status { get; set; } = ResourceStatus.Ready;

    /// <summary>本地保存路径；未下载时为空。</summary>
    public string SavePath { get; set; } = string.Empty;

    /// <summary>视频号等加密媒体的解密密钥（Base64）；为空表示未加密。</summary>
    public string DecodeKey { get; set; } = string.Empty;

    /// <summary>平台附加数据：<c>headers</c>（原始请求头 JSON）、<c>wx_file_formats</c> 等。</summary>
    public Dictionary<string, string> OtherData { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>描述（可编辑，下载时优先作为文件名）。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>响应 Content-Type。</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>展示标题（描述 / Content-Disposition / URL 文件名）。</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>嗅探时间（本地时间字符串）。</summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>下载进度百分比（0-100），仅运行中有效。</summary>
    public double Progress { get; set; }

    /// <summary>状态说明（运行中显示百分比 / 速度，失败显示错误信息）。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>复制一份资源，避免事件推送时被后台线程并发修改。</summary>
    /// <returns>资源副本。</returns>
    public ResourceItem Clone()
    {
        var copy = (ResourceItem)MemberwiseClone();
        copy.OtherData = new Dictionary<string, string>(OtherData, StringComparer.OrdinalIgnoreCase);
        return copy;
    }
}

/// <summary>
/// 下载任务状态推送（与资源状态同步，前端可在状态列渲染进度）。
/// </summary>
public sealed class DownloadTaskInfo
{
    /// <summary>资源标识（与 <see cref="ResourceItem.Id"/> 相同）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>资源直链。</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>状态（见 <see cref="ResourceStatus"/>）。</summary>
    public string Status { get; set; } = ResourceStatus.Ready;

    /// <summary>本地保存路径。</summary>
    public string SavePath { get; set; } = string.Empty;

    /// <summary>进度（0-100）。</summary>
    public double Progress { get; set; }

    /// <summary>状态说明。</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 代理运行状态（前端顶栏与设置页展示）。
/// </summary>
public sealed class ProxyState
{
    /// <summary>代理是否正在监听。</summary>
    public bool Running { get; set; }

    /// <summary>监听地址（Host:Port）。</summary>
    public string Address { get; set; } = "127.0.0.1:8899";

    /// <summary>系统代理是否已指向本机代理。</summary>
    public bool SystemProxyEnabled { get; set; }

    /// <summary>根证书是否已安装到当前用户受信任根。</summary>
    public bool CertificateInstalled { get; set; }

    /// <summary>根证书导出路径（.cer）。</summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>根证书在线下载地址（走本地 API）。</summary>
    public string CertificateUrl { get; set; } = string.Empty;

    /// <summary>上游代理是否生效。</summary>
    public bool UpstreamProxyEnabled { get; set; }

    /// <summary>上游代理地址。</summary>
    public string UpstreamProxy { get; set; } = string.Empty;

    /// <summary>状态说明。</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 应用信息（关于页与排障用）。
/// </summary>
public sealed class AppInfo
{
    /// <summary>应用名称。</summary>
    public string Name { get; set; } = "Wails.Net 资源嗅探下载器";

    /// <summary>版本号。</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>运行平台。</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>应用数据目录。</summary>
    public string DataDirectory { get; set; } = string.Empty;

    /// <summary>设置文件路径。</summary>
    public string SettingsPath { get; set; } = string.Empty;

    /// <summary>默认下载目录。</summary>
    public string DownloadDirectory { get; set; } = string.Empty;
}

/// <summary>
/// 应用设置（持久化为 settings.json，字段与 res-downloader 的 Config 对齐）。
/// </summary>
public sealed class AppSettings
{
    /// <summary>代理监听地址，默认 127.0.0.1。</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>代理监听端口，默认 8899。</summary>
    public int Port { get; set; } = 8899;

    /// <summary>主题：light / dark / auto。</summary>
    public string Theme { get; set; } = "light";

    /// <summary>界面语言：zh / en。</summary>
    public string Locale { get; set; } = "zh";

    /// <summary>视频号清晰度：0 默认、1 超清、2 高画质、3 中画质、4 低画质。</summary>
    public int Quality { get; set; }

    /// <summary>下载保存目录。</summary>
    public string SaveDirectory { get; set; } = string.Empty;

    /// <summary>文件名长度限制（0 表示默认 10 个字符，仅在描述非空时生效）。</summary>
    public int FilenameLen { get; set; }

    /// <summary>文件名是否追加时间标识。</summary>
    public bool FilenameTime { get; set; } = true;

    /// <summary>上游代理地址，例如 <c>http://127.0.0.1:7890</c>。</summary>
    public string UpstreamProxy { get; set; } = string.Empty;

    /// <summary>是否启用上游代理（代理转发走上游）。</summary>
    public bool OpenProxy { get; set; }

    /// <summary>下载时是否使用上游代理。</summary>
    public bool DownloadProxy { get; set; }

    /// <summary>启动应用后自动开启抓取（含系统代理）。</summary>
    public bool AutoProxy { get; set; }

    /// <summary>视频号是否全量拦截（关闭时只拦截视频详情）。</summary>
    public bool WxAction { get; set; } = true;

    /// <summary>单个下载任务的连接数（分片并发）。</summary>
    public int TaskNumber { get; set; } = Math.Max(2, Environment.ProcessorCount * 2);

    /// <summary>同时进行的下载任务数。</summary>
    public int DownNumber { get; set; } = 3;

    /// <summary>下载时使用的 User-Agent（覆盖嗅探到的 UA）。</summary>
    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/129.0.0.0 Safari/537.36";

    /// <summary>下载时重放的请求头：<c>default</c> 表示系统默认集合，或逗号分隔的自定义列表。</summary>
    public string UseHeaders { get; set; } = "default";

    /// <summary>新资源是否追加到列表尾部（false 表示插入头部）。</summary>
    public bool InsertTail { get; set; } = true;

    /// <summary>MIME 拦截规则：Content-Type → 类型 + 后缀。</summary>
    public Dictionary<string, MimeEntry> MimeMap { get; set; } = MimeTypeMap.CreateDefault();

    /// <summary>域名规则：每行一条，支持 <c>*</c>、<c>*.domain</c>、精确域名与 <c>!</c> 排除。</summary>
    public string Rule { get; set; } = "*";

    /// <summary>复制一份设置（含 MimeMap 深拷贝）。</summary>
    /// <returns>设置副本。</returns>
    public AppSettings Clone() => new()
    {
        Host = Host,
        Port = Port,
        Theme = Theme,
        Locale = Locale,
        Quality = Quality,
        SaveDirectory = SaveDirectory,
        FilenameLen = FilenameLen,
        FilenameTime = FilenameTime,
        UpstreamProxy = UpstreamProxy,
        OpenProxy = OpenProxy,
        DownloadProxy = DownloadProxy,
        AutoProxy = AutoProxy,
        WxAction = WxAction,
        TaskNumber = TaskNumber,
        DownNumber = DownNumber,
        UserAgent = UserAgent,
        UseHeaders = UseHeaders,
        InsertTail = InsertTail,
        MimeMap = MimeMap.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase),
        Rule = Rule,
    };
}
