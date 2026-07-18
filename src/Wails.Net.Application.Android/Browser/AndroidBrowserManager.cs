using Android.Content;
using Android.Net;
using Wails.Net.Application.Browser;
using Wails.Net.Application.Managers;
using Uri = Android.Net.Uri;

namespace Wails.Net.Application.Browser;

/// <summary>
/// Android 浏览器管理器实现。
/// 对应 Wails v3 Go 版本 internal/browser 包 Android 实现。
/// 通过 Android <c>Intent.ActionView</c> 隐式启动系统默认浏览器。
/// </summary>
public sealed class AndroidBrowserManager : IBrowserManager
{
    /// <inheritdoc />
    public void OpenURL(string url) => OpenWithIntent(url);

    /// <inheritdoc />
    public void OpenURLInDefaultBrowser(string url) => OpenWithIntent(url);

    /// <summary>
    /// 通过 <c>Intent.ActionView</c> 启动浏览器 Activity。
    /// 对应 Go 版本调用 <c>ctx.StartActivity(intent)</c>。
    /// </summary>
    /// <param name="url">待打开的 URL。</param>
    private static void OpenWithIntent(string url)
    {
        if (!BrowserUrlValidator.TryValidate(url, out var sanitized))
        {
            return;
        }

        try
        {
            var context = global::Android.App.Application.Context;
            if (context is null)
            {
                return;
            }

            var uri = Uri.Parse(sanitized);
            if (uri is null)
            {
                return;
            }

            var intent = new Intent(Intent.ActionView, uri);
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            // 无浏览器应用可处理此 Intent，静默忽略
        }
        catch (Java.Lang.SecurityException)
        {
            // 安全策略限制，静默忽略
        }
    }
}
