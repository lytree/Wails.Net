using System.Net;

namespace Wails.Net.AssetServer;

/// <summary>
/// 资源服务器选项。
/// 对应 Wails v3 Go 版本 <c>internal/assetserver/options.go</c> 中的 Options 结构。
/// 用于描述资源处理器的名称、根路径、中间件配置、错误处理及超时设置。
/// </summary>
public class AssetOptions
{
    /// <summary>
    /// 获取或设置资源处理器名称。
    /// </summary>
    public string Handler { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置资源根路径。
    /// 用于从文件系统或嵌入资源中定位资源文件。
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置中间件配置字典。
    /// 键为中间件名称，值为中间件参数。
    /// </summary>
    public Dictionary<string, string> Middleware { get; set; } = new();

    /// <summary>
    /// 获取或设置自定义错误处理回调。
    /// 当资源处理过程中发生错误（如 404、500）时调用。
    /// 若为 null，则使用默认错误处理（返回对应状态码和简单错误体）。
    /// </summary>
    public Action<HttpListenerContext, Exception>? ErrorHandler { get; set; }

    /// <summary>
    /// 获取或设置单次请求处理的超时时间。
    /// 超过此时间的请求将被取消并返回 503 状态码。
    /// 默认值为 30 秒，对应 Go 版本默认无限制但建议设置上限。
    /// </summary>
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
