using System.Text.Json;
using Wails.Net.Application.Events;
using Wails.Net.Application.Options;
using Wails.Net.Application.Services.Updater;

namespace Wails.Net.Application.Services;

/// <summary>
/// 更新信息，包含版本检查结果。
/// </summary>
public sealed class UpdateInfo
{
    /// <summary>
    /// 获取最新版本号。
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// 获取更新包下载地址，可为 null。
    /// </summary>
    public string? DownloadUrl { get; init; }

    /// <summary>
    /// 获取是否有可用更新。
    /// </summary>
    public bool UpdateAvailable { get; init; }

    /// <summary>
    /// 获取更新说明，可为 null。
    /// </summary>
    public string? ReleaseNotes { get; init; }
}

/// <summary>
/// 应用更新服务，提供版本检查、下载、解压和安装的完整更新流程。
/// 对应 Wails v3 Go 版本 pkg/updater。
/// 通过 HTTP 请求检查更新，支持断点续传下载、SHA256 校验和、归档解压和 helper 进程替换。
/// </summary>
public class UpdaterService : IServiceStartup, IServiceShutdown
{
    /// <summary>
    /// HTTP 客户端实例。
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 事件处理器，用于分发更新相关事件到前端。
    /// </summary>
    private EventProcessor? _eventProcessor;

    /// <summary>
    /// 更新下载器实例，负责流式下载、断点续传和校验和验证。
    /// </summary>
    private UpdateDownloader? _downloader;

    /// <summary>
    /// 更新服务配置。
    /// </summary>
    private UpdaterConfig _config = new();

    /// <summary>
    /// 获取或设置当前应用版本号，用于判断是否有可用更新。
    /// </summary>
    public string CurrentVersion
    {
        get => _config.CurrentVersion ?? "1.0.0";
        set => _config.CurrentVersion = value;
    }

    /// <summary>
    /// 获取或设置更新包下载目录。
    /// 默认为系统临时目录。
    /// </summary>
    public string DownloadDirectory
    {
        get => _config.DownloadDirectory;
        set => _config.DownloadDirectory = value;
    }

    /// <summary>
    /// 获取或设置更新服务配置。
    /// 设置配置后会重新创建下载器实例。
    /// </summary>
    public UpdaterConfig Config
    {
        get => _config;
        set
        {
            _config = value ?? new UpdaterConfig();
            _downloader = new UpdateDownloader(
                _httpClient,
                _config.Headers,
                _config.DisableChecksumVerification);
        }
    }

    /// <summary>
    /// 使用默认 HttpClient 构造更新服务实例。
    /// </summary>
    public UpdaterService()
        : this(new HttpClient())
    {
    }

    /// <summary>
    /// 使用指定 HttpClient 构造更新服务实例。
    /// </summary>
    /// <param name="httpClient">HTTP 客户端实例。</param>
    public UpdaterService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 注入事件处理器，使更新服务能够向前端分发事件。
    /// </summary>
    /// <param name="eventProcessor">事件处理器实例。</param>
    public void SetEventProcessor(EventProcessor eventProcessor)
    {
        _eventProcessor = eventProcessor;
    }

