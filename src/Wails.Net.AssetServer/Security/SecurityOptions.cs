namespace Wails.Net.AssetServer.Security;

/// <summary>
/// 资源服务器安全选项，聚合 CSP nonce 注入与 Isolation 模式配置。
/// 对应 Tauri v2 的安全配置：CSP nonce 防注入 + Isolation iframe 隔离。
/// </summary>
public class SecurityOptions
{
    /// <summary>
    /// 获取或设置 CSP nonce 注入配置。
    /// 默认值：未启用（保持现有 CSP 行为不变）。
    /// </summary>
    public NonceOptions Nonce { get; set; } = new();

    /// <summary>
    /// 获取或设置 Isolation Pattern 配置。
    /// 默认值：未启用。
    /// </summary>
    public IsolationOptions Isolation { get; set; } = new();
}
