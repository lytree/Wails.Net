using Microsoft.Extensions.Options;

namespace Wails.Net.Application.Hosting;

/// <summary>
/// <see cref="DesktopHostOptions"/> 启动期校验器（OPT-CONFIG-002）。
/// <para>
/// 在 Host 启动期（<c>ValidateOnStart()</c>）校验核心配置，将配置错误前置到启动阶段，
/// 避免延迟到窗口创建、AssetServer 启动或 IPC 初始化时才暴露。
/// 错误消息包含具体配置路径（如 <c>Wails:Window:Width</c>），便于定位。
/// </para>
/// </summary>
public sealed class DesktopHostOptionsValidator : IValidateOptions<DesktopHostOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DesktopHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApplicationName))
        {
            failures.Add("Wails:ApplicationName 不能为空。");
        }

        if (options.Window.Width <= 0)
        {
            failures.Add("Wails:Window:Width 必须大于 0。");
        }

        if (options.Window.Height <= 0)
        {
            failures.Add("Wails:Window:Height 必须大于 0。");
        }

        if (string.IsNullOrWhiteSpace(options.Assets.DefaultDocument))
        {
            failures.Add("Wails:Assets:DefaultDocument 不能为空。");
        }

        if (!string.IsNullOrWhiteSpace(options.Assets.RootPath) && !IsValidPath(options.Assets.RootPath))
        {
            failures.Add($"Wails:Assets:RootPath 路径格式无效：{options.Assets.RootPath}。");
        }

        if (!string.IsNullOrWhiteSpace(options.DevServerUrl) &&
            !IsValidHttpUrl(options.DevServerUrl))
        {
            failures.Add($"Wails:DevServerUrl 必须是合法的 HTTP/HTTPS 绝对 URI：{options.DevServerUrl}。");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// 检查路径字符串是否包含非法路径字符。
    /// </summary>
    private static bool IsValidPath(string path)
    {
        return path.IndexOfAny(Path.GetInvalidPathChars()) < 0;
    }

    /// <summary>
    /// 检查字符串是否为合法的 HTTP/HTTPS 绝对 URI。
    /// </summary>
    private static bool IsValidHttpUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }
}