    /// <summary>
    /// 服务启动，初始化更新服务。
    /// 检查是否以 helper 模式启动，如果是则执行 helper 流程并退出进程；
    /// 否则从应用选项填充当前版本号。
    /// </summary>
    /// <param name="options">应用选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动操作的异步任务。</returns>
    public Task ServiceStartup(ApplicationOptions options, CancellationToken cancellationToken)
    {
        // 检查是否以 helper 模式启动（更新安装完成后重启自身作为 helper 进程）。
        // HandleHelperMode 在非 helper 模式下立即返回；在 helper 模式下永不返回（调用 Environment.Exit）。
        HelperProcess.HandleHelperMode();

        if (_config.CurrentVersion is null || _config.CurrentVersion.Length == 0)
        {
            _config.CurrentVersion = options.Version;
        }

        _downloader = new UpdateDownloader(
            _httpClient,
            _config.Headers,
            _config.DisableChecksumVerification);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 服务关闭，释放 HTTP 客户端资源。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示关闭操作的异步任务。</returns>
    public Task ServiceShutdown(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 检查指定 URL 处是否有可用更新。
    /// 发送 HTTP GET 请求，解析 JSON 响应中的 version 字段与当前版本比较。
    /// 此方法为向后兼容方法，内部调用 CheckForUpdatesAsync。
    /// </summary>
    /// <param name="url">更新检查 URL。</param>
    /// <returns>包含版本信息和是否有可用更新的 UpdateInfo 实例。</returns>
    public async Task<UpdateInfo> CheckForUpdates(string url)
    {
        var savedUrl = _config.UpdateURL;
        _config.UpdateURL = url;
        try
        {
            var manifest = await CheckForUpdatesAsync();
            return new UpdateInfo
            {
                Version = manifest.Version,
                DownloadUrl = manifest.DownloadURL,
                UpdateAvailable = IsNewerVersion(manifest.Version, CurrentVersion),
                ReleaseNotes = manifest.ReleaseNotes
            };
        }
        finally
        {
            _config.UpdateURL = savedUrl;
        }
    }

    /// <summary>
    /// 从指定 URL 下载更新包到本地临时目录。
    /// 此方法为向后兼容方法，内部调用 UpdateDownloader.DownloadAsync。
    /// </summary>
    /// <param name="url">更新包下载 URL。</param>
    /// <returns>下载文件的本地路径。</returns>
    public async Task<string> DownloadUpdate(string url)
    {
        if (!Directory.Exists(DownloadDirectory))
        {
            Directory.CreateDirectory(DownloadDirectory);
        }

        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = $"update_{Guid.NewGuid():N}";
        }

        var filePath = Path.Combine(DownloadDirectory, fileName);

        var manifest = new UpdateManifest
        {
            DownloadURL = url
        };

        var downloader = _downloader ?? new UpdateDownloader(_httpClient, _config.Headers, _config.DisableChecksumVerification);
        await downloader.DownloadAsync(manifest, filePath, progress: null, CancellationToken.None);

        return filePath;
    }

    /// <summary>
    /// 安装指定路径的更新包。
    /// 解压归档文件并执行平台特定安装。
    /// </summary>
    /// <param name="path">更新包文件路径。</param>
    /// <returns>表示安装操作的异步任务。</returns>
    public async Task InstallUpdate(string path)
    {
        await InstallUpdateAsync(path);
    }

    /// <summary>
    /// 异步检查更新。向配置的 UpdateURL 发送 GET 请求，解析响应为 UpdateManifest。
    /// 如果发现新版本，发射 UpdaterEventUpdateAvailable 事件；
    /// 否则发射 UpdaterEventNoUpdateAvailable 事件。
    /// 如果配置了 AutoDownload，自动调用 DownloadUpdateAsync。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析得到的更新清单。</returns>
    /// <exception cref="InvalidOperationException">UpdateURL 未配置。</exception>
    public async Task<UpdateManifest> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.UpdateURL))
        {
            throw new InvalidOperationException("更新检查 URL 未配置，请设置 UpdaterConfig.UpdateURL。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _config.UpdateURL);
        foreach (var header in _config.Headers)
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
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json)
            ?? throw new InvalidDataException("更新清单 JSON 反序列化失败。");

        if (IsNewerVersion(manifest.Version, CurrentVersion))
        {
            EmitEvent(UpdateEvents.UpdaterEventUpdateAvailable, manifest);

            if (_config.AutoDownload && !string.IsNullOrWhiteSpace(manifest.DownloadURL))
            {
                await DownloadUpdateAsync(manifest, cancellationToken);
            }
        }
        else
        {
            EmitEvent(UpdateEvents.UpdaterEventNoUpdateAvailable, null);
        }

        return manifest;
    }

    /// <summary>
    /// 异步下载更新包。
    /// 发射 UpdaterEventDownloadStarted 事件；
    /// 下载过程中通过 UpdaterEventDownloadProgress 报告进度；
    /// 完成后发射 UpdaterEventDownloadComplete；错误时发射 UpdaterEventDownloadError。
    /// </summary>
    /// <param name="manifest">更新清单，包含下载 URL 和校验和信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>下载文件的本地路径。</returns>
    public async Task<string> DownloadUpdateAsync(UpdateManifest manifest, CancellationToken cancellationToken = default)
    {
        EmitEvent(UpdateEvents.UpdaterEventDownloadStarted, manifest);

        if (!Directory.Exists(DownloadDirectory))
        {
            Directory.CreateDirectory(DownloadDirectory);
        }

        var fileName = GetDownloadFileName(manifest);
        var filePath = Path.Combine(DownloadDirectory, fileName);

        var downloader = _downloader
            ?? new UpdateDownloader(_httpClient, _config.Headers, _config.DisableChecksumVerification);

        var progress = new Progress<UpdateProgressEventArgs>(args =>
        {
            EmitEvent(UpdateEvents.UpdaterEventDownloadProgress, args);
        });

        try
        {
            await downloader.DownloadAsync(manifest, filePath, progress, cancellationToken);
            EmitEvent(UpdateEvents.UpdaterEventDownloadComplete, manifest);
            return filePath;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidDataException)
        {
            EmitEvent(UpdateEvents.UpdaterEventDownloadError, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 异步安装更新包。
    /// 发射 UpdaterEventInstallStarted 事件；
    /// 解压归档并执行平台特定安装；
    /// 完成后发射 UpdaterEventInstallComplete；错误时发射 UpdaterEventInstallError。
    /// </summary>
    /// <param name="archivePath">更新包归档文件路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示安装操作的异步任务。</returns>
    public async Task InstallUpdateAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"更新包不存在: {archivePath}", archivePath);
        }

        EmitEvent(UpdateEvents.UpdaterEventInstallStarted, archivePath);

        try
        {
            var extractDir = Path.Combine(
                DownloadDirectory,
                $"wails-update-{Guid.NewGuid():N}");

            await UpdateExtractor.ExtractAsync(archivePath, extractDir, cancellationToken);

            // 在解压目录中查找可执行或可安装文件
            var installFile = FindInstallableFile(extractDir);
            if (installFile is not null)
            {
                await UpdateExtractor.InstallUpdateAsync(installFile, cancellationToken);
            }

            EmitEvent(UpdateEvents.UpdaterEventInstallComplete, null);
            EmitEvent(UpdateEvents.UpdaterEventUpdateApplied, null);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or NotSupportedException)
        {
            EmitEvent(UpdateEvents.UpdaterEventInstallError, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 检查并下载更新（如果 AutoDownload 启用）。
    /// 等同于 CheckForUpdatesAsync，由 AutoDownload 标志决定是否自动下载。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析得到的更新清单。</returns>
    public async Task<UpdateManifest> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        return await CheckForUpdatesAsync(cancellationToken);
    }

    /// <summary>
    /// 执行完整更新流程：检查 + 下载 + 安装（如果 AutoInstall 启用）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示完整更新操作的异步任务。</returns>
    public async Task FullUpdateAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await CheckForUpdatesAsync(cancellationToken);

        if (!IsNewerVersion(manifest.Version, CurrentVersion))
        {
            return;
        }

        if (_config.AutoDownload && !string.IsNullOrWhiteSpace(manifest.DownloadURL))
        {
            var archivePath = await DownloadUpdateAsync(manifest, cancellationToken);

            if (_config.AutoInstall)
            {
                await InstallUpdateAsync(archivePath, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 从更新清单的下载 URL 推导本地文件名。
    /// </summary>
    /// <param name="manifest">更新清单。</param>
    /// <returns>本地文件名。</returns>
    private static string GetDownloadFileName(UpdateManifest manifest)
    {
        if (Uri.TryCreate(manifest.DownloadURL, UriKind.Absolute, out var uri))
        {
            var fileName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrEmpty(fileName))
            {
                return fileName;
            }
        }

        return $"update_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// 在解压目录中查找可执行或可安装文件。
    /// 优先级：.exe / .msi（Windows）、.deb / .rpm / .AppImage（Linux）、其他可执行文件。
    /// </summary>
    /// <param name="extractDirectory">解压目录。</param>
    /// <returns>找到的可安装文件路径，未找到返回 null。</returns>
    private static string? FindInstallableFile(string extractDirectory)
    {
        if (!Directory.Exists(extractDirectory))
        {
            return null;
        }

        var allFiles = Directory.GetFiles(extractDirectory, "*", SearchOption.AllDirectories);

        string[] priorityExtensions = OperatingSystem.IsWindows()
            ? [".exe", ".msi"]
            : OperatingSystem.IsLinux()
                ? [".deb", ".rpm", ".appimage", ".AppImage"]
                : [".exe", ".msi"];

        foreach (var ext in priorityExtensions)
        {
            var match = allFiles.FirstOrDefault(f =>
                Path.GetExtension(f).Equals(ext, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        // 回退：返回第一个文件
        return allFiles.FirstOrDefault();
    }

    /// <summary>
    /// 比较版本号，判断 candidate 是否比 current 更新。
    /// 支持语义化版本号格式（如 1.2.3）。
    /// </summary>
    /// <param name="candidate">候选版本号。</param>
    /// <param name="current">当前版本号。</param>
    /// <returns>候选版本更新返回 true，否则返回 false。</returns>
    private static bool IsNewerVersion(string candidate, string current)
    {
        if (Version.TryParse(candidate, out var candidateVer) &&
            Version.TryParse(current, out var currentVer))
        {
            return candidateVer > currentVer;
        }
        return string.Compare(candidate, current, StringComparison.Ordinal) > 0;
    }

    /// <summary>
    /// 发射更新事件（如果事件处理器已注入）。
    /// </summary>
    /// <param name="name">事件名称。</param>
    /// <param name="data">事件数据。</param>
    private void EmitEvent(string name, object? data)
    {
        _eventProcessor?.Emit(name, data);
    }
}
