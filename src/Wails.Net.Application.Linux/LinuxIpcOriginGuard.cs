namespace Wails.Net.Application.Platform;

/// <summary>
/// Linux（WebKitGTK）IPC 请求来源闸门（§11.2 第 3 项 / GHSA-2p5f-2gx8-mh8m 缓解）。
/// <para>
/// WebKitGTK 的 <c>wails://</c> 自定义协议由 <c>WebContext</c> 全局注册，任何帧（含跨源子帧）
/// 都可以向后端 <c>/wails/*</c> 端点发起 <c>no-cors</c> POST。由于 <c>URISchemeRequest</c>
/// 不暴露发起帧（frame）信息，本闸门退而校验请求头 <c>Origin</c> 是否与窗口当前页面同源：
/// </para>
/// <list type="bullet">
/// <item>仅拦截 IPC 请求（<c>POST /wails/*</c>），静态资源与 Streams 端点不受影响；</item>
/// <item><c>Origin</c> 头存在时：必须与窗口当前页面同源（scheme + host + port，大小写不敏感，
/// 忽略 <c>http/https</c> 的默认端口）；</item>
/// <item>不透明源（<c>Origin: null</c>，sandbox / <c>data:</c> 子帧）：<c>Sec-Fetch-Site</c> 表明跨源上下文
/// 时拒绝；否则仅当窗口 URL 协议为 <c>wails</c> 时容忍——WebKitGTK 可能把自定义协议页面的 Origin
/// 序列化为字面量 <c>null</c>，若一律拒绝会破坏应用自身 IPC，此时改由 <see cref="LinuxFrameGuard"/>
/// 脚本守卫兜底；其它协议（<c>http/https/about</c>）下的 <c>null</c> 源一律拒绝；</item>
/// <item><c>Origin</c> 缺失时按 <c>Sec-Fetch-Site</c> 兜底：<c>cross-site</c> / <c>same-site</c> 拒绝；
/// 两者都缺失视为 WebKitGTK 未透传头部（旧版本），放行以保持兼容
/// （此时由 <see cref="LinuxFrameGuard"/> 注入的脚本守卫作为纵深防御）。</item>
/// </list>
/// <para>
/// <b>已知平台限制</b>：同源子帧与主帧共享 Origin，无法也不需要在请求层区分（同源子帧本就可访问
/// 主帧 DOM 与运行时）；WebKitGTK 若完全不透传 <c>Origin</c>/<c>Sec-Fetch-Site</c>，
/// 则此层无法判定帧来源，退化为脚本守卫（上游 Wails v3 同样未解决该限制）。
/// </para>
/// </summary>
internal static class LinuxIpcOriginGuard
{
    /// <summary>
    /// IPC 端点路径前缀（与 <c>WebviewRequest.IsIpcRequest</c> 一致）。
    /// </summary>
    internal const string IpcPathPrefix = "/wails/";

    /// <summary>
    /// IPC 请求方法（仅接受 POST）。
    /// </summary>
    internal const string IpcMethod = "POST";

    /// <summary>
    /// 请求来源头名称。
    /// </summary>
    internal const string OriginHeaderName = "Origin";

    /// <summary>
    /// 浏览器请求上下文头名称（无 Origin 时兜底判定）。
    /// </summary>
    internal const string SecFetchSiteHeaderName = "Sec-Fetch-Site";

    /// <summary>
    /// 不透明来源（sandbox 子帧 / data: URL）的 Origin 头取值。
    /// </summary>
    private const string OpaqueOrigin = "null";

    private const string SchemeSeparator = "://";

