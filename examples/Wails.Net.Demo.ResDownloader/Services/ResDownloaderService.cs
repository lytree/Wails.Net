using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Bindings;
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 资源下载器门面服务：对前端暴露绑定方法，并把代理 / 下载事件桥接到 Wails 事件总线。
/// <para>
/// 前端调用约定：<c>wails.call('ResDownloaderService.方法名', [参数...])</c>；
/// 事件：<c>res:sniffed</c>（新资源）、<c>res:updated</c>（资源状态/进度）、
/// <c>res:proxy</c>（代理状态）、<c>res:cleared</c>（列表清空）。
/// </para>
/// </summary>
public sealed class ResDownloaderService : IDisposable
{
    private static readonly JsonSerializerOptions ImportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly WailsApplication _application;
    private readonly SettingsStore _settings;
    private readonly CertificateAuthority _authority;
    private readonly ResourceRepository _repository;
    private readonly LocalProxyServer _proxy;
    private readonly DownloadManager _downloads;
    private readonly SystemProxyManager _systemProxy = new();
    private readonly ILogger<ResDownloaderService> _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private bool _disposed;

    /// <summary>
    /// 创建门面服务，并订阅代理与下载事件。
    /// </summary>
    /// <param name="application">Wails 应用实例（用于广播事件）。</param>
    /// <param name="settings">设置存储。</param>
    /// <param name="authority">证书颁发机构。</param>
    /// <param name="repository">资源仓库。</param>
    /// <param name="proxy">代理服务器。</param>
    /// <param name="downloads">下载管理器。</param>
    /// <param name="logger">日志。</param>
    public ResDownloaderService(
        WailsApplication application,
        SettingsStore settings,
        CertificateAuthority authority,
        ResourceRepository repository,
        LocalProxyServer proxy,
        DownloadManager downloads,
        ILogger<ResDownloaderService> logger)
    {
        _application = application;
        _settings = settings;
        _authority = authority;
        _repository = repository;
        _proxy = proxy;
        _downloads = downloads;
        _logger = logger;

        _proxy.ResourceSniffed += OnResourceSniffed;
        _downloads.ResourceUpdated += OnResourceUpdated;

        if (_settings.Get().AutoProxy)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await StartProxyAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "自动启动代理失败");
                }
            });
        }
    }

    /* ------------------------------------------------------------------ */
    /* 配置与状态                                                          */
    /* ------------------------------------------------------------------ */

    /// <summary>获取当前设置。</summary>
    /// <returns>设置副本。</returns>
    [Binding]
    public AppSettings GetConfig() => _settings.Get();

    /// <summary>
    /// 保存设置；Host / Port 变化时自动重启代理。
    /// </summary>
    /// <param name="config">新设置。</param>
    /// <returns>规范化后的设置。</returns>
    [Binding]
    public async Task<AppSettings> SaveConfigAsync(AppSettings config)
    {
        var previous = _settings.Get();
        var saved = _settings.Save(config);
        var addressChanged = previous.Port != saved.Port
                             || !string.Equals(previous.Host, saved.Host, StringComparison.OrdinalIgnoreCase);

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (addressChanged && _proxy.IsRunning)
            {
                await _proxy.StopAsync().ConfigureAwait(false);
                _proxy.Start(saved.Host, saved.Port);
                if (_systemProxy.IsEnabled(saved.Port))
                {
                    _systemProxy.Enable(saved.Port);
                }
            }
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "切换监听地址失败");
        }
        finally
        {
            _lifecycleGate.Release();
        }

        var state = BuildProxyState("设置已保存");
        EmitProxyState(state);
        return saved;
    }

    /// <summary>获取应用信息（关于页 / 排障）。</summary>
    /// <returns>应用信息。</returns>
    [Binding]
    public AppInfo GetAppInfo()
    {
        var settings = _settings.Get();
        return new AppInfo
        {
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0",
            Platform = Environment.OSVersion.Platform.ToString(),
            DataDirectory = AppPaths.DataDirectory,
            SettingsPath = _settings.FilePath,
            DownloadDirectory = settings.SaveDirectory,
        };
    }

    /// <summary>获取代理状态。</summary>
    /// <returns>代理状态。</returns>
    [Binding]
    public ProxyState GetProxyState() => BuildProxyState(_proxy.IsRunning ? "代理运行中" : "代理未启动");

    /// <summary>启动代理。</summary>
    /// <returns>启动后的代理状态。</returns>
    [Binding]
    public async Task<ProxyState> StartProxyAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = _settings.Get();
            if (!_proxy.IsRunning)
            {
                try
                {
                    _proxy.Start(settings.Host, settings.Port);
                }
                catch (SocketException ex)
                {
                    var failed = BuildProxyState($"端口 {settings.Port} 启动失败：{ex.Message}");
                    EmitProxyState(failed);
                    return failed;
                }
            }

            if (settings.AutoProxy)
            {
                _systemProxy.Enable(settings.Port);
            }

            var state = BuildProxyState($"代理已启动：{_proxy.Address}");
            EmitProxyState(state);
            return state;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>停止代理，并还原系统代理设置。</summary>
    /// <returns>停止后的代理状态。</returns>
    [Binding]
    public async Task<ProxyState> StopProxyAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _proxy.StopAsync().ConfigureAwait(false);
            _systemProxy.Disable();

            var state = BuildProxyState("代理已停止");
            EmitProxyState(state);
            return state;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>开启/关闭系统代理（指向本机代理）。</summary>
    /// <param name="enabled">是否启用。</param>
    /// <returns>操作后的代理状态。</returns>
    [Binding]
    public ProxyState SetSystemProxy(bool enabled)
    {
        if (enabled)
        {
            if (!_proxy.IsRunning)
            {
                return BuildProxyState("请先启动代理");
            }

            if (!_systemProxy.IsSupported)
            {
                return BuildProxyState("当前平台需手动设置系统代理");
            }

            _systemProxy.Enable(_proxy.Port);
        }
        else
        {
            _systemProxy.Disable();
        }

        var state = BuildProxyState(enabled ? "系统代理已开启" : "系统代理已关闭");
        EmitProxyState(state);
        return state;
    }

    /// <summary>
    /// 设置参与嗅探的资源类型（前端筛选条件会同步到后端，避免无谓的列表增长）。
    /// </summary>
    /// <param name="types">类型集合；空集合表示全部。</param>
    /// <returns>实际生效的类型。</returns>
    [Binding]
    public string[] SetResourceTypes(string[] types)
    {
        _proxy.SetActiveTypes(types);
        var effective = types is { Length: > 0 } && !types.Contains(ResourceClassify.All, StringComparer.OrdinalIgnoreCase)
            ? types
            : [ResourceClassify.All];
        return effective;
    }

    /// <summary>获取当前 MimeMap 中可用的资源类型（含 <c>all</c>）。</summary>
    /// <returns>类型列表。</returns>
    [Binding]
    public string[] GetAvailableTypes()
    {
        var types = MimeTypeMap.DistinctClassifies(_settings.Get().MimeMap);
        types.Insert(0, ResourceClassify.All);
        return [.. types];
    }

    /* ------------------------------------------------------------------ */
    /* 资源                                                                */
    /* ------------------------------------------------------------------ */

    /// <summary>获取已嗅探的资源列表（按列表顺序）。</summary>
    /// <returns>资源列表。</returns>
    [Binding]
    public List<ResourceItem> GetResources() => _repository.Snapshot();

    /// <summary>
    /// 清空资源列表。
    /// </summary>
    /// <param name="keepActive">是否保留下载中 / 排队中的资源。</param>
    /// <returns>被移除的资源标识。</returns>
    [Binding]
    public string[] ClearResources(bool keepActive)
    {
        var removed = _repository.Clear(keepActive);
        Emit("res:cleared", new { ids = removed });
        return [.. removed];
    }

    /// <summary>
    /// 按 URL 摘要批量删除资源。
    /// </summary>
    /// <param name="signs">URL 摘要集合。</param>
    /// <returns>实际删除数量。</returns>
    [Binding]
    public int DeleteResources(string[] signs)
    {
        var removed = _repository.RemoveBySign(signs);
        if (removed > 0)
        {
            Emit("res:deleted", new { count = removed });
        }

        return removed;
    }

    /// <summary>
    /// 修改资源描述（描述非空时会作为下载文件名）。
    /// </summary>
    /// <param name="resourceId">资源标识。</param>
    /// <param name="description">新描述。</param>
    /// <returns>更新后的资源。</returns>
    [Binding]
    public ResourceItem? UpdateDescription(string resourceId, string description)
    {
        var updated = _repository.Update(resourceId, item => item.Description = (description ?? string.Empty).Trim());
        if (updated is not null)
        {
            Emit("res:updated", updated);
        }

        return updated;
    }

    /// <summary>为指定资源创建下载任务。</summary>
    /// <param name="resourceId">资源标识。</param>
    /// <returns>入队后的资源；已在下载中返回 <c>null</c>。</returns>
    [Binding]
    public ResourceItem? DownloadResource(string resourceId)
    {
        var resource = _repository.Get(resourceId)
                       ?? throw new InvalidOperationException("资源不存在，可能已被清理");
        return _downloads.Enqueue(resource);
    }

    /// <summary>批量加入下载队列。</summary>
    /// <param name="resourceIds">资源标识集合。</param>
    /// <returns>实际入队数量。</returns>
    [Binding]
    public int DownloadResources(string[] resourceIds)
    {
        var count = 0;
        foreach (var id in resourceIds ?? [])
        {
            var resource = _repository.Get(id);
            if (resource is not null && _downloads.Enqueue(resource) is not null)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>取消下载任务。</summary>
    /// <param name="resourceId">资源标识。</param>
    /// <returns>已发出取消请求返回 <c>true</c>。</returns>
    [Binding]
    public bool CancelDownload(string resourceId) => _downloads.Cancel(resourceId);

    /// <summary>批量取消下载任务。</summary>
    /// <param name="resourceIds">资源标识集合。</param>
    /// <returns>实际取消数量。</returns>
    [Binding]
    public int CancelDownloads(string[] resourceIds)
    {
        var count = 0;
        foreach (var id in resourceIds ?? [])
        {
            if (_downloads.Cancel(id))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 批量导入资源（每行一条 URL 编码的 JSON 或纯 URL）。
    /// </summary>
    /// <param name="lines">导入内容行。</param>
    /// <returns>实际导入的资源。</returns>
    [Binding]
    public List<ResourceItem> ImportResources(string[] lines)
    {
        var items = new List<ResourceItem>();
        foreach (var raw in lines ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var line = raw.Trim();
            var text = line;
            try
            {
                text = Uri.UnescapeDataString(line);
            }
            catch (UriFormatException)
            {
                // 保持原样尝试解析
            }

            if (text.StartsWith('{'))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<ResourceItem>(text, ImportJsonOptions);
                    if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.Url))
                    {
                        items.Add(parsed);
                        continue;
                    }
                }
                catch (JsonException)
                {
                    // 落到纯 URL 分支
                }
            }

            if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
                && (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                // 纯 URL 导入没有响应头，按 URL 扩展名推断类型（png → image、mp4 → video …）
                var resolved = MimeTypeMap.Resolve(_settings.Get().MimeMap, null, text);
                var suffix = resolved?.Suffix ?? ResourceNaming.NormalizeSuffix(null, text);
                items.Add(new ResourceItem
                {
                    Url = text,
                    UrlSign = ResourceRepository.ComputeSign(text),
                    Domain = ResourceNaming.TopLevelDomain(text),
                    Classify = resolved?.Classify ?? ResourceClassify.Stream,
                    Suffix = suffix,
                    ContentType = "application/octet-stream",
                    Title = ResourceNaming.BuildTitle(text, null, suffix),
                    Status = ResourceStatus.Ready,
                });
            }
        }

        var added = _repository.Import(items, _settings.Get().InsertTail);
        foreach (var item in added)
        {
            Emit("res:sniffed", item);
        }

        return added;
    }

    /// <summary>
    /// 导出资源内容到保存目录下的文本文件，并在文件管理器中定位。
    /// </summary>
    /// <param name="content">文件内容（每行一条）。</param>
    /// <returns>导出文件完整路径。</returns>
    [Binding]
    public string ExportResources(string content)
    {
        var directory = AppPaths.EnsureDirectory(_settings.Get().SaveDirectory);
        var path = Path.Combine(directory, $"res-downloader-{DateTime.Now:yyyyMMddHHmmss}.txt");
        File.WriteAllText(path, content ?? string.Empty);
        RevealInFolder(path);
        return path;
    }

    /// <summary>
    /// 解密加密媒体（视频号），生成 <c>_decrypt</c> 副本。
    /// </summary>
    /// <param name="resourceId">资源标识。</param>
    /// <returns>解密后的文件路径。</returns>
    [Binding]
    public string DecryptResource(string resourceId)
    {
        var resource = _repository.Get(resourceId)
                       ?? throw new InvalidOperationException("资源不存在，可能已被清理");

        if (string.IsNullOrWhiteSpace(resource.SavePath) || !File.Exists(resource.SavePath))
        {
            throw new InvalidOperationException("请先下载该资源，再执行解密");
        }

        var path = MediaDecryptor.DecryptFile(resource.SavePath, resource.DecodeKey);
        var updated = _repository.Update(resourceId, item =>
        {
            item.SavePath = path;
            item.Status = ResourceStatus.Done;
            item.Message = "已解密";
        });

        if (updated is not null)
        {
            Emit("res:updated", updated);
        }

        return path;
    }

    /* ------------------------------------------------------------------ */
    /* 证书与系统操作                                                      */
    /* ------------------------------------------------------------------ */

    /// <summary>导出根证书（.cer）供手动安装。</summary>
    /// <returns>证书文件路径。</returns>
    [Binding]
    public string ExportCertificate() => _authority.ExportRootCertificate();

    /// <summary>一键安装根证书到“当前用户 → 受信任的根证书颁发机构”。</summary>
    /// <returns>安装成功返回 <c>true</c>。</returns>
    [Binding]
    public bool InstallCertificate() => _authority.Install();

    /// <summary>判断根证书是否已安装。</summary>
    /// <returns>已安装返回 <c>true</c>。</returns>
    [Binding]
    public bool IsCertificateInstalled() => _authority.IsInstalled();

    /// <summary>获取根证书下载地址（走本地 API，便于在浏览器里直接信任）。</summary>
    /// <returns>下载地址。</returns>
    [Binding]
    public string GetCertificateUrl()
    {
        var settings = _settings.Get();
        var host = settings.Host is "0.0.0.0" or "*" or "+" ? "127.0.0.1" : settings.Host;
        return $"http://{host}:{settings.Port}/__rdapi/cert";
    }

    /// <summary>在文件管理器中打开下载目录。</summary>
    [Binding]
    public void OpenSaveFolder()
    {
        var directory = AppPaths.EnsureDirectory(_settings.Get().SaveDirectory);
        OpenWithShell(directory);
    }

    /// <summary>
    /// 打开文件所在目录并选中文件；路径是目录时直接打开目录。
    /// </summary>
    /// <param name="path">文件或目录路径。</param>
    [Binding]
    public void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            OpenWithShell(path);
            return;
        }

        if (File.Exists(path))
        {
            RevealInFolder(path);
        }
    }

    /// <summary>使用系统默认浏览器打开链接。</summary>
    /// <param name="url">要打开的链接。</param>
    [Binding]
    public void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        OpenWithShell(url);
    }

    /// <summary>
    /// 重置应用：清空设置与缓存并重启（对应 res-downloader 的「清理缓存并重启」）。
    /// </summary>
    [Binding]
    public void ResetApp()
    {
        try
        {
            _settings.Reset();
            _proxy.StopAsync().GetAwaiter().GetResult();
            _systemProxy.Disable();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "重置应用时清理失败");
        }

        RestartApplication();
    }

    /// <summary>
    /// 应用退出前清理：停止代理并还原系统代理，避免用户断网。
    /// </summary>
    public void Shutdown()
    {
        try
        {
            _proxy.StopAsync().GetAwaiter().GetResult();
            _systemProxy.Disable();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "退出清理时出错");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _proxy.ResourceSniffed -= OnResourceSniffed;
        _downloads.ResourceUpdated -= OnResourceUpdated;
        _downloads.Dispose();
        _lifecycleGate.Dispose();
        _systemProxy.Disable();
    }

    private void OnResourceSniffed(ResourceItem resource) => Emit("res:sniffed", resource);

    private void OnResourceUpdated(ResourceItem resource) => Emit("res:updated", resource);

    private void EmitProxyState(ProxyState state) => Emit("res:proxy", state);

    private void Emit(string eventName, object? payload)
    {
        try
        {
            _application.BroadcastEvent(eventName, payload);
        }
        catch (Exception ex)
        {
            // 前端尚未就绪或传输层已关闭时忽略，避免影响代理线程
            _logger.LogDebug(ex, "广播事件 {Event} 失败", eventName);
        }
    }

    private ProxyState BuildProxyState(string message)
    {
        var settings = _settings.Get();
        var state = new ProxyState
        {
            Running = _proxy.IsRunning,
            Address = _proxy.IsRunning ? _proxy.Address : $"{settings.Host}:{settings.Port}",
            SystemProxyEnabled = _systemProxy.IsEnabled(settings.Port),
            CertificateInstalled = _authority.IsInstalled(),
            CertificatePath = _authority.RootCerPath,
            CertificateUrl = GetCertificateUrl(),
            UpstreamProxyEnabled = settings.OpenProxy && !string.IsNullOrWhiteSpace(settings.UpstreamProxy),
            UpstreamProxy = settings.UpstreamProxy,
            Message = message,
        };

        return state;
    }

    private void RestartApplication()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = AppContext.BaseDirectory,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "重启应用失败");
        }

        try
        {
            _application.Quit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "退出应用失败");
        }
    }

    private void RevealInFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return;
            }

            OpenWithShell(Path.GetDirectoryName(path) ?? path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "定位文件 {Path} 失败", path);
        }
    }

    private void OpenWithShell(string target)
    {
        try
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out _))
            {
                // http/https 链接交给默认浏览器
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open", $"\"{target}\"") { UseShellExecute = false });
            }
            else
            {
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{target}\"") { UseShellExecute = false });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "打开 {Target} 失败", target);
        }
    }
}
