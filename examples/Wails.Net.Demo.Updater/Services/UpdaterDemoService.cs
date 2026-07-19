using Wails.Net.Application.Bindings;
using Wails.Net.Application.Services;
using Wails.Net.Application.Services.Updater;

namespace Wails.Net.Demo.Updater.Services;

/// <summary>
/// 更新操作历史记录，包含阶段、消息与时间戳。
/// </summary>
/// <param name="Stage">阶段（check / download / install / info / error）。</param>
/// <param name="Message">消息内容。</param>
/// <param name="Time">时间戳。</param>
public sealed record UpdateLogRecord(string Stage, string Message, DateTime Time);

/// <summary>
/// 模拟 stable 渠道的更新提供者。
/// 当前版本 1.0.0，远端 stable 版本 1.0.0（无更新）。
/// </summary>
public sealed class MockStableUpdateProvider : IUpdateProvider
{
    /// <summary>Provider 名称。</summary>
    public string Name => "mock-stable";

    /// <summary>返回 stable 渠道的更新清单。</summary>
    public Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var manifest = new UpdateManifest
        {
            Version = "1.0.0",
            ReleaseNotes = "（Stable 渠道）当前已是最新版本",
            DownloadURL = string.Empty,
        };
        return Task.FromResult<UpdateManifest?>(manifest);
    }
}

/// <summary>
/// 模拟 beta 渠道的更新提供者。
/// 当前版本 1.0.0，远端 beta 版本 1.1.0（有更新）。
/// </summary>
public sealed class MockBetaUpdateProvider : IUpdateProvider
{
    /// <summary>Provider 名称。</summary>
    public string Name => "mock-beta";

    /// <summary>返回 beta 渠道的更新清单（模拟发现新版本）。</summary>
    public Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var manifest = new UpdateManifest
        {
            Version = "1.1.0",
            ReleaseNotes = "（Beta 渠道）新增多 Provider 链式检查；修复若干问题。",
            DownloadURL = "https://example.com/wails-net-demo/beta/1.1.0.zip",
            Checksum = "0000000000000000000000000000000000000000000000000000000000000000",
            PublishedDate = DateTime.UtcNow,
        };
        return Task.FromResult<UpdateManifest?>(manifest);
    }
}

/// <summary>
/// 模拟 RC 渠道的更新提供者。
/// 当前版本 1.0.0，远端 RC 版本 1.0.5（有更新）。
/// </summary>
public sealed class MockRcUpdateProvider : IUpdateProvider
{
    /// <summary>Provider 名称。</summary>
    public string Name => "mock-rc";

    /// <summary>返回 RC 渠道的更新清单（模拟发现新版本）。</summary>
    public Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var manifest = new UpdateManifest
        {
            Version = "1.0.5",
            ReleaseNotes = "（RC 渠道）候选版本，仅包含 bug 修复。",
            DownloadURL = "https://example.com/wails-net-demo/rc/1.0.5.zip",
            PublishedDate = DateTime.UtcNow.AddDays(-3),
        };
        return Task.FromResult<UpdateManifest?>(manifest);
    }
}

/// <summary>
/// 更新演示服务，封装 UpdaterService 的多 Provider 配置与触发逻辑。
/// 通过 [Binding] 暴露给前端：
/// - 切换 Provider 链（stable / beta / rc / 三者顺序链）
/// - 设置当前版本号
/// - 触发检查 / 下载 / 安装
/// - 查询 Provider 列表与历史
/// </summary>
public sealed class UpdaterDemoService
{
    /// <summary>
    /// 框架 UpdaterService，由 DI 注入。
    /// </summary>
    private readonly UpdaterService _updater;

    /// <summary>
    /// 操作历史列表，使用锁保证线程安全。
    /// </summary>
    private readonly List<UpdateLogRecord> _history = new();

    /// <summary>
    /// 历史记录锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 构造 UpdaterDemoService 实例。
    /// </summary>
    /// <param name="updater">框架 UpdaterService。</param>
    public UpdaterDemoService(UpdaterService updater)
    {
        _updater = updater;
    }

    /// <summary>
    /// 获取当前应用版本号。
    /// </summary>
    /// <returns>当前版本号字符串。</returns>
    [Binding]
    public string GetCurrentVersion() => _updater.CurrentVersion;

