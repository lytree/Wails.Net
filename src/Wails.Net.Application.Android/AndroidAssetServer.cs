using Android.Content.Res;
using Android.Webkit;
using Microsoft.Extensions.Logging;
using Wails.Net.AssetServer;

namespace Wails.Net.Application.Platform;

/// <summary>
/// Android 平台资源服务器，从 APK 的 assets 目录读取资源。
/// 对应 Tauri v2 的 Android 资源加载方案：使用自定义 scheme（http://wails.localhost/）
/// 通过 <see cref="WebViewClient" /> 的 <c>ShouldInterceptRequest</c> 重写拦截请求，
/// 再由本类从 <c>AssetManager</c> 读取 assets 目录中的文件。
/// </summary>
public sealed class AndroidAssetServer : AssetServer.AssetServer
{
    /// <summary>
    /// Android AssetManager 引用，用于读取 APK 内的 assets 目录。
    /// </summary>
    private readonly AssetManager? _assetManager;

    /// <summary>
    /// assets 目录内的子路径前缀，默认为空字符串（直接从 assets 根目录读取）。
    /// 非空时拼接在请求路径之前，例如 <c>"www"</c> 会将 <c>index.html</c> 解析为 <c>www/index.html</c>。
    /// </summary>
    private readonly string _assetPrefix;

    /// <summary>
    /// 日志器，用于诊断 Asset 加载失败等问题。可为 null（未配置日志时）。
    /// </summary>
    private readonly ILogger<AndroidAssetServer>? _logger;

    /// <summary>
    /// 使用指定 AssetManager 构造 <see cref="AndroidAssetServer" /> 实例。
    /// </summary>
    /// <param name="assetManager">Android AssetManager 实例，可为 null（非 Android 环境时）。</param>
    /// <param name="assetPrefix">assets 目录内的子路径前缀，默认为空字符串。</param>
    public AndroidAssetServer(AssetManager? assetManager, string assetPrefix = "")
        : this(assetManager, assetPrefix, logger: null)
    {
    }

    /// <summary>
    /// 使用指定 AssetManager 和日志器构造 <see cref="AndroidAssetServer" /> 实例。
    /// </summary>
    /// <param name="assetManager">Android AssetManager 实例，可为 null（非 Android 环境时）。</param>
    /// <param name="assetPrefix">assets 目录内的子路径前缀，默认为空字符串。</param>
    /// <param name="logger">日志器实例，可为 null。</param>
    public AndroidAssetServer(AssetManager? assetManager, string assetPrefix, ILogger<AndroidAssetServer>? logger)
        : base(CreateOptions())
    {
        _assetManager = assetManager;
        _assetPrefix = assetPrefix ?? string.Empty;
        _logger = logger;
    }

    /// <summary>
    /// 从 APK assets 目录读取指定路径的文件内容。
    /// 路径会去除前导斜杠并拼接 <see cref="_assetPrefix" /> 前缀。
    /// </summary>
    /// <param name="path">请求的资源路径。</param>
    /// <returns>文件内容字节组；若文件不存在或 AssetManager 不可用则返回 null。</returns>
    protected override byte[]? ReadAssetCore(string path)
    {
        if (_assetManager is null || string.IsNullOrEmpty(path))
        {
            return null;
        }

        var normalizedPath = path.TrimStart('/');
        if (string.IsNullOrEmpty(normalizedPath))
        {
            normalizedPath = "index.html";
        }

        // 拼接 assets 目录前缀
        if (!string.IsNullOrEmpty(_assetPrefix))
        {
            normalizedPath = $"{_assetPrefix}/{normalizedPath}";
        }

        try
        {
            using var stream = _assetManager.Open(normalizedPath);
            if (stream is null)
            {
                _logger?.LogWarning("Android AssetManager.Open 返回 null：{Path}", normalizedPath);
                return null;
            }

            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
        catch (Java.IO.FileNotFoundException)
        {
            // 文件不存在是正常路径（404），不记录警告
            return null;
        }
        catch (Java.IO.IOException ex)
        {
            // IO 异常（AssetManager 损坏、APK 打包错误等），记录错误便于诊断
            _logger?.LogError(ex, "读取 Android Asset 失败（IO 异常）：{Path}", normalizedPath);
            return null;
        }
        catch (Exception ex)
        {
            // 其他未预期异常，记录错误避免静默吞掉
            _logger?.LogError(ex, "读取 Android Asset 失败（未预期异常）：{Path}", normalizedPath);
            return null;
        }
    }

    /// <summary>
    /// 创建资源服务器选项。
    /// </summary>
    /// <returns>配置好的 <see cref="AssetOptions" /> 实例。</returns>
    private static AssetOptions CreateOptions()
    {
        return new AssetOptions
        {
            Handler = "android-asset",
            // RootPath 在 Android 平台不使用（直接由 AssetManager.Open 读取）
            RootPath = string.Empty,
        };
    }
}
