using Android.Content.Res;
using Android.Webkit;
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
    /// </summary>
    private readonly string _assetPrefix;

    /// <summary>
    /// 使用指定 AssetManager 构造 <see cref="AndroidAssetServer" /> 实例。
    /// </summary>
    /// <param name="assetManager">Android AssetManager 实例，可为 null（非 Android 环境时）。</param>
    /// <param name="assetPrefix">assets 目录内的子路径前缀，默认为空字符串。</param>
    public AndroidAssetServer(AssetManager? assetManager, string assetPrefix = "")
        : base(CreateOptions())
    {
        _assetManager = assetManager;
        _assetPrefix = assetPrefix ?? string.Empty;
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
                return null;
            }

            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
        catch (Java.IO.FileNotFoundException)
        {
            return null;
        }
        catch
        {
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
            RootPath = "assets",
        };
    }
}