    /// <summary>
    /// 设置当前应用版本号（用于演示触发"有更新"或"无更新"的不同场景）。
    /// </summary>
    /// <param name="version">新的版本号，如 "1.0.0"、"1.2.0"。</param>
    [Binding]
    public void SetCurrentVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        _updater.CurrentVersion = version;
        Record("info", $"当前版本已设置为 {version}");
    }

    /// <summary>
    /// 获取已注册的 Provider 名称列表。
    /// </summary>
    /// <returns>Provider 名称列表。</returns>
    [Binding]
    public List<string> GetProviders()
    {
        return _updater.Providers.Select(p => p.Name).ToList();
    }

    /// <summary>
    /// 切换 Provider 链。
    /// <list type="bullet">
    /// <item><c>stable</c>：仅注册 mock-stable</item>
    /// <item><c>beta</c>：仅注册 mock-beta</item>
    /// <item><c>rc</c>：仅注册 mock-rc</item>
    /// <item><c>chain</c>：按 stable → rc → beta 顺序注册（首个返回非 null 者胜出）</item>
    /// </list>
    /// </summary>
    /// <param name="preset">预设名称。</param>
    [Binding]
    public void SwitchProviderChain(string preset)
    {
        _updater.ClearProviders();
        switch (preset?.ToLowerInvariant())
        {
            case "stable":
                _updater.AddProvider(new MockStableUpdateProvider());
                break;
            case "beta":
                _updater.AddProvider(new MockBetaUpdateProvider());
                break;
            case "rc":
                _updater.AddProvider(new MockRcUpdateProvider());
                break;
            case "chain":
                _updater.AddProvider(new MockStableUpdateProvider());
                _updater.AddProvider(new MockRcUpdateProvider());
                _updater.AddProvider(new MockBetaUpdateProvider());
                break;
            default:
                Record("error", $"未知预设：{preset}");
                return;
        }
        Record("info", $"已切换 Provider 链：{preset}（{_updater.Providers.Count} 个 Provider）");
    }

    /// <summary>
    /// 异步检查更新。返回胜出 Provider 的更新清单。
    /// </summary>
    /// <returns>包含版本号、发行说明、下载 URL、Provider 名称的对象。</returns>
    [Binding]
    public async Task<object> CheckForUpdatesAsync()
    {
        try
        {
            var manifest = await _updater.CheckForUpdatesAsync();
            var hasUpdate = !string.Equals(manifest.Version, _updater.CurrentVersion, StringComparison.Ordinal);
            Record("check", $"检查完成：远端版本 {manifest.Version}（来源 {manifest.ProviderName}），{(hasUpdate ? "有更新" : "已是最新")}");
            return new
            {
                version = manifest.Version,
                releaseNotes = manifest.ReleaseNotes,
                downloadUrl = manifest.DownloadURL,
                provider = manifest.ProviderName,
                hasUpdate,
                currentVersion = _updater.CurrentVersion,
            };
        }
        catch (Exception ex)
        {
            Record("error", $"检查失败：{ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 异步下载更新包。本 Demo 的 URL 不可达，仅演示调用流程；
    /// 实际应用中应使用真实可下载的 URL。
    /// </summary>
    /// <param name="downloadUrl">更新包下载 URL。</param>
    /// <returns>下载结果消息。</returns>
    [Binding]
    public async Task<string> DownloadUpdateAsync(string downloadUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadUrl);
        try
        {
            var manifest = new UpdateManifest
            {
                Version = "0.0.0",
                DownloadURL = downloadUrl,
            };
            var path = await _updater.DownloadUpdateAsync(manifest);
            Record("download", $"下载完成：{path}");
            return $"下载完成：{path}";
        }
        catch (Exception ex)
        {
            Record("error", $"下载失败：{ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 获取操作历史记录列表的副本。
    /// </summary>
    /// <returns>历史记录列表。</returns>
    [Binding]
    public List<UpdateLogRecord> GetHistory()
    {
        lock (_lock)
        {
            return new List<UpdateLogRecord>(_history);
        }
    }

    /// <summary>
    /// 清空操作历史记录。
    /// </summary>
    [Binding]
    public void ClearHistory()
    {
        lock (_lock)
        {
            _history.Clear();
        }
    }

    /// <summary>
    /// 记录一条历史。
    /// </summary>
    /// <param name="stage">阶段。</param>
    /// <param name="message">消息。</param>
    private void Record(string stage, string message)
    {
        lock (_lock)
        {
            _history.Add(new UpdateLogRecord(stage, message, DateTime.Now));
            if (_history.Count > 100)
            {
                _history.RemoveAt(0);
            }
        }
    }
}
