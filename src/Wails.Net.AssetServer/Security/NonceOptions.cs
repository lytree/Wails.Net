namespace Wails.Net.AssetServer.Security;

/// <summary>
/// CSP Nonce 注入选项。
/// 对应 Tauri v2 的 CSP nonce 防注入机制：每个请求生成唯一 nonce，
/// 注入到 HTML 的 script/link 标签，并在 CSP 头中允许该 nonce 执行。
/// </summary>
public class NonceOptions
{
    /// <summary>
    /// 获取或设置是否启用 nonce 注入。
    /// 默认值为 false（保持现有 CSP 行为不变）。
    /// </summary>
    public bool EnableNonce { get; set; }

    /// <summary>
    /// 获取或设置基础 CSP 策略字符串。
    /// 启用 nonce 后，<see cref="NonceInjector.BuildCspHeader"/> 会将 nonce 追加到
    /// <c>script-src</c> 指令。
    /// 默认值： <c>default-src 'self'; script-src 'self'; style-src 'self'</c>
    /// </summary>
    public string CspPolicy { get; set; } = "default-src 'self'; script-src 'self'; style-src 'self'";

    /// <summary>
    /// 获取或设置是否将 nonce 注入到 HTML 标签的 <c>nonce</c> 属性。
    /// 默认值为 true。若为 false，仅设置 CSP 头而不修改 HTML。
    /// </summary>
    public bool InjectIntoHtml { get; set; } = true;
}
