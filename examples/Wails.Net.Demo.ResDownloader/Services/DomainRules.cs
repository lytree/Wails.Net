using System.Net;

namespace Wails.Net.Demo.ResDownloader.Services;

/// <summary>
/// 域名规则集（对应 res-downloader 的 <c>Rule</c> 配置）。
/// <para>
/// 每行一条规则，自上而下匹配、后者覆盖前者：
/// <c>*</c> 匹配全部；<c>*.qq.com</c> 匹配 qq.com 及其子域；<c>video.qq.com</c> 精确匹配；
/// 以 <c>!</c> 开头表示排除。<c>#</c> 开头为注释。默认动作是「不解密」（直接透传）。
/// </para>
/// </summary>
public sealed class DomainRules
{
    private sealed record Rule(string Raw, bool Negated, bool All, bool Wildcard, string Domain);

    private readonly Rule[] _rules;

    private DomainRules(Rule[] rules) => _rules = rules;

    /// <summary>默认规则：全部域名都做 MITM（仅当用户清空规则时使用）。</summary>
    public static DomainRules MatchAll { get; } = new([new Rule("*", false, true, false, string.Empty)]);

    /// <summary>
    /// 解析规则文本。
    /// </summary>
    /// <param name="text">规则文本，可为空。</param>
    /// <returns>规则集；文本为空时返回匹配全部。</returns>
    public static DomainRules Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return MatchAll;
        }

        var rules = new List<Rule>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var negated = false;
            if (line.StartsWith('!'))
            {
                negated = true;
                line = line[1..].Trim();
                if (line.Length == 0)
                {
                    continue;
                }
            }

            if (line == "*")
            {
                rules.Add(new Rule("*", negated, true, false, string.Empty));
                continue;
            }

            var wildcard = line.StartsWith("*.", StringComparison.Ordinal);
            var domain = wildcard ? line[2..] : line;
            rules.Add(new Rule(line, negated, false, wildcard, domain.ToLowerInvariant()));
        }

        return rules.Count == 0 ? MatchAll : new DomainRules([.. rules]);
    }

    /// <summary>
    /// 判断某个主机是否需要 MITM（解密后嗅探）。
    /// </summary>
    /// <param name="host">主机名，可带端口或 IPv6 方括号。</param>
    /// <returns>需要解密返回 <c>true</c>；直接透传返回 <c>false</c>。</returns>
    public bool ShouldMitm(string host)
    {
        var name = NormalizeHost(host);
        var action = false;

        foreach (var rule in _rules)
        {
            if (rule.All)
            {
                action = !rule.Negated;
                continue;
            }

            if (rule.Wildcard)
            {
                if (name.Equals(rule.Domain, StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("." + rule.Domain, StringComparison.OrdinalIgnoreCase))
                {
                    action = !rule.Negated;
                }

                continue;
            }

            if (name.Equals(rule.Domain, StringComparison.OrdinalIgnoreCase))
            {
                action = !rule.Negated;
            }
        }

        return action;
    }

    /// <summary>提取主机名（去端口、去 IPv6 方括号、转小写）。</summary>
    /// <param name="host">主机或 host:port。</param>
    /// <returns>规范化主机名。</returns>
    public static string NormalizeHost(string host)
    {
        var value = (host ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (value[0] == '[')
        {
            var end = value.IndexOf(']', StringComparison.Ordinal);
            if (end > 0)
            {
                return value[1..end].ToLowerInvariant();
            }
        }

        if (IPAddress.TryParse(value, out var address))
        {
            return address.ToString().ToLowerInvariant();
        }

        var colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon)
        {
            value = value[..colon];
        }

        return value.ToLowerInvariant();
    }
}
