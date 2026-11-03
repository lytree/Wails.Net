using System.Net;

namespace Wails.Net.Application.Security;

/// <summary>
/// Server 模式 HTTP/WebSocket 请求安全闸门（P0 安全加固）。
/// 对应 Wails v3 `v3.0.0` 官方 Go 源码：
/// <list type="bullet">
/// <item><c>v3/pkg/application/host_guard_server.go</c> 的 <c>serverHostAllowed</c> / <c>requireAllowedHost</c>；</item>
/// <item><c>v3/pkg/application/transport_http_server.go</c> 的 <c>checkRuntimeRequest</c> / <c>authorizeRuntimeOrigin</c>；</item>
/// <item><c>v3/pkg/application/websocket_server.go</c> 的 <c>websocketAcceptOptions</c>。</item>
/// </list>
/// <para>
/// 两条规则（仅在 Server 模式装配；裸构造传输层时不启用，保持低层 API 向后兼容）：
/// <list type="number">
/// <item><b>DNS rebinding 防护</b>：绑定 loopback 时，<c>Host</c> 头只接受
/// <c>localhost</c>、任意 IP 字面量、绑定主机，或命中 <see cref="IpcOptions.AllowedOrigins"/>
/// （对应 Go 的 <c>ServerOptions.WebSocketOriginPatterns</c>）的名字；其余返回 403。</item>
/// <item><b>Runtime 端点 Origin 校验</b>：<c>/wails/message</c> 只接受 POST；
/// 请求携带 <c>Origin</c> 时必须与请求 Host 同源或命中白名单；
/// 无 <c>Origin</c> 时按 <c>Sec-Fetch-Site</c> 判定（<c>cross-site</c>/<c>same-site</c> 拒绝）。</item>
/// </list>
/// </para>
/// <para>
/// 白名单模式语法对齐 Go 的 <c>path.Match</c> 子集：支持 <c>*</c> 通配符（匹配任意字符），
/// 且模式可带 <c>scheme://</c> 前缀（匹配时剥离前缀，与 Go 的 <c>serverHostAllowed</c> 一致）。
/// 匹配大小写不敏感。
/// </para>
/// </summary>
public sealed class IpcRequestGuard
{
    /// <summary>
    /// 唯一天然 loopback 主机名：任意其它名字都可能被 DNS 解析改指向。
    /// 对应 Go <c>localhostName</c>。
    /// </summary>
    private const string LocalhostName = "localhost";

    private const string HttpSchemeSeparator = "://";

    private readonly List<string> _patterns = new();

