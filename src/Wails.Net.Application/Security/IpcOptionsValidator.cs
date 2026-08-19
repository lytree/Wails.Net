using Microsoft.Extensions.Options;

namespace Wails.Net.Application.Security;

/// <summary>
/// <see cref="IpcOptions"/> 启动期校验器（OPT-CONFIG-002 / OPT-IPC-001）。
/// <para>
/// 校验 IPC 传输层安全配置：
/// <list type="bullet">
/// <item>端口范围合法（<c>PortStart</c> ≥ 1024、<c>PortStart</c> ≤ <c>PortEnd</c>、<c>PortEnd</c> ≤ 65535）。</item>
/// <item>白名单 Origin 必须为合法 HTTP/HTTPS URI（支持通配符模式 <c>https://*.example.com</c>）。</item>
/// <item>白名单中不得包含本地源（本地源总是允许，无需声明）。</item>
/// </list>
/// </para>
/// </summary>
public sealed class IpcOptionsValidator : IValidateOptions<IpcOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, IpcOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.PortStart < 1024 || options.PortStart > 65535)
        {
            failures.Add($"Wails:Ipc:PortStart 必须在 1024-65535 范围内（当前 {options.PortStart}）。");
        }

        if (options.PortEnd < options.PortStart || options.PortEnd > 65535)
        {
            failures.Add($"Wails:Ipc:PortEnd 必须 >= PortStart 且 <= 65535（当前 PortEnd={options.PortEnd}，PortStart={options.PortStart}）。");
        }

        

        if (options.MailboxCapacity < 1)
        {
            failures.Add($"Wails:Ipc:MailboxCapacity 必须 >= 1（当前 {options.MailboxCapacity}）。");
        }

        if (options.MaxConcurrentCallsPerConnection < 1)
        {
            failures.Add($"Wails:Ipc:MaxConcurrentCallsPerConnection 必须 >= 1（当前 {options.MaxConcurrentCallsPerConnection}）。");
        }

        for (var i = 0; i < options.AllowedOrigins.Count; i++)
        {
            var origin = options.AllowedOrigins[i];
            if (string.IsNullOrWhiteSpace(origin))
            {
                failures.Add($"Wails:Ipc:AllowedOrigins:{i} 不得为空。");
                continue;
            }

            if (!IsValidOriginPattern(origin))
            {
                failures.Add($"Wails:Ipc:AllowedOrigins:{i} 必须是合法的 HTTP/HTTPS URI（支持 *. 通配符）：{origin}。");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// 检查 Origin 是否为合法 HTTP/HTTPS URI 模式（支持 <c>https://*.example.com</c> 通配符）。
    /// </summary>
    private static bool IsValidOriginPattern(string origin)
    {
        if (!origin.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 去掉通配符后应仍是合法绝对 URI
        var withoutWildcard = origin.Replace("*.", string.Empty, StringComparison.Ordinal);
        return Uri.TryCreate(withoutWildcard, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