    /// <summary>
    /// 判断请求是否为 IPC 请求（<c>POST /wails/*</c>）。
    /// </summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">请求路径。</param>
    /// <returns>IPC 请求返回 true。</returns>
    internal static bool IsIpcRequest(string? method, string? path)
    {
        return string.Equals(method, IpcMethod, StringComparison.OrdinalIgnoreCase)
            && path is not null
            && path.StartsWith(IpcPathPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 校验请求是否允许访问 IPC 端点。
    /// </summary>
    /// <param name="method">HTTP 方法。</param>
    /// <param name="path">请求路径。</param>
    /// <param name="headers">请求头（大小写不敏感，可为 null）。</param>
    /// <param name="windowUrl">窗口当前页面的 URL（用于同源比较，可为 null）。</param>
    /// <returns>允许返回 true；可疑的跨源 IPC 请求返回 false（调用方应返回 403）。</returns>
    internal static bool IsIpcRequestAllowed(
        string? method,
        string? path,
        IReadOnlyDictionary<string, string>? headers,
        string? windowUrl)
    {
        if (!IsIpcRequest(method, path))
        {
            return true;
        }

        var origin = TryGetHeader(headers, OriginHeaderName);
        if (string.IsNullOrWhiteSpace(origin))
        {
            // WebKitGTK 未透传 Origin 时按 Sec-Fetch-Site 兜底；两者都没有则无法判定，放行（兼容）。
            var secFetchSite = TryGetHeader(headers, SecFetchSiteHeaderName);
            if (string.IsNullOrWhiteSpace(secFetchSite))
            {
                return true;
            }

            return !IsCrossSiteContext(secFetchSite);
        }

        var trimmedOrigin = origin.Trim();
        if (string.Equals(trimmedOrigin, OpaqueOrigin, StringComparison.OrdinalIgnoreCase))
        {
            // 不透明源：sandbox / data: 子帧，或 WebKit 对自定义协议页面序列化的 "null"。
            return IsOpaqueOriginTolerated(windowUrl, TryGetHeader(headers, SecFetchSiteHeaderName));
        }

        if (!TryNormalizeOrigin(trimmedOrigin, out var requestOrigin))
        {
            return false;
        }

        if (!TryNormalizeOrigin(windowUrl, out var windowOrigin))
        {
            // 窗口尚未导航（无 URL）时无法比较，放行以保持兼容。
            return true;
        }

        return string.Equals(requestOrigin, windowOrigin, StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断不透明源（<c>Origin: null</c>）是否可容忍。
    /// <para>
    /// <c>Sec-Fetch-Site</c> 表明跨源上下文（<c>cross-site</c> / <c>same-site</c>）时一律拒绝；
    /// 否则仅当窗口 URL 协议为 <c>wails</c> 时容忍：WebKitGTK 可能把自定义协议页面的 Origin
    /// 序列化为字面量 <c>null</c>，拒绝会破坏应用自身 IPC，此时由脚本守卫兜底。
    /// </para>
    /// </summary>
    /// <param name="windowUrl">窗口当前页面的 URL。</param>
    /// <param name="secFetchSite"><c>Sec-Fetch-Site</c> 头值（可为 null）。</param>
    /// <returns>容忍返回 true。</returns>
    private static bool IsOpaqueOriginTolerated(string? windowUrl, string? secFetchSite)
    {
        if (!string.IsNullOrWhiteSpace(secFetchSite) && IsCrossSiteContext(secFetchSite))
        {
            return false;
        }

        return TryGetScheme(windowUrl, out var scheme)
            && string.Equals(scheme, "wails", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 提取 URL 的协议（<c>://</c> 之前的部分）。
    /// </summary>
    /// <param name="value">URL 值。</param>
    /// <param name="scheme">协议名（小写，如 <c>wails</c>）。</param>
    /// <returns>解析成功返回 true。</returns>
    private static bool TryGetScheme(string? value, out string scheme)
    {
        scheme = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var schemeIndex = text.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (schemeIndex <= 0)
        {
            return false;
        }

        scheme = text[..schemeIndex].ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// 判断 <c>Sec-Fetch-Site</c> 是否表示跨源上下文。
    /// </summary>
    private static bool IsCrossSiteContext(string secFetchSite)
    {
        return string.Equals(secFetchSite.Trim(), "cross-site", StringComparison.OrdinalIgnoreCase)
            || string.Equals(secFetchSite.Trim(), "same-site", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 将 URL 或 Origin 值归一化为 <c>scheme://host[:port]</c> 形式（小写、去除路径/查询/片段、
    /// 去除 http/https 默认端口）。
    /// </summary>
    /// <param name="value">URL 或 Origin 头值。</param>
    /// <param name="normalized">归一化后的来源（如 <c>wails://localhost</c>）。</param>
    /// <returns>解析成功返回 true。</returns>
    internal static bool TryNormalizeOrigin(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var schemeIndex = text.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (schemeIndex <= 0)
        {
            return false;
        }

        var scheme = text[..schemeIndex];
        var rest = text[(schemeIndex + SchemeSeparator.Length)..];

        var end = rest.IndexOfAny(['/', '?', '#']);
        if (end >= 0)
        {
            rest = rest[..end];
        }

        // 去掉可能的 userinfo（用户信息不参与同源判定）。
        var at = rest.LastIndexOf('@');
        if (at >= 0)
        {
            rest = rest[(at + 1)..];
        }

        if (rest.Length == 0)
        {
            return false;
        }

        var authority = rest.ToLowerInvariant();
        authority = StripDefaultPort(scheme, authority);
        normalized = scheme.ToLowerInvariant() + SchemeSeparator + authority;
        return true;
    }

    /// <summary>
    /// 去除 http/https 的默认端口（80/443），使 <c>http://localhost:80</c> 与 <c>http://localhost</c> 等价。
    /// </summary>
    private static string StripDefaultPort(string scheme, string authority)
    {
        var defaultPort = scheme.ToLowerInvariant() switch
        {
            "http" => ":80",
            "https" => ":443",
            _ => null,
        };

        if (defaultPort is not null && authority.EndsWith(defaultPort, StringComparison.Ordinal))
        {
            return authority[..^defaultPort.Length];
        }

        return authority;
    }

    /// <summary>
    /// 以大小写不敏感方式读取请求头。
    /// </summary>
    private static string? TryGetHeader(IReadOnlyDictionary<string, string>? headers, string name)
    {
        if (headers is null || headers.Count == 0)
        {
            return null;
        }

        if (headers.TryGetValue(name, out var direct))
        {
            return direct;
        }

        foreach (var pair in headers)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
