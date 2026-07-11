namespace Wails.Net.Runtime.Js;

/// <summary>
/// JavaScript 运行时生成选项。
/// 对应 Wails v3 Go 版本 <c>internal/runtime/runtime.go</c> 中运行时生成所需的配置参数。
/// </summary>
public class RuntimeOptions
{
    /// <summary>
    /// 获取或设置平台标识（"windows"、"linux"、"server"）。
    /// </summary>
    public string Platform { get; set; } = "unknown";

    /// <summary>
    /// 获取或设置是否启用调试模式。
    /// </summary>
    public bool IsDebug { get; set; }

    /// <summary>
    /// 获取或设置是否为 Server 模式（无 GUI 的容器化部署）。
    /// </summary>
    public bool IsServerMode { get; set; }

    /// <summary>
    /// 获取或设置资源服务器 URL。
    /// </summary>
    public string AssetServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 WebSocket URL（仅 Server 模式使用）。
    /// </summary>
    public string WebSocketUrl { get; set; } = string.Empty;
}