    /// <summary>
    /// 构造请求安全闸门。
    /// </summary>
    /// <param name="allowedPatterns">
    /// 允许的 Origin / Host 模式（对应 Go 的 <c>ServerOptions.WebSocketOriginPatterns</c>，
    /// Wails.Net 中来自 <see cref="IpcOptions.AllowedOrigins"/>）。空/空白项忽略。
    /// </param>
    /// <param name="bindHost">
    /// 服务器绑定主机（默认 <c>localhost</c>，与 <c>HttpTransport</c>/<c>WebSocketTransport</c>
    /// 的 <c>http://localhost:{Port}</c> 前缀一致）。非 loopback 绑定时 Host 校验自动关闭。
    /// </param>
    public IpcRequestGuard(IEnumerable<string>? allowedPatterns = null, string? bindHost = null)
    {
        BindHost = string.IsNullOrWhiteSpace(bindHost) ? LocalhostName : bindHost.Trim();

        if (allowedPatterns is not null)
        {
            foreach (var pattern in allowedPatterns)
            {
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    _patterns.Add(pattern.Trim());
                }
            }
        }
    }

    /// <summary>
    /// 获取服务器绑定主机。
    /// </summary>
    public string BindHost { get; }

    /// <summary>
    /// 获取或设置是否跳过 Origin 校验（对应 Go 的 <c>ServerOptions.WebSocketAllowAllOrigins</c>）。
    /// Wails.Net 映射为 <see cref="IpcOptions.RequireOriginValidation"/> 取反；
    /// 即使跳过 Origin 校验，loopback 绑定下的 Host 校验仍然生效（与 Go 一致）。
    /// </summary>
    public bool AllowAllOrigins { get; init; }

    /// <summary>
    /// 获取是否启用 Host 校验（仅 loopback 绑定时启用，与 Go <c>requireAllowedHost</c> 一致）。
    /// </summary>
    public bool IsHostCheckEnabled => IsLoopbackBindHost(BindHost);

    /// <summary>
    /// 判断绑定主机是否只监听 loopback：<c>localhost</c> 或 loopback IP 字面量。
    /// 对应 Go <c>isLoopbackBindHost</c>：其它名字视为有意部署到网络，不做 Host 校验。
    /// </summary>
    /// <param name="bindHost">绑定主机。</param>
    /// <returns>loopback 绑定返回 true。</returns>
    public static bool IsLoopbackBindHost(string? bindHost)
    {
        if (string.IsNullOrWhiteSpace(bindHost))
        {
            return false;
        }

        var host = TrimBrackets(bindHost.Trim());
        if (string.Equals(host, LocalhostName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IsLoopbackAddress(address);
    }

    /// <summary>
    /// 校验请求 <c>Host</c> 头是否允许（DNS rebinding 防护）。
    /// 对应 Go <c>serverHostAllowed</c>：
    /// <list type="bullet">
    /// <item>空 Host（仅 HTTP/1.0 客户端会省略）放行；</item>
    /// <item><c>localhost</c> 放行；</item>
    /// <item>任意 IP 字面量放行（IP 无法被 DNS 改写）；</item>
    /// <item>绑定主机放行；</item>
    /// <item>模式白名单命中（分别匹配带端口与不带端口的 Host）放行；</item>
    /// <item>其余拒绝（含尾点、简写 IP 等拼写变体）。</item>
    /// </list>
    /// 端口不参与校验。
    /// </param>
    /// <param name="requestHost">原始 <c>Host</c> 头值（可含端口）。</param>
    /// <returns>允许返回 true。</returns>
    public bool IsHostAllowed(string? requestHost)
    {
        if (!IsHostCheckEnabled)
        {
            return true;
        }

        if (string.IsNullOrEmpty(requestHost))
        {
            // 浏览器总会发送 Host；仅 HTTP/1.0 客户端会省略。
            return true;
        }

        var host = ExtractHost(requestHost);

        if (string.Equals(host, LocalhostName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out _))
        {
            return true;
        }

        if (string.Equals(host, TrimBrackets(BindHost), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return MatchesPattern(requestHost) || MatchesPattern(host);
    }

    /// <summary>
    /// 校验 runtime 端点（<c>/wails/message</c>，对应 Go 的 <c>/wails/runtime</c>）的请求来源。
    /// 对应 Go <c>authorizeRuntimeOrigin</c>（POST 方法校验由传输层单独处理）。
    /// </summary>
    /// <param name="origin"><c>Origin</c> 头值，可为 null/空。</param>
    /// <param name="secFetchSite"><c>Sec-Fetch-Site</c> 头值，可为 null/空。</param>
    /// <param name="requestHost">原始 <c>Host</c> 头值。</param>
    /// <returns>允许返回 true。</returns>
    public bool IsRuntimeRequestAllowed(string? origin, string? secFetchSite, string? requestHost)
    {
        if (AllowAllOrigins)
        {
            return true;
        }

        if (string.IsNullOrEmpty(origin))
        {
            // 浏览器的跨源 POST 总会带 Origin；Sec-Fetch-Site 兜底个别不带 Origin 的情况。
            // 两者都没有则视为非浏览器客户端（同 Wails v3）。
            if (string.IsNullOrEmpty(secFetchSite))
            {
                return true;
            }

            return !string.Equals(secFetchSite, "cross-site", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(secFetchSite, "same-site", StringComparison.OrdinalIgnoreCase);
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || string.IsNullOrEmpty(originUri.Host))
        {
            return false;
        }

        var requestHostValue = requestHost?.Trim();
        if (!string.IsNullOrEmpty(requestHostValue) &&
            string.Equals(requestHostValue, originUri.Authority, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return MatchesOriginPattern(originUri);
    }

    /// <summary>
    /// 校验 WebSocket 升级请求的 <c>Origin</c>。
    /// 对齐 <c>coder/websocket</c> 的 <c>OriginPatterns</c> 语义：
    /// 请求 Host 恒允许；其余 Origin 必须命中模式白名单；<see cref="AllowAllOrigins"/> 时跳过。
    /// </summary>
    /// <param name="origin"><c>Origin</c> 头值，可为 null/空。</param>
    /// <param name="requestHost">原始 <c>Host</c> 头值。</param>
    /// <returns>允许返回 true。</returns>
    public bool IsWebSocketOriginAllowed(string? origin, string? requestHost)
        => IsRuntimeRequestAllowed(origin, secFetchSite: null, requestHost);

    /// <summary>
    /// 校验 <c>HttpListenerRequest</c> 的 <c>Host</c> 头。
    /// </summary>
    /// <param name="request">HTTP 请求。</param>
    /// <returns>允许返回 true。</returns>
    public bool IsHostAllowed(HttpListenerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsHostAllowed(request.Headers["Host"]);
    }

    /// <summary>
    /// 校验 <c>HttpListenerRequest</c> 是否允许访问 runtime 消息端点。
    /// </summary>
    /// <param name="request">HTTP 请求。</param>
    /// <returns>允许返回 true。</returns>
    public bool IsRuntimeRequestAllowed(HttpListenerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsRuntimeRequestAllowed(
            request.Headers["Origin"],
            request.Headers["Sec-Fetch-Site"],
            request.Headers["Host"]);
    }

    /// <summary>
    /// 校验 <c>HttpListenerRequest</c> 的 WebSocket 升级来源。
    /// </summary>
    /// <param name="request">HTTP 请求。</param>
    /// <returns>允许返回 true。</returns>
    public bool IsWebSocketOriginAllowed(HttpListenerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsWebSocketOriginAllowed(request.Headers["Origin"], request.Headers["Host"]);
    }

    /// <summary>
    /// Origin 模式匹配。对应 Go <c>authorizeRuntimeOrigin</c>：
    /// <list type="bullet">
    /// <item>模式含 <c>scheme://</c> 时匹配 <c>scheme://host:port</c>（scheme 必须一致）；</item>
    /// <item>模式不含 scheme 时匹配 <c>host:port</c>。</item>
    /// </list>
    /// </summary>
    private bool MatchesOriginPattern(Uri originUri)
    {
        if (_patterns.Count == 0)
        {
            return false;
        }

        var authority = originUri.Authority;
        foreach (var pattern in _patterns)
        {
            var target = pattern.Contains(HttpSchemeSeparator, StringComparison.Ordinal)
                ? originUri.Scheme + HttpSchemeSeparator + authority
                : authority;

            if (GlobMatches(pattern, target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Host 模式匹配：剥离模式的 <c>scheme://</c> 前缀后，对目标做 <c>*</c> 通配匹配（大小写不敏感）。
    /// 对应 Go <c>serverHostAllowed</c> 中 <c>strings.Cut(pattern, "://")</c> 的用法。
    /// </summary>
    private bool MatchesPattern(string target)
    {
        if (_patterns.Count == 0 || string.IsNullOrEmpty(target))
        {
            return false;
        }

        foreach (var pattern in _patterns)
        {
            var normalized = pattern;
            var schemeIndex = normalized.IndexOf(HttpSchemeSeparator, StringComparison.Ordinal);
            if (schemeIndex >= 0)
            {
                normalized = normalized[(schemeIndex + HttpSchemeSeparator.Length)..];
            }

            if (GlobMatches(normalized, target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>*</c> 通配匹配（<c>*</c> 匹配任意字符，大小写不敏感）。对应 Go <c>path.Match</c> 的常用子集。
    /// </summary>
    private static bool GlobMatches(string pattern, string target)
    {
        var regex = "^"
            + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*", StringComparison.Ordinal)
            + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(
            target,
            regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// 从 Host 头提取主机名（去掉端口与 IPv6 方括号）。对应 Go <c>net.SplitHostPort</c> 分支。
    /// </summary>
    private static string ExtractHost(string requestHost)
    {
        var value = requestHost.Trim();

        if (value.StartsWith('['))
        {
            var end = value.IndexOf(']');
            return end > 1 ? value[1..end] : TrimBrackets(value);
        }

        var colon = value.LastIndexOf(':');
        return colon > 0 ? value[..colon] : value;
    }

    /// <summary>
    /// 去掉首尾方括号（IPv6 字面量）。
    /// </summary>
    private static string TrimBrackets(string value)
        => value.Trim().Trim('[', ']');

    /// <summary>
    /// 判断 IP 地址是否 loopback（127.0.0.0/8 或 ::1）。
    /// </summary>
    private static bool IsLoopbackAddress(IPAddress address)
    {
        if (address.IsIPv6LinkLocal)
        {
            return false;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.Equals(IPAddress.IPv6Loopback);
        }

        return address.Equals(IPAddress.Loopback);
    }
}
