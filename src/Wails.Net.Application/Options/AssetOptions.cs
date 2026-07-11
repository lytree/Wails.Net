namespace Wails.Net.Application.Options;

/// <summary>
/// 资源服务器配置选项（应用级）。
/// 对应 Wails v3 Go 版本 application_options.go 中的 AssetServer 配置。
/// </summary>
public sealed class AssetOptions
{
    /// <summary>
    /// 获取或设置资源根路径。
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置资源服务器监听端口，0 表示自动分配。
    /// </summary>
    public int Port { get; set; } = 0;

    /// <summary>
    /// 获取或设置是否启用 CORS（跨域资源共享）。
    /// </summary>
    public bool EnableCORS { get; set; } = true;

    /// <summary>
    /// 获取或设置是否启用 ETag 缓存。
    /// </summary>
    public bool EnableETag { get; set; } = true;

    /// <summary>
    /// 获取或设置是否启用 Range 请求（部分内容请求）。
    /// </summary>
    public bool EnableRangeRequests { get; set; } = true;
}
