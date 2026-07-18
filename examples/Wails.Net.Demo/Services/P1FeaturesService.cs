using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wails.Net.Application.Services;
using Wails.Net.Application.Services.Updater;
// 使用别名避免 Application 类型与 Wails.Net.Application 命名空间冲突（CS0118）
using WailsApplication = Wails.Net.Application.Application;

namespace Wails.Net.Demo.Services;

/// <summary>
/// P1 阶段新能力演示服务，将 BrowserManager、Updater、Logger 桥接等能力暴露给前端。
/// </summary>
public class P1FeaturesService
{
    /// <summary>
    /// 后端日志记录器，用于演示 Logger ↔ 前端 console 双向桥接（P1-3）。
    /// </summary>
    private readonly ILogger<P1FeaturesService> _logger;

    /// <summary>
    /// 应用实例引用，用于访问 BrowserManager 等平台管理器。
    /// </summary>
    private readonly WailsApplication _application;

    /// <summary>
    /// 更新服务实例，用于演示多 Provider Updater（P1-8）。
    /// </summary>
    private readonly UpdaterService? _updaterService;

    /// <summary>
    /// 构造 P1FeaturesService 实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="application">应用实例。</param>
    /// <param name="updaterService">更新服务（可选，DI 注入）。</param>
    public P1FeaturesService(
        ILogger<P1FeaturesService> logger,
        WailsApplication application,
        UpdaterService? updaterService = null)
    {
        _logger = logger;
        _application = application;
        _updaterService = updaterService;
    }

    /// <summary>
    /// 通过 BrowserManager 打开外部 URL（P1-1）。
    /// 演示三平台 BrowserManager 的统一 API。
    /// </summary>
    /// <param name="url">要打开的 URL。</param>
    /// <returns>操作结果消息。</returns>
    public string OpenExternalUrl(string url)
    {
        if (_application.BrowserManager is null)
        {
            return "BrowserManager 未注册（当前平台可能未启用）";
        }

        try
        {
            _application.BrowserManager.OpenURL(url);
            return $"已请求打开：{url}";
        }
        catch (Exception ex)
        {
            return $"打开失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 向 ILogger 写入日志（P1-3）。
    /// 启用 BrowserConsoleLogForwarder 后，此日志会自动转发到前端 DevTools console。
    /// </summary>
    /// <param name="level">日志级别（Information/Warning/Error）。</param>
    /// <param name="message">日志消息。</param>
    /// <returns>确认消息。</returns>
    public string LogFromBackend(string level, string message)
    {
        switch (level.ToLowerInvariant())
        {
            case "warning":
                _logger.LogWarning("[Demo] {Message}", message);
                return $"已写入 Warning 日志：{message}";
            case "error":
                _logger.LogError("[Demo] {Message}", message);
                return $"已写入 Error 日志：{message}";
            default:
                _logger.LogInformation("[Demo] {Message}", message);
                return $"已写入 Information 日志：{message}";
        }
    }

    /// <summary>
    /// 演示事件订阅：返回当前应用是否正在运行，以及 ShouldQuit 回调状态（P1-7）。
    /// </summary>
    /// <returns>包含应用状态的 JSON 字符串。</returns>
    public string GetApplicationStatus()
    {
        var status = new
        {
            isRunning = _application.IsRunning,
            shouldQuit = _application.ShouldQuit(),
            hasPostShutdownHook = _application.Options.PostShutdown is not null,
            hasShouldQuitHook = _application.Options.ShouldQuit is not null,
        };
        return JsonSerializer.Serialize(status);
    }

    /// <summary>
    /// 触发应用退出流程（P1-7）。
    /// 注意：此方法会触发 Shutdown 流程，包括 PostShutdown 回调。
    /// </summary>
    public void TriggerShutdown()
    {
        _logger.LogInformation("前端请求触发 Shutdown");
        _application.Quit();
    }

    /// <summary>
    /// 检查更新（P1-8 多 Provider Updater）。
    /// 使用注册的 Provider 链检查更新，返回结果。
    /// </summary>
    /// <returns>更新检查结果的 JSON 字符串。</returns>
    public async Task<string> CheckForUpdatesAsync()
    {
        if (_updaterService is null)
        {
            return JsonSerializer.Serialize(new { error = "UpdaterService 未注册" });
        }

        try
        {
            var manifest = await _updaterService.CheckForUpdatesAsync();
            var result = new
            {
                version = manifest.Version,
                provider = manifest.ProviderName ?? "unknown",
                downloadUrl = manifest.DownloadURL,
                releaseNotes = manifest.ReleaseNotes ?? string.Empty,
                isNewer = !string.IsNullOrEmpty(manifest.Version),
            };
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    /// <summary>
    /// 获取已注册的 Updater Provider 列表（P1-8）。
    /// </summary>
    /// <returns>Provider 名称数组的 JSON 字符串。</returns>
    public string GetRegisteredProviders()
    {
        if (_updaterService is null)
        {
            return "[]";
        }

        var names = _updaterService.Providers.Select(p => p.Name).ToArray();
        return JsonSerializer.Serialize(names);
    }
}
