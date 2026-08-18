namespace Wails.Net.Application.Security;

/// <summary>
/// IPC 源校验器，验证 WebView 消息来源是否可信。
/// 对应 Tauri v2 的 IPC 安全校验。
/// </summary>
public sealed class IpcOriginValidator
{
    private readonly UrlWhitelist _whitelist;

    /// <summary>构造 IPC 源校验器</summary>
    public IpcOriginValidator(UrlWhitelist whitelist)
    {
        ArgumentNullException.ThrowIfNull(whitelist);
        _whitelist = whitelist;
    }

    /// <summary>验证 IPC 消息来源是否允许</summary>
    public bool Validate(string? origin)
    {
        // 本地源（wails://, http://localhost, http://127.0.0.1）总是允许
        if (string.IsNullOrEmpty(origin)) return true;
        if (IsLocalOrigin(origin)) return true;
        return _whitelist.IsAllowed(origin);
    }

    /// <summary>
    /// 验证 IPC 消息来源信息是否允许（OriginInfo 三字段版本）。
    /// 对应 Wails v3 Go 版本中 <c>originInfo</c> 校验逻辑：
    /// <list type="bullet">
    /// <item>当 <see cref="OriginInfo.IsMainFrame"/> 为 false 时（iframe 消息），
    /// 同时校验 <see cref="OriginInfo.Origin"/> 与 <see cref="OriginInfo.TopOrigin"/>；</item>
    /// <item>当为 true（主框架消息）时，与单参数版本行为一致。</item>
    /// </list>
    /// 任一字段不在白名单且非本地源时拒绝。
    /// </summary>
    /// <param name="originInfo">消息来源信息。</param>
    /// <returns>若允许返回 true；否则返回 false。</returns>
    public bool Validate(OriginInfo originInfo)
    {
        // 主框架消息：与单字段校验等价
        if (originInfo.IsMainFrame)
        {
            return Validate(originInfo.Origin);
        }

        // iframe 消息：Origin 与 TopOrigin 都需校验
        if (!Validate(originInfo.Origin))
        {
            return false;
        }

        // TopOrigin 与 Origin 不同时再校验，相同则跳过
        if (!string.IsNullOrEmpty(originInfo.TopOrigin) &&
            !string.Equals(originInfo.Origin, originInfo.TopOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return Validate(originInfo.TopOrigin);
        }

        return true;
    }

    /// <summary>
    /// 检查是否为本地源。审计 §3.4：原先 <c>StartsWith</c> 会被
    /// <c>http://localhost.evil.example</c>、<c>http://127.0.0.1.evil.example</c>、
    /// <c>http://localhostfoo</c>、<c>https://wails.localhost.evil.example</c>
    /// 等同源异义字符串绕过。改为先 <see cref="Uri.TryCreate(string, UriKind, out Uri?)"/>
    /// 解析为绝对 URI，再精确比较 Scheme/Host；无法解析的串拒绝。
    /// </summary>
    private static bool IsLocalOrigin(string origin)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return false;
        }

        // wails:// 是 Wails 自定义 scheme，整个 scheme 视为可信来源。
        // 注意：这里只信任整个 scheme，不再单独校验 host；如需限定 host 范围，
        // 可在 Uri.TryCreate 后与下方 http/https 分支同等处理。
        if (origin.StartsWith("wails://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            // 非绝对 URL 或解析失败（包含控制字符、空格、畸形 scheme 等），一律拒绝。
            return false;
        }

        // Uri.Scheme 对标准 scheme 返回小写；非标准 scheme 保留原始大小写，故用 OrdinalIgnoreCase。
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            // 仅信任精确的 loopback host；端口不限，留给上层策略决定。
            return IsLoopbackHost(uri.Host);
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            // Wails v3 的官方 loopback HTTPS 域。
            return string.Equals(uri.Host, "wails.localhost", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// 检查 host 是否为标准 loopback（localhost / 127.0.0.1 / [::1]）。
    /// Uri.Host 已做 IDN/punycode 规范化且对 IPv6 自动剥方括号，可直接 Ordinal 比较。
    /// </summary>
    private static bool IsLoopbackHost(string host)
    {
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
    }
}
