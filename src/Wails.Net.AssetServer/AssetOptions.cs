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

    /// <summary>
    /// 获取或设置是否启用 SPA 路由回退。
    /// 启用后，当请求的资源不存在时，自动回退到 <see cref="DefaultDocument"/>。
    /// 适用于 Vue/React/Angular 等前端框架的客户端路由。
    /// 默认值为 false（由 Application 层根据 <c>DesktopHostOptions</c> 启用）。
    /// </summary>
    public bool EnableSpaFallback { get; set; }

    /// <summary>
    /// 获取或设置 SPA 回退使用的默认文档名称。
    /// 当 <see cref="EnableSpaFallback"/> 为 true 且资源未找到时使用。
    /// 默认值为 "index.html"。
    /// </summary>
    public string DefaultDocument { get; set; } = "index.html";

    /// <summary>
    /// 获取或设置自定义 MIME 类型映射字典。
    /// 键为文件扩展名（含前导点，不区分大小写，如 <c>.webmanifest</c>），
    /// 值为对应的 MIME 类型。
    /// 查找时优先于此字典，未命中再使用内置映射。
    /// </summary>
    public Dictionary<string, string> CustomMimeTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取或设置自定义 MIME 类型解析器。
    /// 传入文件路径，返回对应 MIME 类型；返回 null 表示未识别，交由内置映射处理。
    /// 优先级最高，先于此字典 <see cref="CustomMimeTypes"/> 与内置映射执行。
    /// </summary>
    public Func<string, string?>? MimeTypeResolver { get; set; }
}
