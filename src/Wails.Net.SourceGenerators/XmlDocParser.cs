using System.Text.RegularExpressions;

namespace Wails.Net.SourceGenerators;

/// <summary>
/// XML 文档注释解析器：从 <c>GetDocumentationCommentXml()</c> 返回的 XML 中提取
/// <c>&lt;summary&gt;</c> 与 <c>&lt;param&gt;</c> 的纯文本内容，供 TypeScript 绑定生成保留注释。
/// </summary>
internal static partial class XmlDocParser
{
    // <summary>...</summary>（Singleline 模式跨行匹配）
    [GeneratedRegex(@"<summary>(.*?)</summary>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SummaryRegex();

    // <param name="xxx">...</param>
    [GeneratedRegex(@"<param\s+name=""([^""]*)""\s*>(.*?)</param>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ParamRegex();

    /// <summary>
    /// 提取 XML 文档中的方法摘要纯文本。
    /// </summary>
    /// <param name="docXml">GetDocumentationCommentXml() 返回的 XML（可为空）。</param>
    /// <returns>摘要纯文本；无摘要返回 null。</returns>
    public static string? ExtractSummary(string? docXml)
    {
        if (string.IsNullOrEmpty(docXml))
        {
            return null;
        }

        var m = SummaryRegex().Match(docXml);
        return m.Success ? Clean(m.Groups[1].Value) : null;
    }

    /// <summary>
    /// 提取 XML 文档中指定参数的摘要纯文本。
    /// </summary>
    /// <param name="docXml">GetDocumentationCommentXml() 返回的 XML（可为空）。</param>
    /// <param name="paramName">参数名。</param>
    /// <returns>参数摘要纯文本；无对应注释返回 null。</returns>
    public static string? ExtractParam(string? docXml, string paramName)
    {
        if (string.IsNullOrEmpty(docXml))
        {
            return null;
        }

        foreach (Match m in ParamRegex().Matches(docXml))
        {
            if (m.Groups[1].Value == paramName)
            {
                return Clean(m.Groups[2].Value);
            }
        }

        return null;
    }

    /// <summary>
    /// 清理注释文本：去空白行、压缩多余空白、转义换行（单行 JSDoc 友好）。
    /// </summary>
    private static string Clean(string text)
    {
        var lines = text
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        if (lines.Length == 0)
        {
            return string.Empty;
        }

        // 多行合并为单行（保留空格分隔），便于输出单行 JSDoc
        return string.Join(" ", lines);
    }
}
