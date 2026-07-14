namespace Wails.Net.Application.Security;

/// <summary>
/// CORS（跨域资源共享）配置选项。
/// 对应 Tauri v2 的 CORS 安全配置，替代传输层硬编码的 <c>Access-Control-Allow-Origin: *</c>。
/// 支持白名单回显（仅允许的 Origin 返回）而非通配符 <c>*</c>，提升安全性。
/// </summary>
public sealed class CorsOptions
{
    /// <summary>
    /// 获取或设置是否启用 CORS（默认 true）。
    /// 禁用时传输层不添加 CORS 头部。
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 获取或设置允许的 Origin 列表。
    /// 请求的 Origin 在此列表中时，回显该 Origin 作为 Access-Control-Allow-Origin 响应头。
    /// 空列表表示允许所有本地源（wails://, localhost, 127.0.0.1）。
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = new();

    /// <summary>
    /// 获取或设置允许的 HTTP 方法（默认 GET, POST, PUT, DELETE, OPTIONS）。
    /// </summary>
    public string AllowedMethods { get; set; } = "GET, POST, PUT, DELETE, OPTIONS";

    /// <summary>
    /// 获取或设置允许的请求头（默认 Content-Type, Authorization）。
    /// </summary>
    public string AllowedHeaders { get; set; } = "Content-Type, Authorization";

    /// <summary>
    /// 获取或设置是否允许携带凭证（默认 false）。
    /// </summary>
    public bool AllowCredentials { get; set; } = false;

    /// <summary>
    /// 获取或设置预检请求缓存时间（秒，默认 3600）。
    /// </summary>
    public int MaxAgeSeconds { get; set; } = 3600;

    /// <summary>
    /// 检查指定 Origin 是否在允许范围内。
    /// 本地源（wails://, localhost, 127.0.0.1）总是允许；
    /// 其他源需在 <see cref="AllowedOrigins"/> 列表中。
    /// </summary>
    /// <param name="origin">要校验的 Origin。</param>
    /// <returns>允许时返回 Origin 字符串作为响应头值；拒绝时返回 null。</returns>
    public string? ResolveAllowedOrigin(string? origin)
    {
        if (!Enabled || string.IsNullOrEmpty(origin)) return null;

        // 本地源总是允许
        if (IsLocalOrigin(origin))
        {
            return origin;
        }

        // 精确匹配
        foreach (var allowed in AllowedOrigins)
        {
            if (string.Equals(origin, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return origin;
            }
        }

        return null;
    }

    /// <summary>
    /// 检查是否为本地源。
    /// </summary>
    private static bool IsLocalOrigin(string origin)
    {
        return origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("wails://", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("https://wails.localhost", StringComparison.OrdinalIgnoreCase);
    }
}
