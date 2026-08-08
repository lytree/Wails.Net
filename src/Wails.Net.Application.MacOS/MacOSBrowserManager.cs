using Wails.Net.Application.Browser;
using Wails.Net.Application.Managers;

namespace Wails.Net.Application.Browser;

/// <summary>
/// macOS 浏览器管理器实现。
/// 对应 Wails v3 Go 版本 internal/browser 包 macOS 实现，
/// 通过 <c>NSWorkspace.SharedWorkspace.OpenUrl</c> 在默认浏览器中打开 URL。
/// </summary>
public sealed class MacOSBrowserManager : IBrowserManager
{
    /// <inheritdoc />
    public void OpenURL(string url) => OpenInDefaultBrowser(url);

    /// <inheritdoc />
    public void OpenURLInDefaultBrowser(string url) => OpenInDefaultBrowser(url);

    /// <summary>
    /// 使用 NSWorkspace 打开 URL。
    /// </summary>
    /// <param name="url">待验证并打开的 URL。</param>
    private static void OpenInDefaultBrowser(string url)
    {
#if MACOS
        if (!BrowserUrlValidator.TryValidate(url, out var sanitized))
        {
            return;
        }

        using var nsUrl = Foundation.NSUrl.FromString(sanitized);
        if (nsUrl is not null)
        {
            AppKit.NSWorkspace.SharedWorkspace.OpenUrl(nsUrl);
        }
#else
        // 非 macOS 目标：no-op 骨架。
#endif
    }
}
