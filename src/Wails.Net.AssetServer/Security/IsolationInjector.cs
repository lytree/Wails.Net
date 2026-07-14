using System.Text.RegularExpressions;

namespace Wails.Net.AssetServer.Security;

/// <summary>
/// Isolation Pattern 注入器。
/// 对应 Tauri v2 的 Isolation 模式：在 HTML body 起始处插入隔离 iframe。
/// <para>
/// 通过 <c>&lt;iframe src="<isolationSrc>" name="<frameName>" sandbox="<sandbox>"&gt;</c>
/// 将敏感 JS 代码隔离在独立 iframe 中执行，sandbox 属性限制其能力。
/// </para>
/// </summary>
public static class IsolationInjector
{
    /// <summary>
    /// 匹配 HTML body 开标签。
    /// 仅匹配第一个 body 标签（HTML 文档只能有一个 body）。
    /// </summary>
    private static readonly Regex BodyTagRegex = new(
        @"<body([^>]*)>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// 在 HTML body 起始处插入 isolation iframe。
    /// </summary>
    /// <param name="html">原始 HTML 字符串。</param>
    /// <param name="options">Isolation 选项。</param>
    /// <returns>插入 iframe 后的 HTML 字符串。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="html"/> 或 <paramref name="options"/> 为 null 时抛出。</exception>
    /// <exception cref="InvalidOperationException">当 HTML 中未找到 body 标签时抛出。</exception>
    public static string InjectIsolationIframe(string html, IsolationOptions options)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(options);

        if (html.Length == 0)
        {
            return html;
        }

        var match = BodyTagRegex.Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                "HTML 中未找到 <body> 标签，无法注入 isolation iframe");
        }

        var iframe = BuildIframeTag(options);
        // 在 body 开标签后立即插入 iframe（作为 body 的第一个子元素）
        var insertPosition = match.Index + match.Length;
        return html.Insert(insertPosition, iframe);
    }

    /// <summary>
    /// 构建 isolation iframe 标签字符串。
    /// </summary>
    /// <param name="options">Isolation 选项。</param>
    /// <returns>完整的 iframe 标签字符串。</returns>
    private static string BuildIframeTag(IsolationOptions options)
    {
        var src = options.IsolationSrc;
        var name = options.FrameName;
        var sandbox = options.Sandbox;

        return $"<iframe src=\"{src}\" name=\"{name}\" sandbox=\"{sandbox}\"" +
               " style=\"display:none;width:0;height:0;border:0;\"></iframe>";
    }

    /// <summary>
    /// 验证 Isolation 资源文件是否存在。
    /// 检查 <paramref name="rootPath"/>/<paramref name="options"/>.IsolationDir/index.html 是否存在。
    /// </summary>
    /// <param name="rootPath">资源根路径。</param>
    /// <param name="options">Isolation 选项。</param>
    /// <returns>若 index.html 存在返回 true；否则返回 false。</returns>
    public static bool ValidateIsolationFiles(string rootPath, IsolationOptions options)
    {
        ArgumentNullException.ThrowIfNull(rootPath);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(options.IsolationDir))
        {
            return false;
        }

        var indexHtmlPath = Path.Combine(rootPath, options.IsolationDir, "index.html");
        return File.Exists(indexHtmlPath);
    }
}
