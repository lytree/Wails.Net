namespace Wails.Net.Application.Services.Updater;

/// <summary>
/// 更新服务事件常量，对应 Wails v3 Go 版本 events.go 中的事件名。
/// 通过 EventProcessor.Emit 发布，前端可通过 wails.Events.On 订阅。
/// </summary>
public static class UpdateEvents
{
    /// <summary>
    /// 发现可用更新。负载数据：UpdateManifest。
    /// </summary>
    public const string UpdaterEventUpdateAvailable = "wails:updater:update-available";

    /// <summary>
    /// 无可用更新。负载数据：null。
    /// </summary>
    public const string UpdaterEventNoUpdateAvailable = "wails:updater:no-update";

    /// <summary>
    /// 下载开始。负载数据：UpdateManifest。
    /// </summary>
    public const string UpdaterEventDownloadStarted = "wails:updater:download-started";

    /// <summary>
    /// 下载进度更新。负载数据：UpdateProgressEventArgs。
    /// </summary>
    public const string UpdaterEventDownloadProgress = "wails:updater:download-progress";

    /// <summary>
    /// 下载完成。负载数据：UpdateManifest。
    /// </summary>
    public const string UpdaterEventDownloadComplete = "wails:updater:download-complete";

    /// <summary>
    /// 下载错误。负载数据：错误消息字符串。
    /// </summary>
    public const string UpdaterEventDownloadError = "wails:updater:download-error";

    /// <summary>
    /// 安装开始。负载数据：归档文件路径字符串。
    /// </summary>
    public const string UpdaterEventInstallStarted = "wails:updater:install-started";

    /// <summary>
    /// 安装完成。负载数据：null。
    /// </summary>
    public const string UpdaterEventInstallComplete = "wails:updater:install-complete";

    /// <summary>
    /// 安装错误。负载数据：错误消息字符串。
    /// </summary>
    public const string UpdaterEventInstallError = "wails:updater:install-error";

    /// <summary>
    /// 更新已应用，等待重启。负载数据：null。
    /// </summary>
    public const string UpdaterEventUpdateApplied = "wails:updater:update-applied";
}

/// <summary>
/// 下载进度事件参数，对应 Wails v3 Go 版本 events.go 中 Progress 结构。
/// </summary>
public sealed class UpdateProgressEventArgs
{
    /// <summary>
    /// 获取下载进度百分比（0-100）。
    /// </summary>
    public double ProgressPercentage { get; init; }

    /// <summary>
    /// 获取已下载字节数。
    /// </summary>
    public long BytesDownloaded { get; init; }

    /// <summary>
    /// 获取总字节数，-1 表示未知。
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// 获取下载速率（字节/秒）。
    /// </summary>
    public long BytesPerSecond { get; init; }
}
